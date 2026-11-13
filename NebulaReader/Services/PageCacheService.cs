using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Implementación Singleton de caché LRU acotada a una ventana de 3 páginas [actual - 1, actual, actual + 1].
/// Invoca Dispose() de forma determinista sobre los bitmaps expulsados para evitar OutOfMemoryException.
/// </summary>
public sealed class PageCacheService : IPageCacheService, IDisposable
{
    private sealed class CacheEntry(SKBitmap bitmap, SKImage? image)
    {
        private int _pinCount = 1;

        public SKBitmap Bitmap { get; } = bitmap;
        public SKImage? Image { get; } = image;
        public SKBitmap? FittedBitmap { get; set; }
        public int FittedWidth { get; set; }

        public bool TryPin()
        {
            while (true)
            {
                var current = Volatile.Read(ref _pinCount);
                if (current <= 0) return false;
                if (Interlocked.CompareExchange(ref _pinCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public void Unpin()
        {
            if (Interlocked.Decrement(ref _pinCount) == 0)
            {
                FittedBitmap?.Dispose();
                FittedBitmap = null;
                Image?.Dispose();
                Bitmap.Dispose();
            }
        }
    }

    private sealed class EntryPin(CacheEntry entry) : IDisposable
    {
        private CacheEntry? _entry = entry;

        public void Dispose()
        {
            Interlocked.Exchange(ref _entry, null)?.Unpin();
        }
    }

    private readonly Dictionary<(string BookId, int PageIndex), CacheEntry> _entries = new();
    private readonly object _syncRoot = new();

    public int MaxCachedPages => 3;

    public bool TryDrawBitmap(string bookId, int pageIndex, Action<SKBitmap> drawAction)
    {
        ArgumentNullException.ThrowIfNull(drawAction);
        return TryDrawPage(bookId, pageIndex, preferredFitWidthPx: null, (_, bitmap) => drawAction(bitmap));
    }

    public bool TryDrawPage(string bookId, int pageIndex, int? preferredFitWidthPx, Action<SKImage?, SKBitmap> drawAction)
    {
        ArgumentNullException.ThrowIfNull(drawAction);

        lock (_syncRoot)
        {
            if (_entries.TryGetValue((bookId, pageIndex), out var entry))
            {
                var useFitted = preferredFitWidthPx is > 64
                    && entry.FittedBitmap != null
                    && Math.Abs(entry.FittedWidth - preferredFitWidthPx.Value) <= 4;

                drawAction(entry.Image, useFitted ? entry.FittedBitmap! : entry.Bitmap);
                return true;
            }
            return false;
        }
    }

    public IDisposable? TryPinBitmap(string bookId, int pageIndex, out SKBitmap? bitmap)
    {
        lock (_syncRoot)
        {
            if (_entries.TryGetValue((bookId, pageIndex), out var entry) && entry.TryPin())
            {
                bitmap = entry.Bitmap;
                return new EntryPin(entry);
            }
        }

        bitmap = null;
        return null;
    }

    public void SetFittedBitmap(string bookId, int pageIndex, SKBitmap fittedBitmap, int fittedWidthPx)
    {
        ArgumentNullException.ThrowIfNull(fittedBitmap);

        SKBitmap? staleFitted = null;
        lock (_syncRoot)
        {
            if (_entries.TryGetValue((bookId, pageIndex), out var entry))
            {
                if (!ReferenceEquals(entry.FittedBitmap, fittedBitmap))
                {
                    staleFitted = entry.FittedBitmap;
                    entry.FittedBitmap = fittedBitmap;
                    entry.FittedWidth = fittedWidthPx;
                }
            }
            else
            {
                staleFitted = fittedBitmap;
            }
        }

        staleFitted?.Dispose();
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
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        var nextEntry = new CacheEntry(bitmap, image);

        List<CacheEntry>? evicted = null;
        lock (_syncRoot)
        {
            var key = (bookId, pageIndex);
            if (_entries.TryGetValue(key, out var previous) && !ReferenceEquals(previous.Bitmap, bitmap))
            {
                evicted ??= [];
                evicted.Add(previous);
            }

            _entries[key] = nextEntry;
            CollectEvictedOutsideWindowCore(bookId, minKeepPageIndex, maxKeepPageIndex, ref evicted);
        }

        DisposeEntries(evicted);
    }

    public void EvictOutsideWindow(string bookId, int centerPageIndex, int radius = 1)
    {
        EvictOutsideRange(bookId, centerPageIndex - radius, centerPageIndex + radius);
    }

    public void EvictOutsideRange(string bookId, int minKeepPageIndex, int maxKeepPageIndex)
    {
        List<CacheEntry>? evicted = null;
        lock (_syncRoot)
        {
            CollectEvictedOutsideWindowCore(bookId, minKeepPageIndex, maxKeepPageIndex, ref evicted);
        }

        DisposeEntries(evicted);
    }

    public void ClearBook(string bookId)
    {
        List<CacheEntry>? removed = null;
        lock (_syncRoot)
        {
            var keysToRemove = _entries.Keys.Where(k => k.BookId == bookId).ToList();
            foreach (var key in keysToRemove)
            {
                if (_entries.Remove(key, out var entry))
                {
                    removed ??= [];
                    removed.Add(entry);
                }
            }
        }

        DisposeEntries(removed);
    }

    public void Dispose()
    {
        List<CacheEntry>? all = null;
        lock (_syncRoot)
        {
            if (_entries.Count > 0)
            {
                all = _entries.Values.ToList();
                _entries.Clear();
            }
        }

        DisposeEntries(all);
    }

    private void CollectEvictedOutsideWindowCore(
        string bookId,
        int minKeepPageIndex,
        int maxKeepPageIndex,
        ref List<CacheEntry>? evicted)
    {
        var keysToRemove = _entries.Keys
            .Where(k => k.BookId == bookId && (k.PageIndex < minKeepPageIndex || k.PageIndex > maxKeepPageIndex))
            .ToList();

        foreach (var key in keysToRemove)
        {
            if (_entries.Remove(key, out var entry))
            {
                evicted ??= [];
                evicted.Add(entry);
            }
        }
    }

    private static void DisposeEntries(List<CacheEntry>? entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
        {
            entry.Unpin();
        }
    }
}
