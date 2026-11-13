using SkiaSharp;

namespace NebulaReader.Services;

/// <summary>
/// Extensión opcional de IBookSource para decodificar o renderizar páginas directamente en SKBitmap
/// evitando compresión/descompresión intermedia.
/// </summary>
public interface ISkiaBookSource : IBookSource
{
    Task<SKBitmap?> GetPageBitmapAsync(int pageIndex, int maxDimensionPx, CancellationToken ct = default);
}

