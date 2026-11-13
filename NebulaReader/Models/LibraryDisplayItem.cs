using CommunityToolkit.Mvvm.ComponentModel;

namespace NebulaReader.Models;

/// <summary>
/// Elemento visual de la cuadrícula de biblioteca: puede representar un grupo/saga de varios documentos
/// (con mosaico de hasta 4 portadas) o un documento individual.
/// </summary>
public sealed partial class LibraryDisplayItem : ObservableObject
{
    [ObservableProperty]
    public partial double CoverHeight { get; set; } = 200;

    public bool IsGroup { get; init; }
    public bool IsSingleBook => !IsGroup;

    public string Title { get; init; } = string.Empty;
    public BookItem? Book { get; init; }
    public IReadOnlyList<BookItem> GroupBooks { get; init; } = Array.Empty<BookItem>();

    public int BookCount { get; init; }
    public string BadgeText { get; init; } = string.Empty;
    public string SubtitleText { get; init; } = string.Empty;
    public string? TagsText { get; init; }
    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);

    public string? Cover1 { get; init; }
    public string? Cover2 { get; init; }
    public string? Cover3 { get; init; }
    public string? Cover4 { get; init; }

    public bool HasCover1 => !string.IsNullOrWhiteSpace(Cover1);
    public bool HasCover2 => !string.IsNullOrWhiteSpace(Cover2);
    public bool HasCover3 => !string.IsNullOrWhiteSpace(Cover3);
    public bool HasCover4 => !string.IsNullOrWhiteSpace(Cover4);

    public static LibraryDisplayItem FromSingleBook(BookItem book, IReadOnlyList<string>? displayTags = null, double coverHeight = 200)
    {
        var tagsText = displayTags is { Count: > 0 }
            ? string.Join(" · ", displayTags.Take(2))
            : null;

        return new LibraryDisplayItem
        {
            CoverHeight = coverHeight,
            IsGroup = false,
            Title = book.Title,
            Book = book,
            GroupBooks = [book],
            BookCount = 1,
            BadgeText = book.Format.ToString().ToUpperInvariant(),
            SubtitleText = $"Pág. {book.CurrentPage}/{(book.TotalPages > 0 ? book.TotalPages : 1)}",
            TagsText = tagsText,
            Cover1 = book.CoverThumbnailPath
        };
    }

    public static LibraryDisplayItem FromSeriesGroup(
        string seriesName,
        IReadOnlyList<BookItem> books,
        IReadOnlyList<string>? commonTags = null,
        double coverHeight = 200)
    {
        var covers = books
            .Select(b => b.CoverThumbnailPath)
            .Take(4)
            .ToList();

        var tagsText = commonTags is { Count: > 0 }
            ? string.Join(" · ", commonTags.Take(2))
            : null;

        return new LibraryDisplayItem
        {
            CoverHeight = coverHeight,
            IsGroup = true,
            Title = seriesName,
            Book = null,
            GroupBooks = books,
            BookCount = books.Count,
            BadgeText = $"{books.Count} docs",
            SubtitleText = $"Saga · {books.Count} documentos",
            TagsText = tagsText,
            Cover1 = covers.Count > 0 ? covers[0] : null,
            Cover2 = covers.Count > 1 ? covers[1] : null,
            Cover3 = covers.Count > 2 ? covers[2] : null,
            Cover4 = covers.Count > 3 ? covers[3] : null
        };
    }
}

