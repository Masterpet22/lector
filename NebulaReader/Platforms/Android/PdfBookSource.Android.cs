using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using SkiaSharp;
using AndroidColor = Android.Graphics.Color;
using AndroidUri = Android.Net.Uri;
using AppContext = Android.App.Application;

namespace NebulaReader.Services;

public sealed partial class PdfBookSource
{
    private ParcelFileDescriptor? _fileDescriptor;
    private PdfRenderer? _renderer;
    private string? _tempFilePath;
    private readonly object _renderLock = new();

    private async partial Task InitializeFromPathOrUriAsync(string filePathOrUri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (filePathOrUri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = AndroidUri.Parse(filePathOrUri)
                ?? throw new InvalidOperationException($"URI inválida: {filePathOrUri}");

            var resolver = AppContext.Context.ContentResolver
                ?? throw new InvalidOperationException("ContentResolver no disponible en Android.");

            _fileDescriptor = resolver.OpenFileDescriptor(uri, "r");
            if (_fileDescriptor == null)
            {
                await using var inputStream = resolver.OpenInputStream(uri)
                    ?? throw new InvalidOperationException($"No se pudo abrir la URI PDF: {filePathOrUri}");
                await InitializeFromStreamAsync(inputStream, ct).ConfigureAwait(false);
                return;
            }
        }
        else
        {
            var localPath = filePathOrUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(filePathOrUri).LocalPath
                : filePathOrUri;

            _fileDescriptor = ParcelFileDescriptor.Open(new Java.IO.File(localPath), ParcelFileMode.ReadOnly)
                ?? throw new FileNotFoundException("No se pudo abrir el archivo PDF.", localPath);
        }

        _renderer = new PdfRenderer(_fileDescriptor);
        PageCount = _renderer.PageCount;
    }

    private async partial Task InitializeFromStreamAsync(Stream stream, CancellationToken ct)
    {
        _tempFilePath = System.IO.Path.Combine(FileSystem.CacheDirectory, $"nebula_pdf_{Guid.NewGuid():N}.pdf");
        await using (var tempFile = File.Create(_tempFilePath))
        {
            await stream.CopyToAsync(tempFile, ct).ConfigureAwait(false);
        }

        _fileDescriptor = ParcelFileDescriptor.Open(new Java.IO.File(_tempFilePath), ParcelFileMode.ReadOnly)
            ?? throw new InvalidOperationException("No se pudo abrir el PDF temporal en Android.");
        _renderer = new PdfRenderer(_fileDescriptor);
        PageCount = _renderer.PageCount;
    }

    private partial Task<Stream> RenderPagePlatformAsync(int pageIndex, double density, int? maxWidthPx, CancellationToken ct)
    {
        return Task.Run<Stream>(() =>
        {
            ct.ThrowIfCancellationRequested();

            lock (_renderLock)
            {
                using var bitmap = RenderAndroidBitmap(pageIndex, density, maxWidthPx, DefaultMaxRenderDimensionPx);
                var memoryStream = new MemoryStream();
                var format = maxWidthPx.HasValue ? Bitmap.CompressFormat.Jpeg! : Bitmap.CompressFormat.Png!;
                var quality = maxWidthPx.HasValue ? 85 : 100;
                bitmap.Compress(format, quality, memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
        }, ct);
    }

    private partial Task<SKBitmap?> RenderPageBitmapPlatformAsync(int pageIndex, double density, int maxDimensionPx, CancellationToken ct)
    {
        return Task.Run<SKBitmap?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            lock (_renderLock)
            {
                using var bitmap = RenderAndroidBitmap(pageIndex, density, maxWidthPx: null, maxDimensionPx);
                return CopyAndroidBitmapToSkBitmap(bitmap);
            }
        }, ct);
    }

    private Bitmap RenderAndroidBitmap(int pageIndex, double density, int? maxWidthPx, int maxDimensionPx)
    {
        ObjectDisposedException.ThrowIf(_disposed || _renderer == null, this);

        using var page = _renderer.OpenPage(pageIndex);
        var (width, height) = CalculateTargetSize(page.Width, page.Height, density, maxWidthPx, maxDimensionPx);

        var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException("No se pudo asignar el bitmap para renderizar la página PDF.");

        using (var canvas = new Canvas(bitmap))
        {
            canvas.DrawColor(AndroidColor.White);
        }

        page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
        return bitmap;
    }

    private static unsafe SKBitmap CopyAndroidBitmapToSkBitmap(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var skBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        var srcPtr = bitmap.LockPixels();
        if (srcPtr == IntPtr.Zero)
        {
            skBitmap.Dispose();
            throw new InvalidOperationException("No se pudo bloquear los píxeles del bitmap PDF en Android.");
        }

        try
        {
            var dstPtr = skBitmap.GetPixels();
            var srcRowBytes = bitmap.RowBytes;
            var dstRowBytes = skBitmap.RowBytes;

            if (srcRowBytes == dstRowBytes)
            {
                var totalBytes = (long)srcRowBytes * height;
                Buffer.MemoryCopy((void*)srcPtr, (void*)dstPtr, totalBytes, totalBytes);
            }
            else
            {
                var copyBytes = Math.Min(srcRowBytes, dstRowBytes);
                for (var row = 0; row < height; row++)
                {
                    Buffer.MemoryCopy(
                        (byte*)srcPtr + ((long)row * srcRowBytes),
                        (byte*)dstPtr + ((long)row * dstRowBytes),
                        copyBytes,
                        copyBytes);
                }
            }
        }
        finally
        {
            bitmap.UnlockPixels();
        }

        return skBitmap;
    }

    private partial Task<float[]?> GetPageAspectRatiosPlatformAsync(CancellationToken ct)
    {
        return Task.Run<float[]?>(() =>
        {
            var ratios = new float[PageCount];
            var index = 0;

            while (index < ratios.Length)
            {
                if (ct.IsCancellationRequested || _disposed) return null;

                lock (_renderLock)
                {
                    if (_disposed || _renderer == null) return null;

                    var batchEnd = Math.Min(ratios.Length, index + 25);
                    for (; index < batchEnd; index++)
                    {
                        if (ct.IsCancellationRequested) return null;
                        using var page = _renderer.OpenPage(index);
                        ratios[index] = page.Width > 0 ? (float)page.Height / page.Width : 1.414f;
                    }
                }
            }

            return ratios;
        }, ct);
    }

    private partial void DisposePlatform()
    {
        lock (_renderLock)
        {
            _renderer?.Close();
            _renderer?.Dispose();
            _renderer = null;

            _fileDescriptor?.Close();
            _fileDescriptor?.Dispose();
            _fileDescriptor = null;

            if (!string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
            {
                try
                {
                    File.Delete(_tempFilePath);
                }
                catch
                {
                    // Ignorar fallo de limpieza de archivo temporal
                }
            }
        }
    }
}

