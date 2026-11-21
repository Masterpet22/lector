using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryTagBrowseTests
{
    [Fact]
    public void ExactTagCombinesWithSearchFormatAndStatus()
    {
        var match = new BookItem { Title = "Galaxia 2", SeriesName = "Galaxia", Tags = " Espacio ; Aventura ",
            Format = BookFormat.Cbz, Status = ReadingStatus.Reading, CurrentPage = 12 };
        var partial = new BookItem { Title = "Galaxia 3", Tags = "Aventura espacial", Format = BookFormat.Cbz, Status = ReadingStatus.Reading };
        var pdf = new BookItem { Title = "Galaxia 4", Tags = "Aventura", Format = BookFormat.Pdf, Status = ReadingStatus.Reading };
        var unread = new BookItem { Title = "Galaxia 5", Tags = "Aventura", Format = BookFormat.Cbz };

        var groups = LibraryBrowseService.FilterGroups([match, partial, pdf, unread], "galaxia",
            BookFormat.Cbz, ReadingStatus.Reading, " AVENTURA ");

        Assert.Same(match, Assert.Single(Assert.Single(groups).Items));
        Assert.Equal(12, match.CurrentPage);
    }

    [Fact]
    public void TagGroupsIncludeEveryTagOnceAndKeepOriginalDocument()
    {
        var comic = new BookItem { Title = "Comic 10", Tags = "Aventura, Ciencia; aventura", CurrentPage = 5 };
        var manual = new BookItem { Title = "Comic 2", Tags = "CIENCIA", Format = BookFormat.Pdf };
        var groups = LibraryBrowseService.FilterGroups([comic, manual], grouping: LibraryGrouping.Tags);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.True(group.IsGroup));
        Assert.Same(comic, Assert.Single(groups[0].Items));
        Assert.Equal(new[] { manual, comic }, groups[1].Items);
        Assert.Equal(2, groups.SelectMany(group => group.Items).DistinctBy(book => book.Id).Count());
        Assert.Equal(5, comic.CurrentPage);
    }

    [Fact]
    public void UntaggedGroupHasDifferentKeyFromLiteralTagWithSameName()
    {
        var untagged = new BookItem { Title = "Sin clasificar", Tags = " , ; " };
        var tagged = new BookItem { Title = "Clasificado", Tags = "Sin etiquetas" };
        var groups = LibraryBrowseService.FilterGroups([untagged, tagged], grouping: LibraryGrouping.Tags);

        Assert.Equal(2, groups.Count);
        Assert.Equal(groups[0].DisplayName, groups[1].DisplayName);
        Assert.NotEqual(groups[0].Key, groups[1].Key);
        Assert.Same(untagged, Assert.Single(groups.Single(group => group.Key == "untagged:").Items));
        var filtered = LibraryBrowseService.FilterGroups([untagged, tagged], withoutTags: true, grouping: LibraryGrouping.Tags);
        Assert.Same(untagged, Assert.Single(Assert.Single(filtered).Items));
    }

    [Fact]
    public void DocumentsModeDoesNotGroupSeriesOrSharedTagsAndSortsNaturally()
    {
        var ten = new BookItem { Title = "Saga 10", SeriesName = "Saga", Tags = "Aventura" };
        var two = new BookItem { Title = "Saga 2", SeriesName = "Saga", Tags = "Aventura" };
        var groups = LibraryBrowseService.FilterGroups([ten, two], grouping: LibraryGrouping.Documents);

        Assert.All(groups, group => Assert.False(group.IsGroup));
        Assert.Equal(new[] { two, ten }, groups.SelectMany(group => group.Items));
    }

    [Fact]
    public void TagOptionsDeduplicateAndRetainSelectedTagAfterEdits()
    {
        var selected = new TagFilterOption("Etiqueta: aventura", "aventura");
        var options = LibraryBrowseService.GetTagOptions(
            [new BookItem { Tags = "Aventura; aventura, Ciencia" }], selected);

        Assert.Equal(4, options.Count);
        Assert.Same(selected, options.Single(option => option.Tag == "aventura"));
        var afterEdit = LibraryBrowseService.GetTagOptions([new BookItem { Tags = "Ciencia" }], selected);
        Assert.Contains(selected, afterEdit);
        Assert.Empty(LibraryBrowseService.FilterGroups([new BookItem { Tags = "Ciencia" }], tag: selected.Tag));
        Assert.DoesNotContain(selected, LibraryBrowseService.GetTagOptions([new BookItem { Tags = "Ciencia" }]));
    }

    [Fact]
    public void SelectingOverlappingTagGroupsDoesNotDuplicateBatchDocuments()
    {
        var book = new BookItem { Title = "Comic", Tags = "Aventura, Ciencia" };
        var groups = LibraryBrowseService.FilterGroups([book], grouping: LibraryGrouping.Tags);
        var selection = new LibrarySelectionState();
        selection.SelectAll(groups.Select(group => LibraryDisplayItem.FromSeriesGroup(
            group.DisplayName, group.Items, group.CommonTags, groupKey: group.Key, groupKind: "Etiqueta")).ToArray());

        Assert.Equal(1, selection.Count);
        Assert.Same(book, Assert.Single(selection.ResolveBooks([book])));
    }

    [Fact]
    public void TagGroupCardsKeepFullTagNameAndNavigationKey()
    {
        var card = LibraryDisplayItem.FromSeriesGroup("Tema 12", [new BookItem()],
            groupKey: "tag:Tema 12", groupKind: "Etiqueta");

        Assert.Equal("Tema 12", card.DisplayTitle);
        Assert.False(card.HasTitleNumber);
        Assert.Equal("tag:Tema 12", card.GroupKey);
        Assert.StartsWith("Etiqueta", card.SubtitleText);
    }

    [Fact]
    public void FilteringOneTagGroupKeepsItsIdentityWhenOnlyOneBookMatches()
    {
        var first = new BookItem { Title = "A", Tags = "Ciencia", Status = ReadingStatus.Reading };
        var second = new BookItem { Title = "B", Tags = "ciencia", Status = ReadingStatus.Completed };
        var before = LibraryBrowseService.FilterGroups([first, second], grouping: LibraryGrouping.Tags);
        var after = LibraryBrowseService.FilterGroups([first, second], status: ReadingStatus.Reading, grouping: LibraryGrouping.Tags);

        Assert.Equal(Assert.Single(before).Key, Assert.Single(after).Key);
        Assert.Same(first, Assert.Single(after[0].Items));
        Assert.True(after[0].IsGroup);
    }
}
