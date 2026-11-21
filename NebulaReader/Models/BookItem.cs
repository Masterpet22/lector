using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace NebulaReader.Models;

/// <summary>
/// Representa un cómic o documento indexado en la biblioteca local y persistido en SQLite.
/// </summary>
[Table("Books")]
public partial class BookItem : ObservableObject
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    [Indexed(Name = "IX_Books_FilePath", Unique = true)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Ubicación anterior mostrada al asociar un libro restaurado de otro dispositivo.</summary>
    public string? OriginalFilePath { get; set; }

    public BookFormat Format { get; set; } = BookFormat.Unknown;

    [ObservableProperty]
    public partial int TotalPages { get; set; }

    [ObservableProperty]
    public partial int CurrentPage { get; set; } = 1;

    [ObservableProperty]
    public partial string? CoverThumbnailPath { get; set; }

    [ObservableProperty]
    public partial string? SeriesName { get; set; }

    [ObservableProperty]
    public partial string? Tags { get; set; }

    public bool MetadataExtracted { get; set; }

    public bool TitleEditedManually { get; set; }
    public bool SeriesEditedManually { get; set; }
    public bool TagsEditedManually { get; set; }
    public string? CoverError { get; set; }
    public string? ContentFingerprint { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    public partial DateTime? LastReadAtUtc { get; set; }

    [ObservableProperty]
    public partial ReadingStatus Status { get; set; } = ReadingStatus.Unread;
}
