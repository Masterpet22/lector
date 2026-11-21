using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryGroupNavigationTests
{
    private static BookItem[] Series() =>
        [new() { Title = "Manual", SeriesName = "DB", CurrentPage = 12 },
         new() { Title = "Comic", SeriesName = "DB", CurrentPage = 8 }];

    [Fact]
    public void DissolvingAnOpenSeriesReturnsToRootWithBothDocumentsAndTheirProgress()
    {
        var books = Series();
        var key = Assert.Single(LibraryBrowseService.Browse(books).Groups).Key;
        Assert.Equal(key, LibraryBrowseService.Browse(books, key).ActiveGroupKey);
        books[0].SeriesName = "Otra serie";

        var afterEdit = LibraryBrowseService.Browse(books, key);

        Assert.Null(afterEdit.ActiveGroupKey);
        Assert.All(afterEdit.Groups, group => Assert.False(group.IsGroup));
        Assert.Equal(2, afterEdit.Groups.SelectMany(group => group.Items).Count());
        Assert.Equal(12, books[0].CurrentPage);
        Assert.Equal(8, books[1].CurrentPage);
    }

    [Fact]
    public void RenamingEveryDocumentInAnOpenSeriesShowsTheRenamedGroupAtRoot()
    {
        var books = Series();
        foreach (var book in books) book.SeriesName = "Nueva";

        var result = LibraryBrowseService.Browse(books, "DB");

        Assert.Null(result.ActiveGroupKey);
        Assert.Equal("Nueva", Assert.Single(result.Groups).DisplayName);
    }

    [Fact]
    public void FilterThatHidesAnExistingGroupKeepsNavigationAndCanBeCleared()
    {
        var books = Series();
        var filtered = LibraryBrowseService.Browse(books, "DB", searchText: "Sin coincidencias");
        Assert.Equal("DB", filtered.ActiveGroupKey);
        Assert.Empty(filtered.Groups);

        var cleared = LibraryBrowseService.Browse(books, filtered.ActiveGroupKey);
        Assert.Equal("DB", cleared.ActiveGroupKey);
        Assert.Equal(2, Assert.Single(cleared.Groups).Items.Count);
        Assert.Null(LibraryBrowseService.Browse(books).ActiveGroupKey);
    }

    [Fact]
    public void EditingSeriesWhileFiltersHideItStillClearsObsoleteNavigation()
    {
        var books = Series();
        books[0].SeriesName = "Otra";

        var result = LibraryBrowseService.Browse(books, "DB", tag: "No existe");

        Assert.Null(result.ActiveGroupKey);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public void DeletingLastGroupMembersReturnsToRoot()
    {
        var result = LibraryBrowseService.Browse([], "DB");

        Assert.Null(result.ActiveGroupKey);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public void RemovingLastTagFromAnOpenTagGroupReturnsToRemainingGroups()
    {
        var book = new BookItem { Title = "Comic", Tags = "Ciencia" };
        var before = LibraryBrowseService.Browse([book], grouping: LibraryGrouping.Tags);
        book.Tags = null;

        var result = LibraryBrowseService.Browse([book], Assert.Single(before.Groups).Key, grouping: LibraryGrouping.Tags);

        Assert.Null(result.ActiveGroupKey);
        Assert.Equal("untagged:", Assert.Single(result.Groups).Key);
        Assert.Same(book, Assert.Single(result.Groups[0].Items));
    }
}
