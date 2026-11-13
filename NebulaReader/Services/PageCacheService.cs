namespace NebulaReader.Services;

/// <summary>
/// Implementación Singleton de caché LRU acotada para prevenir OutOfMemoryException.
/// </summary>
public class PageCacheService : IPageCacheService
{
    private readonly Dictionary<(string BookId, int PageIndex), byte[]> _entries = new();
    private readonly object _syncRoot = new();

    public int MaxCachedPages => 3;

    public bool TryGetPageBytes(string bookId, int pageIndex, out byte[]? data)
    {
        lock (_syncRoot)
        {
            return _entries.TryGetValue((bookId, pageIndex), out data);
        }
    }

    public void StorePageBytes(string bookId, int pageIndex, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_syncRoot)
        {
            _entries[(bookId, pageIndex)] = data;
        }
    }

    public void EvictOutsideWindow(string bookId, int centerPageIndex, int radius = 1)
    {
        lock (_syncRoot)
        {
            var keysToRemove = _entries.Keys
                .Where(k => k.BookId == bookId && Math.Abs(k.PageIndex - centerPageIndex) > radius)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _entries.Remove(key);
            }
        }
    }

    public void ClearBook(string bookId)
    {
        lock (_syncRoot)
        {
            var keysToRemove = _entries.Keys.Where(k => k.BookId == bookId).ToList();
            foreach (var key in keysToRemove)
            {
                _entries.Remove(key);
            }
        }
    }
}

