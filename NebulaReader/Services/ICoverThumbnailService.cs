namespace NebulaReader.Services;

/// <summary>
/// Contrato para extraer la página 0, redimensionarla y almacenarla en caché local como miniatura de portada.
/// </summary>
public interface ICoverThumbnailService
{
    Task<(string? ThumbnailPath, int PageCount)> GenerateCoverAsync(
        string bookId,
        string filePathOrUri,
        CancellationToken ct = default);
}

