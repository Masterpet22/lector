namespace NebulaReader.Models;

/// <summary>
/// Representa un cómic o documento indexado en la biblioteca local.
/// </summary>
public class BookItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public BookFormat Format { get; set; } = BookFormat.Unknown;
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? CoverThumbnailPath { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastReadAtUtc { get; set; }
    public ReadingStatus Status { get; set; } = ReadingStatus.Unread;
}

