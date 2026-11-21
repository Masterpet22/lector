using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class LibrarySelectionStateTests
{
    [Fact]
    public void ResolveBooksKeepsEachSelectedDocumentAndItsOwnTags()
    {
        var first = new BookItem { Title = "Comic", Format = BookFormat.Cbz, Tags = "Aventura" };
        var second = new BookItem { Title = "Manual", Format = BookFormat.Pdf, Tags = "Ciencia" };
        var unselected = new BookItem { Title = "Otro" };
        var selection = new LibrarySelectionState();
        selection.Add(LibraryDisplayItem.FromSeriesGroup("Grupo", [first, second]));
        selection.Add(LibraryDisplayItem.FromSingleBook(first));

        var books = selection.ResolveBooks([first, second, first, unselected]);

        Assert.Equal(2, books.Count);
        Assert.Same(first, books[0]);
        Assert.Same(second, books[1]);
        Assert.Equal("Aventura", books[0].Tags);
        Assert.Equal("Ciencia", books[1].Tags);
    }

    [Fact]
    public void ResolvedBatchSurvivesSelectionChangesAndExcludesRemovedBooks()
    {
        var first = new BookItem { Title = "A" };
        var second = new BookItem { Title = "B" };
        var removed = new BookItem { Title = "Eliminado" };
        var selection = new LibrarySelectionState();
        selection.Add(LibraryDisplayItem.FromSeriesGroup("Serie", [first, second, removed]));

        var batch = selection.ResolveBooks([first, second]);
        selection.Clear();

        Assert.Equal(new[] { first.Id, second.Id }, batch.Select(book => book.Id));
        Assert.Empty(selection.SelectedIds);
    }

    [Fact]
    public void GroupAndRangeSelectionOperateOnDocuments()
    {
        var first = new BookItem { Title = "A" };
        var second = new BookItem { Title = "B" };
        var third = new BookItem { Title = "C" };
        var group = LibraryDisplayItem.FromSeriesGroup("Serie", [first, second]);
        var single = LibraryDisplayItem.FromSingleBook(third);
        var selection = new LibrarySelectionState();

        selection.Toggle(group);
        Assert.Equal(2, selection.Count);
        Assert.True(selection.ContainsAll(group));

        selection.Add(single);
        Assert.Equal(3, selection.Count);
        selection.Toggle(group);
        Assert.Equal(new[] { third.Id }, selection.SelectedIds);

        selection.SelectRange([group, single], 0, 1);
        Assert.Equal(3, selection.Count);
        selection.RetainBooks([first, third]);
        Assert.Equal(2, selection.Count);
        Assert.False(selection.ContainsAll(group));
        Assert.True(selection.ContainsAny(group));

        selection.Clear();
        Assert.Equal(0, selection.Count);
    }
}
