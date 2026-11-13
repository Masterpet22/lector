using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Caché en memoria (ventana deslizante LRU de 3 páginas) con liberación explícita de bitmaps SkiaSharp.
/// </summary>
public interface IPageCacheService
{
    int MaxCachedPages { get; }
    bool TryDrawBitmap(string bookId, int pageIndex, Action<SKBitmap> drawAction);
    bool TryDrawPage(string bookId, int pageIndex, int? preferredFitWidthPx, Action<SKImage?, SKBitmap> drawAction);
    IDisposable? TryPinBitmap(string bookId, int pageIndex, out SKBitmap? bitmap);
    void SetFittedBitmap(string bookId, int pageIndex, SKBitmap fittedBitmap, int fittedWidthPx);
    bool ContainsPage(string bookId, int pageIndex);
    void StoreBitmap(string bookId, int pageIndex, SKBitmap bitmap, int centerPageIndex, int radius = 1);
    void StoreBitmapInRange(string bookId, int pageIndex, SKBitmap bitmap, int minKeepPageIndex, int maxKeepPageIndex);
    void EvictOutsideWindow(string bookId, int centerPageIndex, int radius = 1);
    void EvictOutsideRange(string bookId, int minKeepPageIndex, int maxKeepPageIndex);
    void ClearBook(string bookId);
}
