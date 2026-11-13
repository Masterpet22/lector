namespace NebulaReader.Services;

/// <summary>
/// Contrato unificado para leer páginas de archivos CBZ, CBR y PDF sin exponer el formato al visor.
/// </summary>
public interface IBookSource : IDisposable
{
    string Title { get; }
    int PageCount { get; }
    Task<Stream> GetPageStreamAsync(int pageIndex, CancellationToken ct = default);
    Task<Stream> GetCoverThumbnailAsync();
}

