using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Caché en memoria (ventana deslizante LRU de 3 páginas) con liberación explícita de bitmaps SkiaSharp.
/// </summary>
public interface IPageCacheService
{
    int MaxCachedPages { get; }
    bool TryDrawBitmap(string bookId, int pageIndex, Action<SKBitmap> drawAction);
    bool ContainsPage(string bookId, int pageIndex);
    void StoreBitmap(string bookId, int pageIndex, SKBitmap bitmap, int centerPageIndex, int radius = 1);
    void EvictOutsideWindow(string bookId, int centerPageIndex, int radius = 1);
    void ClearBook(string bookId);
}
