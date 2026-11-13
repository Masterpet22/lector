namespace NebulaReader.Services;

/// <summary>
/// Caché en memoria (ventana deslizante LRU) para páginas decodificadas del visor.
/// </summary>
public interface IPageCacheService
{
    int MaxCachedPages { get; }
    bool TryGetPageBytes(string bookId, int pageIndex, out byte[]? data);
    void StorePageBytes(string bookId, int pageIndex, byte[] data);
    void EvictOutsideWindow(string bookId, int centerPageIndex, int radius = 1);
    void ClearBook(string bookId);
}

