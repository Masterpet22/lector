using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Implementación Singleton de caché LRU acotada a una ventana de 3 páginas [actual - 1, actual, actual + 1].
/// Invoca Dispose() de forma determinista sobre los bitmaps expulsados para evitar OutOfMemoryException.
/// </summary>
public sealed class PageCacheService : IPageCacheService, IDisposable
{
    private readonly Dictionary<(string BookId, int PageIndex), SKBitmap> _entries = new();
    private readonly object _syncRoot = new();

    public int MaxCachedPages => 3;

    public bool TryDrawBitmap(string bookId, int pageIndex, Action<SKBitmap> drawAction)
    {
        ArgumentNullException.ThrowIfNull(drawAction);

        lock (_syncRoot)
        {
            if (_entries.TryGetValue((bookId, pageIndex), out var bitmap))
            {
                drawAction(bitmap);
                return true;
            }
            return false;
        }
    }

    public bool ContainsPage(string bookId, int pageIndex)
    {
        lock (_syncRoot)
        {
            return _entries.ContainsKey((bookId, pageIndex));
        }
    }

    public void StoreBitmap(string bookId, int pageIndex, SKBitmap bitmap, int centerPageIndex, int radius = 1)
    {
        StoreBitmapInRange(bookId, pageIndex, bitmap, centerPageIndex - radius, centerPageIndex + radius);
    }

    public void StoreBitmapInRange(string bookId, int pageIndex, SKBitmap bitmap, int minKeepPageIndex, int maxKeepPageIndex)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        lock (_syncRoot)
        {
            var key = (bookId, pageIndex);
            if (_entries.TryGetValue(key, out var previous) && !ReferenceEquals(previous, bitmap))
            {
                previous.Dispose();
            }

            _entries[key] = bitmap;
            EvictOutsideWindowCore(bookId, minKeepPageIndex, maxKeepPageIndex);
        }
    }

    public void EvictOutsideWindow(string bookId, int centerPageIndex, int radius = 1)
    {
        EvictOutsideRange(bookId, centerPageIndex - radius, centerPageIndex + radius);
    }

    public void EvictOutsideRange(string bookId, int minKeepPageIndex, int maxKeepPageIndex)
    {
        lock (_syncRoot)
        {
            EvictOutsideWindowCore(bookId, minKeepPageIndex, maxKeepPageIndex);
        }
    }

    public void ClearBook(string bookId)
    {
        lock (_syncRoot)
        {
            var keysToRemove = _entries.Keys.Where(k => k.BookId == bookId).ToList();
            foreach (var key in keysToRemove)
            {
                if (_entries.Remove(key, out var bitmap))
                {
                    bitmap.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            foreach (var bitmap in _entries.Values)
            {
                bitmap.Dispose();
            }
            _entries.Clear();
        }
    }

    private void EvictOutsideWindowCore(string bookId, int minKeepPageIndex, int maxKeepPageIndex)
    {
        var keysToRemove = _entries.Keys
            .Where(k => k.BookId == bookId && (k.PageIndex < minKeepPageIndex || k.PageIndex > maxKeepPageIndex))
            .ToList();

        foreach (var key in keysToRemove)
        {
            if (_entries.Remove(key, out var bitmap))
            {
                bitmap.Dispose();
            }
        }
    }
}
