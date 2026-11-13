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
        await _renderGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed || _pdfDocument == null, this);

            using var page = _pdfDocument.GetPage((uint)pageIndex);
            var (width, height) = CalculateTargetSize(page.Size.Width, page.Size.Height, density, maxWidthPx);

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)width,
                DestinationHeight = (uint)height,
                BackgroundColor = WindowsColor.FromArgb(255, 255, 255, 255),
                BitmapEncoderId = maxWidthPx.HasValue ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId
            };

            using var winrtStream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(winrtStream, options).AsTask(ct).ConfigureAwait(false);
            winrtStream.Seek(0);

            var output = new MemoryStream();
            await winrtStream.AsStreamForRead().CopyToAsync(output, ct).ConfigureAwait(false);
            output.Position = 0;
            return output;
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private partial void DisposePlatform()
    {
        _pdfDocument = null;
        _ownedStream?.Dispose();
        _ownedStream = null;
        _renderGate.Dispose();
    }
}

