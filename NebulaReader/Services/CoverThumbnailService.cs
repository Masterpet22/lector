using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Extrae únicamente la página 0 de cada libro, la redimensiona a 300 px de ancho con SkiaSharp
/// y la guarda en FileSystem.CacheDirectory para carga instantánea en la biblioteca.
/// </summary>
public sealed class CoverThumbnailService : ICoverThumbnailService
{
    private const int TargetCoverWidthPx = 300;
    private readonly IBookSourceFactory _bookSourceFactory;
    private readonly string _coversCacheDir;

    public CoverThumbnailService(IBookSourceFactory bookSourceFactory)
    {
        _bookSourceFactory = bookSourceFactory;
        _coversCacheDir = Path.Combine(FileSystem.CacheDirectory, "covers");
    }

    public async Task<(string? ThumbnailPath, int PageCount)> GenerateCoverAsync(
        string bookId,
        string filePathOrUri,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathOrUri);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_coversCacheDir);
        var destinationPath = Path.Combine(_coversCacheDir, $"{bookId}.jpg");

        using var source = await _bookSourceFactory.CreateAsync(filePathOrUri, ct).ConfigureAwait(false);
        var pageCount = source.PageCount;

        if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0)
        {
            return (destinationPath, pageCount);
        }

        if (pageCount <= 0)
        {
            return (null, 0);
        }

        await using var pageStream = await source.GetCoverThumbnailAsync().ConfigureAwait(false);
        if (pageStream.Length == 0)
        {
            return (null, pageCount);
        }

        var saved = await Task.Run(() => ResizeAndSaveCover(pageStream, destinationPath, ct), ct).ConfigureAwait(false);
        return (saved ? destinationPath : null, pageCount);
    }

    private static bool ResizeAndSaveCover(Stream sourceStream, string destinationPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (sourceStream.CanSeek)
        {
            sourceStream.Position = 0;
            using var codec = SKCodec.Create(sourceStream);
            if (codec != null
                && codec.EncodedFormat == SKEncodedImageFormat.Jpeg
                && codec.Info.Width > 0
                && codec.Info.Width <= TargetCoverWidthPx)
            {
                sourceStream.Position = 0;
                return SaveStreamAtomic(sourceStream, destinationPath, ct);
            }

            sourceStream.Position = 0;
        }

        using var original = SKBitmap.Decode(sourceStream);
        if (original == null || original.Width <= 0 || original.Height <= 0)
        {
            return false;
        }

        var targetWidth = Math.Min(TargetCoverWidthPx, original.Width);
        var targetHeight = Math.Max(1, (int)Math.Round(original.Height * (targetWidth / (double)original.Width)));

        SKBitmap bitmapToEncode = original;
        SKBitmap? resized = null;

        try
        {
            if (original.Width != targetWidth || original.Height != targetHeight)
            {
                var info = new SKImageInfo(targetWidth, targetHeight, original.ColorType, original.AlphaType);
                resized = original.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized != null)
                {
                    bitmapToEncode = resized;
                }
            }

            ct.ThrowIfCancellationRequested();

            using var image = SKImage.FromBitmap(bitmapToEncode);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            if (data == null)
            {
                return false;
            }

            using var encodedStream = data.AsStream();
            return SaveStreamAtomic(encodedStream, destinationPath, ct);
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static bool SaveStreamAtomic(Stream input, string destinationPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tempPath = $"{destinationPath}.tmp";
        using (var output = File.Create(tempPath))
        {
            input.CopyTo(output);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        return true;
    }
}

