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
#if ANDROID || WINDOWS
        : this(bookSourceFactory, Path.Combine(FileSystem.CacheDirectory, "covers"))
#else
        : this(bookSourceFactory, Path.Combine(Path.GetTempPath(), "NebulaReader", "covers"))
#endif
    {
    }

    public CoverThumbnailService(IBookSourceFactory bookSourceFactory, string coversCacheDir)
    {
        _bookSourceFactory = bookSourceFactory;
        _coversCacheDir = coversCacheDir;
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
            using var managedStream = new SKManagedStream(sourceStream, disposeManagedStream: false);
            using var codec = SKCodec.Create(managedStream);
            if (codec != null)
            {
                if (codec.EncodedFormat == SKEncodedImageFormat.Jpeg
                    && codec.Info.Width > 0
                    && codec.Info.Width <= TargetCoverWidthPx)
                {
                    sourceStream.Position = 0;
                    return SaveStreamAtomic(sourceStream, destinationPath, ct);
                }

                using var decodedFromCodec = SKBitmap.Decode(codec);
                if (decodedFromCodec != null && decodedFromCodec.Width > 0 && decodedFromCodec.Height > 0)
                {
                    return ResizeBitmapAndSaveCover(decodedFromCodec, destinationPath, ct);
                }
            }

            sourceStream.Position = 0;
        }

        using var original = SKBitmap.Decode(sourceStream);
        if (original == null || original.Width <= 0 || original.Height <= 0)
        {
            return false;
        }

        return ResizeBitmapAndSaveCover(original, destinationPath, ct);
    }

    private static bool ResizeBitmapAndSaveCover(SKBitmap original, string destinationPath, CancellationToken ct)
    {

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

