using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>Selección de documentos que conserva los identificadores al reconstruir las tarjetas.</summary>
public sealed class LibrarySelectionState
{
    private readonly HashSet<string> _bookIds = new(StringComparer.Ordinal);

    public int Count => _bookIds.Count;
    public IReadOnlyList<string> SelectedIds => _bookIds.ToArray();

    public bool ContainsAll(LibraryDisplayItem item) =>
        item.GroupBooks.Count > 0 && item.GroupBooks.All(book => _bookIds.Contains(book.Id));

    public bool ContainsAny(LibraryDisplayItem item) =>
        item.GroupBooks.Any(book => _bookIds.Contains(book.Id));

    public void Toggle(LibraryDisplayItem item)
    {
        if (item.GroupBooks.Count == 0) return;
        if (ContainsAll(item))
            foreach (var book in item.GroupBooks) _bookIds.Remove(book.Id);
        else
            Add(item);
    }

    public void Add(LibraryDisplayItem item)
    {
        foreach (var book in item.GroupBooks) _bookIds.Add(book.Id);
    }

    public void SelectRange(IReadOnlyList<LibraryDisplayItem> items, int firstIndex, int lastIndex)
    {
        if (items.Count == 0) return;
        firstIndex = Math.Clamp(firstIndex, 0, items.Count - 1);
        lastIndex = Math.Clamp(lastIndex, 0, items.Count - 1);
        _bookIds.Clear();
        for (var i = Math.Min(firstIndex, lastIndex); i <= Math.Max(firstIndex, lastIndex); i++)
            Add(items[i]);
    }

    public void SelectAll(IReadOnlyList<LibraryDisplayItem> items)
    {
        foreach (var item in items) Add(item);
    }

    public void RetainBooks(IEnumerable<BookItem> books) =>
        _bookIds.IntersectWith(books.Select(book => book.Id));

    public IReadOnlyList<BookItem> ResolveBooks(IEnumerable<BookItem> books) =>
        books.Where(book => _bookIds.Contains(book.Id)).DistinctBy(book => book.Id).ToArray();

    public void Clear() => _bookIds.Clear();
}
