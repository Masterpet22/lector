namespace NebulaReader.Models;

/// <summary>
/// Almacena el avance de lectura y marcadores de un documento.
/// </summary>
public class ReadingProgress
{
    public string BookId { get; set; } = string.Empty;
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<int> BookmarkedPages { get; set; } = new();

    public double CompletionRatio => TotalPages > 0
        ? Math.Clamp((double)CurrentPage / TotalPages, 0.0, 1.0)
        : 0.0;
}

