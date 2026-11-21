using NebulaReader.Models;

namespace NebulaReader.Services;

public enum LibraryGrouping { Series, Tags, Documents }

public sealed record TagFilterOption(string Label, string? Tag = null, bool WithoutTags = false);

public sealed record LibraryBrowseResult(IReadOnlyList<LibraryGroupBucket<BookItem>> Groups, string? ActiveGroupKey);

/// <summary>Consultas de presentación que conservan la identidad de los libros y sus series.</summary>
public static class LibraryBrowseService
{
    public static LibraryBrowseResult Browse(IEnumerable<BookItem> books, string? activeGroupKey = null,
        string? searchText = null, BookFormat? format = null, ReadingStatus? status = null,
        string? tag = null, bool withoutTags = false, LibraryGrouping grouping = LibraryGrouping.Series)
    {
        var source = books.ToArray();
        var visible = FilterGroups(source, searchText, format, status, tag, withoutTags, grouping);
        bool ContainsActiveGroup(IEnumerable<LibraryGroupBucket<BookItem>> groups) => groups.Any(group =>
            group.IsGroup && string.Equals(group.Key, activeGroupKey, StringComparison.OrdinalIgnoreCase));
        // Una búsqueda puede ocultar un grupo existente; una edición o eliminación puede deshacerlo.
        var activeExists = activeGroupKey != null && (ContainsActiveGroup(visible) ||
            ContainsActiveGroup(FilterGroups(source, grouping: grouping)));
        return new(visible, activeExists ? activeGroupKey : null);
    }

    public static TagFilterOption AllTags { get; } = new("Todas las etiquetas");
    public static TagFilterOption Untagged { get; } = new("Sin etiquetas", WithoutTags: true);

    public static IReadOnlyList<string> GetTags(BookItem book) => (book.Tags ?? string.Empty)
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<TagFilterOption> GetTagOptions(IEnumerable<BookItem> books,
        TagFilterOption? selected = null)
    {
        var options = new List<TagFilterOption> { AllTags, Untagged };
        options.AddRange(books.SelectMany(GetTags).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, NaturalSortComparer.Instance)
            .Select(tag => selected?.Tag != null && string.Equals(selected.Tag, tag, StringComparison.OrdinalIgnoreCase)
                ? selected : new TagFilterOption($"Etiqueta: {tag}", tag)));
        // Conservar el filtro activo si una edición elimina su última coincidencia.
        if (selected?.Tag != null && !options.Contains(selected)) options.Add(selected);
        return options;
    }

    public static IReadOnlyList<LibraryGroupBucket<BookItem>> FilterGroups(
        IEnumerable<BookItem> books, string? searchText = null,
        BookFormat? format = null, ReadingStatus? status = null,
        string? tag = null, bool withoutTags = false, LibraryGrouping grouping = LibraryGrouping.Series)
    {
        var query = searchText?.Trim() ?? string.Empty;
        // Agrupar antes de filtrar conserva la serie aunque solo coincida un volumen.
        var source = books.DistinctBy(book => book.Id).ToList();
        IReadOnlyList<LibraryGroupBucket<BookItem>> groups = grouping switch
        {
            LibraryGrouping.Tags => BuildTagGroups(source),
            LibraryGrouping.Documents => source.OrderBy(book => book.Title, NaturalSortComparer.Instance)
                .ThenBy(book => book.Id, StringComparer.Ordinal)
                .Select(book => new LibraryGroupBucket<BookItem>(false, book.Title, [book], GetTags(book))).ToList(),
            _ => BookMetadataExtractor.BuildLibraryGroups(source, b => b.Title, b => b.SeriesName, b => b.Tags)
        };

        return groups.Select(group => group with
        {
            Items = group.Items.Where(book =>
                (!format.HasValue || book.Format == format.Value) &&
                (!status.HasValue || book.Status == status.Value) &&
                (!withoutTags || GetTags(book).Count == 0) &&
                (string.IsNullOrWhiteSpace(tag) || GetTags(book).Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase)) &&
                (query.Length == 0 || Contains(book.Title, query) ||
                    Contains(book.SeriesName, query) || Contains(book.Tags, query) ||
                    (group.IsGroup && Contains(group.DisplayName, query)))).ToList()
        })
        .Where(group => group.Items.Count > 0)
        .Select(group => group with
        {
            CommonTags = group.Items.SelectMany(GetTags)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList()
        }).ToList();
    }

    private static IReadOnlyList<LibraryGroupBucket<BookItem>> BuildTagGroups(IEnumerable<BookItem> books)
    {
        var groups = new Dictionary<string, List<BookItem>>(StringComparer.OrdinalIgnoreCase);
        var untagged = new List<BookItem>();
        foreach (var book in books.OrderBy(book => book.Title, NaturalSortComparer.Instance)
                     .ThenBy(book => book.Id, StringComparer.Ordinal))
        {
            var tags = GetTags(book);
            if (tags.Count == 0) untagged.Add(book);
            foreach (var tag in tags)
            {
                if (!groups.TryGetValue(tag, out var items)) groups[tag] = items = new();
                items.Add(book);
            }
        }
        var buckets = groups.OrderBy(pair => pair.Key, NaturalSortComparer.Instance)
            .Select(pair => new LibraryGroupBucket<BookItem>(true, pair.Key, pair.Value, [pair.Key])
            { Key = $"tag:{pair.Key}" }).ToList();
        if (untagged.Count > 0)
            buckets.Add(new(true, "Sin etiquetas", untagged, []) { Key = "untagged:" });
        return buckets;
    }

    public static IReadOnlyList<BookItem> GetContinueReading(IEnumerable<BookItem> books) => books
        .Where(book => book.Status == ReadingStatus.Reading &&
            (book.TotalPages <= 0 || book.CurrentPage < book.TotalPages))
        .OrderByDescending(book => book.LastReadAtUtc)
        .ThenBy(book => book.Title, NaturalSortComparer.Instance)
        .ThenBy(book => book.Id, StringComparer.Ordinal)
        .ToList();

    public static double GetProgress(BookItem book) => book.TotalPages > 0
        ? Math.Clamp((double)book.CurrentPage / book.TotalPages, 0, 1)
        : 0;

    public static string GetProgressText(BookItem book) => book.TotalPages > 0
        ? $"Pág. {Math.Clamp(book.CurrentPage, 1, book.TotalPages)}/{book.TotalPages} · {GetProgress(book):P0}"
        : $"Pág. {Math.Max(1, book.CurrentPage)} · Total desconocido";

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
