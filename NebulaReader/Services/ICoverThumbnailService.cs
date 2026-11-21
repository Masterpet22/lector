namespace NebulaReader.Services;

/// <summary>
/// Contrato para detectar la portada, redimensionarla y almacenarla en caché local.
/// </summary>
public interface ICoverThumbnailService
{
    void Invalidate(string bookId);
    void ClearCache();
    bool NeedsRefresh(string bookId, string? thumbnailPath) =>
        string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath);
    Task<(string? ThumbnailPath, int PageCount)> GenerateCoverAsync(
        string bookId,
        string filePathOrUri,
        CancellationToken ct = default);
}

