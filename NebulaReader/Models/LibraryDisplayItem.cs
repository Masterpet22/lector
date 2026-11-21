using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NebulaReader.Services;

namespace NebulaReader.Models;

/// <summary>
/// Elemento visual de la cuadrícula de biblioteca: puede representar un grupo/saga de varios documentos
/// (con mosaico de hasta 4 portadas) o un documento individual.
/// </summary>
public sealed partial class LibraryDisplayItem : ObservableObject
{
    [ObservableProperty]
    public partial double CoverHeight { get; set; } = 200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleBook))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(TitleNumber))]
    [NotifyPropertyChangedFor(nameof(HasTitleNumber))]
    public partial bool IsGroup { get; set; }

    [ObservableProperty]
    public partial string? GroupKey { get; set; }

    public bool IsSingleBook => !IsGroup;

    [ObservableProperty]
    public partial bool CanEditSeriesGroup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionMark))]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionMark))]
    public partial bool IsPartiallySelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSelectionIndicator))]
    public partial bool IsSelectionMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSelectionIndicator))]
    public partial bool IsHovered { get; set; }

    public bool ShowSelectionIndicator => IsSelectionMode || IsHovered;
    public string SelectionMark => IsSelected ? "✓" : IsPartiallySelected ? "−" : string.Empty;

    private string _title = string.Empty;
    private (string Name, string? Number) _titleParts = (string.Empty, null);

    public string Title
    {
        get => _title;
        set
        {
            var next = value ?? string.Empty;
            if ( string.Equals(_title, next, StringComparison.Ordinal)) return;
            _title = next;
            _titleParts = LibraryTitlePresentation.Split(_title);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(TitleNumber));
            OnPropertyChanged(nameof(HasTitleNumber));
        }
    }

    public string DisplayTitle => IsGroup ? Title : _titleParts.Name;
    public string? TitleNumber => IsGroup ? null : _titleParts.Number;
    public bool HasTitleNumber => TitleNumber is not null;

    [ObservableProperty]
    public partial BookItem? Book { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<BookItem> GroupBooks { get; set; } = Array.Empty<BookItem>();

    [ObservableProperty]
    public partial int BookCount { get; set; }

    [ObservableProperty]
    public partial string BadgeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SubtitleText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool HasKnownPageCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTags))]
    public partial string? TagsText { get; set; }

    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);

    [ObservableProperty]
    public partial bool HasCoverError { get; set; }

    [ObservableProperty]
    public partial string? CoverStateText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover1))]
    [NotifyPropertyChangedFor(nameof(CoverCount))]
    [NotifyPropertyChangedFor(nameof(HasOneCover))]
    [NotifyPropertyChangedFor(nameof(HasTwoCovers))]
    [NotifyPropertyChangedFor(nameof(HasThreeCovers))]
    [NotifyPropertyChangedFor(nameof(HasFourCovers))]
    public partial string? Cover1 { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover2))]
    [NotifyPropertyChangedFor(nameof(CoverCount))]
    [NotifyPropertyChangedFor(nameof(HasOneCover))]
    [NotifyPropertyChangedFor(nameof(HasTwoCovers))]
    [NotifyPropertyChangedFor(nameof(HasThreeCovers))]
    [NotifyPropertyChangedFor(nameof(HasFourCovers))]
    public partial string? Cover2 { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover3))]
    [NotifyPropertyChangedFor(nameof(CoverCount))]
    [NotifyPropertyChangedFor(nameof(HasOneCover))]
    [NotifyPropertyChangedFor(nameof(HasTwoCovers))]
    [NotifyPropertyChangedFor(nameof(HasThreeCovers))]
    [NotifyPropertyChangedFor(nameof(HasFourCovers))]
    public partial string? Cover3 { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover4))]
    [NotifyPropertyChangedFor(nameof(CoverCount))]
    [NotifyPropertyChangedFor(nameof(HasOneCover))]
    [NotifyPropertyChangedFor(nameof(HasTwoCovers))]
    [NotifyPropertyChangedFor(nameof(HasThreeCovers))]
    [NotifyPropertyChangedFor(nameof(HasFourCovers))]
    public partial string? Cover4 { get; set; }

    public bool HasCover1 => !string.IsNullOrWhiteSpace(Cover1);
    public bool HasCover2 => !string.IsNullOrWhiteSpace(Cover2);
    public bool HasCover3 => !string.IsNullOrWhiteSpace(Cover3);
    public bool HasCover4 => !string.IsNullOrWhiteSpace(Cover4);
    public int CoverCount => (HasCover1 ? 1 : 0) + (HasCover2 ? 1 : 0) +
        (HasCover3 ? 1 : 0) + (HasCover4 ? 1 : 0);
    public bool HasOneCover => CoverCount == 1;
    public bool HasTwoCovers => CoverCount == 2;
    public bool HasThreeCovers => CoverCount == 3;
    public bool HasFourCovers => CoverCount == 4;

    public string ItemKey => IsGroup ? $"group:{GroupKey ?? Title}" : $"book:{Book?.Id ?? Title}";

    public void UpdateFrom(LibraryDisplayItem other)
    {
        CoverHeight = other.CoverHeight;
        IsGroup = other.IsGroup;
        GroupKey = other.GroupKey;
        CanEditSeriesGroup = other.CanEditSeriesGroup;
        Title = other.Title;
        Book = other.Book;
        GroupBooks = other.GroupBooks;
        BookCount = other.BookCount;
        BadgeText = other.BadgeText;
        SubtitleText = other.SubtitleText;
        Progress = other.Progress;
        HasKnownPageCount = other.HasKnownPageCount;
        TagsText = other.TagsText;
        HasCoverError = other.HasCoverError;
        CoverStateText = other.CoverStateText;
        Cover1 = other.Cover1;
        Cover2 = other.Cover2;
        Cover3 = other.Cover3;
        Cover4 = other.Cover4;
    }

    public static LibraryDisplayItem FromSingleBook(BookItem book, IReadOnlyList<string>? displayTags = null, double coverHeight = 200)
    {
        var needsLocation = book.FilePath.StartsWith("backup://", StringComparison.OrdinalIgnoreCase);
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
            SubtitleText = LibraryBrowseService.GetProgressText(book),
            Progress = LibraryBrowseService.GetProgress(book),
            HasKnownPageCount = book.TotalPages > 0,
            TagsText = tagsText,
            Cover1 = book.CoverThumbnailPath,
            HasCoverError = needsLocation || !string.IsNullOrWhiteSpace(book.CoverError),
            CoverStateText = needsLocation
                ? $"Archivo por asociar · {book.OriginalFilePath ?? "Nombre de archivo desconocido"}"
                : !string.IsNullOrWhiteSpace(book.CoverError) ? "No se pudo generar la portada · Reintentar en Acciones" : null
        };
    }

    public static LibraryDisplayItem FromSeriesGroup(
        string seriesName,
        IReadOnlyList<BookItem> books,
        IReadOnlyList<string>? commonTags = null,
        double coverHeight = 200,
        string? groupKey = null,
        string groupKind = "Saga")
    {
        var covers = books
            .Select(b => b.CoverThumbnailPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Take(4)
            .ToList();

        var tagsText = commonTags is { Count: > 0 }
            ? string.Join(" · ", commonTags.Take(2))
            : null;

        return new LibraryDisplayItem
        {
            CoverHeight = coverHeight,
            IsGroup = true,
            CanEditSeriesGroup = groupKind == "Saga",
            GroupKey = groupKey ?? seriesName,
            Title = seriesName,
            Book = null,
            GroupBooks = books,
            BookCount = books.Count,
            BadgeText = $"{books.Count} docs",
            SubtitleText = $"{groupKind} · {books.Count} documentos",
            TagsText = tagsText,
            Cover1 = covers.Count > 0 ? covers[0] : null,
            Cover2 = covers.Count > 1 ? covers[1] : null,
            Cover3 = covers.Count > 2 ? covers[2] : null,
            Cover4 = covers.Count > 3 ? covers[3] : null
        };
    }
}

/// <summary>Una fila virtualizada de tarjetas con ancho independiente del número de resultados.</summary>
public sealed class LibraryDisplayRow
{
    public ObservableCollection<LibraryDisplayItem> Items { get; }

    public LibraryDisplayRow(IReadOnlyList<LibraryDisplayItem> items)
    {
        Items = new ObservableCollection<LibraryDisplayItem>(items);
    }

    public void SyncItems(IReadOnlyList<LibraryDisplayItem> nextItems)
    {
        for (var i = 0; i < nextItems.Count; i++)
        {
            if (i < Items.Count)
            {
                if (!ReferenceEquals(Items[i], nextItems[i]))
                {
                    Items[i] = nextItems[i];
                }
            }
            else
            {
                Items.Add(nextItems[i]);
            }
        }

        while (Items.Count > nextItems.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }
}
