using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Implementación multiplataforma de ISkiaBookSource para archivos PDF mediante clases parciales nativas.
/// </summary>
public sealed partial class PdfBookSource : ISkiaBookSource
{
    private const int DefaultMaxRenderDimensionPx = 2800;
    private const int ThumbnailWidthPx = 300;
    private const int AspectRatioBatchSize = 8;
    private bool _disposed;
    private int _activeRenderWaiters;
    private double _cachedDensity;

    public string Title { get; }
    public int PageCount { get; private set; }

    private PdfBookSource(string title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Sin título" : title;
    }

    public static async Task<PdfBookSource> OpenAsync(string filePathOrUri, string? title = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathOrUri);

        var resolvedTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePathOrUri)
            : title;

        var source = new PdfBookSource(resolvedTitle);
        await source.InitializeFromPathOrUriAsync(filePathOrUri, ct).ConfigureAwait(false);
        return source;
    }

    public static async Task<PdfBookSource> OpenAsync(string title, Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var source = new PdfBookSource(title);
        await source.InitializeFromStreamAsync(stream, ct).ConfigureAwait(false);
        return source;
    }

    public async Task<Stream> GetPageStreamAsync(int pageIndex, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)pageIndex >= (uint)PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"El índice de página debe estar entre 0 y {Math.Max(0, PageCount - 1)}.");
        }

        ct.ThrowIfCancellationRequested();
        var density = await GetDisplayDensitySafeAsync().ConfigureAwait(false);
        return await RenderPagePlatformAsync(pageIndex, density, maxWidthPx: null, ct).ConfigureAwait(false);
    }

    public async Task<SKBitmap?> GetPageBitmapAsync(int pageIndex, int maxDimensionPx, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)pageIndex >= (uint)PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"El índice de página debe estar entre 0 y {Math.Max(0, PageCount - 1)}.");
        }

        ct.ThrowIfCancellationRequested();
        var density = await GetDisplayDensitySafeAsync().ConfigureAwait(false);
        return await RenderPageBitmapPlatformAsync(pageIndex, density, Math.Max(256, maxDimensionPx), ct).ConfigureAwait(false);
    }

    public Task<float[]?> GetPageAspectRatiosAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PageCount <= 0)
        {
            return Task.FromResult<float[]?>(Array.Empty<float>());
        }

        ct.ThrowIfCancellationRequested();
        return GetPageAspectRatiosPlatformAsync(ct);
    }

    public async Task<Stream> GetCoverThumbnailAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (PageCount == 0)
        {
            return new MemoryStream();
        }

        return await RenderPagePlatformAsync(0, density: 1.0, maxWidthPx: ThumbnailWidthPx, CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposePlatform();
    }

    private static (int Width, int Height) CalculateTargetSize(
        double sourceWidth,
        double sourceHeight,
        double density,
        int? maxWidthPx,
        int maxDimensionPx = DefaultMaxRenderDimensionPx)
    {
        var safeWidth = Math.Max(1.0, sourceWidth);
        var safeHeight = Math.Max(1.0, sourceHeight);

        double targetWidth;
        double targetHeight;

        if (maxWidthPx.HasValue && maxWidthPx.Value > 0)
        {
            var scale = maxWidthPx.Value / safeWidth;
            targetWidth = maxWidthPx.Value;
            targetHeight = safeHeight * scale;
        }
        else
        {
            targetWidth = safeWidth * density;
            targetHeight = safeHeight * density;

            var maxDim = Math.Max(targetWidth, targetHeight);
            if (maxDim > maxDimensionPx)
            {
                var clampScale = maxDimensionPx / maxDim;
                targetWidth *= clampScale;
                targetHeight *= clampScale;
            }
        }

        return (Math.Max(1, (int)Math.Round(targetWidth)), Math.Max(1, (int)Math.Round(targetHeight)));
    }

    private async Task<double> GetDisplayDensitySafeAsync()
    {
        if (_cachedDensity > 0)
        {
            return _cachedDensity;
        }

        try
        {
            double density = 1.5;
            if (MainThread.IsMainThread)
            {
                density = DeviceDisplay.Current.MainDisplayInfo.Density;
            }
            else if (Application.Current != null)
            {
                density = await MainThread.InvokeOnMainThreadAsync(() => DeviceDisplay.Current.MainDisplayInfo.Density).ConfigureAwait(false);
            }

            _cachedDensity = density > 0 ? Math.Clamp(density, 1.35, 2.5) : 1.5;
            return _cachedDensity;
        }
        catch
        {
            // Respaldo seguro si aún no existe ventana/display inicializado
        }

        _cachedDensity = 1.5;
        return 1.5;
    }

    private partial Task InitializeFromPathOrUriAsync(string filePathOrUri, CancellationToken ct);
    private partial Task InitializeFromStreamAsync(Stream stream, CancellationToken ct);
    private partial Task<Stream> RenderPagePlatformAsync(int pageIndex, double density, int? maxWidthPx, CancellationToken ct);
    private partial Task<SKBitmap?> RenderPageBitmapPlatformAsync(int pageIndex, double density, int maxDimensionPx, CancellationToken ct);
    private partial Task<float[]?> GetPageAspectRatiosPlatformAsync(CancellationToken ct);
    private partial void DisposePlatform();
}

