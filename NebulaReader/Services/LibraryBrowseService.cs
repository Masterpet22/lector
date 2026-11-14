using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>Consultas de presentación que conservan la identidad de los libros y sus series.</summary>
public static class LibraryBrowseService
{
    public static IReadOnlyList<LibraryGroupBucket<BookItem>> FilterGroups(
        IEnumerable<BookItem> books, string? searchText = null,
        BookFormat? format = null, ReadingStatus? status = null)
    {
        var query = searchText?.Trim() ?? string.Empty;
        // Agrupar antes de filtrar conserva la serie aunque solo coincida un volumen.
        var groups = BookMetadataExtractor.BuildLibraryGroups(
            books.ToList(), b => b.Title, b => b.SeriesName, b => b.Tags);

        return groups.Select(group => group with
        {
            Items = group.Items.Where(book =>
                (!format.HasValue || book.Format == format.Value) &&
                (!status.HasValue || book.Status == status.Value) &&
                (query.Length == 0 || Contains(book.Title, query) ||
                    Contains(book.SeriesName, query) || Contains(book.Tags, query) ||
                    (group.IsGroup && Contains(group.DisplayName, query)))).ToList()
        })
        .Where(group => group.Items.Count > 0)
        .Select(group => group with
        {
            CommonTags = group.Items.SelectMany(book =>
                    (book.Tags ?? string.Empty).Split([',', ';'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList()
        }).ToList();
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
