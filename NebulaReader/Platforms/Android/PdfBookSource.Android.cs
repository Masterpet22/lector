using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
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
                ObjectDisposedException.ThrowIf(_disposed || _renderer == null, this);

                using var page = _renderer.OpenPage(pageIndex);
                var (width, height) = CalculateTargetSize(page.Width, page.Height, density, maxWidthPx);

                using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)
                    ?? throw new InvalidOperationException("No se pudo asignar el bitmap para renderizar la página PDF.");

                using (var canvas = new Canvas(bitmap))
                {
                    canvas.DrawColor(AndroidColor.White);
                }

                page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

                var memoryStream = new MemoryStream();
                var format = maxWidthPx.HasValue ? Bitmap.CompressFormat.Jpeg! : Bitmap.CompressFormat.Png!;
                var quality = maxWidthPx.HasValue ? 85 : 100;
                bitmap.Compress(format, quality, memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
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

