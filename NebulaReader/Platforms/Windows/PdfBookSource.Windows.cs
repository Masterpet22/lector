using SkiaSharp;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WindowsColor = Windows.UI.Color;

namespace NebulaReader.Services;

public sealed partial class PdfBookSource
{
    private PdfDocument? _pdfDocument;
    private Stream? _ownedStream;
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    private partial Task InitializeFromPathOrUriAsync(string filePathOrUri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var localPath = filePathOrUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(filePathOrUri).LocalPath
            : filePathOrUri;

        var stream = File.OpenRead(localPath);
        return InitializeFromStreamInternalAsync(stream, ownsStream: true, ct);
    }

    private partial Task InitializeFromStreamAsync(Stream stream, CancellationToken ct)
    {
        return InitializeFromStreamInternalAsync(stream, ownsStream: false, ct);
    }

    private async Task InitializeFromStreamInternalAsync(Stream stream, bool ownsStream, CancellationToken ct)
    {
        Stream randomAccessBacking = stream;
        if (!stream.CanSeek)
        {
            var buffered = new MemoryStream();
            await stream.CopyToAsync(buffered, ct).ConfigureAwait(false);
            buffered.Position = 0;
            if (ownsStream)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            randomAccessBacking = buffered;
            ownsStream = true;
        }

        if (ownsStream)
        {
            _ownedStream = randomAccessBacking;
        }

        _pdfDocument = await PdfDocument.LoadFromStreamAsync(randomAccessBacking.AsRandomAccessStream()).AsTask(ct).ConfigureAwait(false);
        PageCount = (int)_pdfDocument.PageCount;
    }

    private async partial Task<Stream> RenderPagePlatformAsync(int pageIndex, double density, int? maxWidthPx, CancellationToken ct)
    {
        var encoderId = maxWidthPx.HasValue ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId;
        using var winrtStream = new InMemoryRandomAccessStream();
        await RenderPageToWinRtStreamAsync(pageIndex, density, maxWidthPx, DefaultMaxRenderDimensionPx, encoderId, winrtStream, ct).ConfigureAwait(false);
        winrtStream.Seek(0);

        var output = new MemoryStream();
        await winrtStream.AsStreamForRead().CopyToAsync(output, ct).ConfigureAwait(false);
        output.Position = 0;
        return output;
    }

    private async partial Task<SKBitmap?> RenderPageBitmapPlatformAsync(int pageIndex, double density, int maxDimensionPx, CancellationToken ct)
    {
        // BMP sin compresión decodificado directamente desde InMemoryRandomAccessStream
        // evita copia intermedia completa en MemoryStream y codificación deflate.
        using var winrtStream = new InMemoryRandomAccessStream();
        await RenderPageToWinRtStreamAsync(
            pageIndex,
            density,
            maxWidthPx: null,
            maxDimensionPx,
            BitmapEncoder.BmpEncoderId,
            winrtStream,
            ct).ConfigureAwait(false);

        winrtStream.Seek(0);
        using var managedStream = winrtStream.AsStreamForRead();
        return SKBitmap.Decode(managedStream);
    }

    private async Task RenderPageToWinRtStreamAsync(
        int pageIndex,
        double density,
        int? maxWidthPx,
        int maxDimensionPx,
        Guid encoderId,
        InMemoryRandomAccessStream destination,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _activeRenderWaiters);
        try
        {
            await _renderGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed || _pdfDocument == null, this);

                using var page = _pdfDocument.GetPage((uint)pageIndex);
                var (width, height) = CalculateTargetSize(page.Size.Width, page.Size.Height, density, maxWidthPx, maxDimensionPx);

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)width,
                    DestinationHeight = (uint)height,
                    BackgroundColor = WindowsColor.FromArgb(255, 255, 255, 255),
                    BitmapEncoderId = encoderId
                };

                await page.RenderToStreamAsync(destination, options).AsTask(ct).ConfigureAwait(false);
            }
            finally
            {
                _renderGate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeRenderWaiters);
        }
    }

    private async partial Task<float[]?> GetPageAspectRatiosPlatformAsync(CancellationToken ct)
    {
        var ratios = new float[PageCount];
        var index = 0;

        while (index < ratios.Length)
        {
            if (ct.IsCancellationRequested || _disposed) return null;

            while (Volatile.Read(ref _activeRenderWaiters) > 0)
            {
                await Task.Delay(12, ct).ConfigureAwait(false);
            }

            await _renderGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_disposed || _pdfDocument == null) return null;

                var batchEnd = Math.Min(ratios.Length, index + AspectRatioBatchSize);
                for (; index < batchEnd; index++)
                {
                    if (ct.IsCancellationRequested) return null;
                    using var page = _pdfDocument.GetPage((uint)index);
                    ratios[index] = page.Size.Width > 0 ? (float)(page.Size.Height / page.Size.Width) : 1.414f;
                }
            }
            finally
            {
                _renderGate.Release();
            }
        }

        return ratios;
    }

    private partial void DisposePlatform()
    {
        _pdfDocument = null;
        _ownedStream?.Dispose();
        _ownedStream = null;
        _renderGate.Dispose();
    }
}

