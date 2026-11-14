using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryBrowseTests
{
    [Theory]
    [InlineData("  GALAXIA  ")]
    [InlineData("AVENTURA")]
    [InlineData("volumen especial")]
    public void SearchMatchesSeriesTagsAndTitleIgnoringCaseAndOuterSpaces(string query)
    {
        var match = new BookItem { Title = "Volumen especial", SeriesName = "Galaxia", Tags = "Aventura; Espacio" };
        var other = new BookItem { Title = "Manual", Format = BookFormat.Pdf };

        var result = LibraryBrowseService.FilterGroups([match, other], query);

        Assert.Same(match, Assert.Single(Assert.Single(result).Items));
    }

    [Fact]
    public void CombinedFiltersPreserveSeriesIdentityAndOnlyMatchingBooksAndCovers()
    {
        var matching = new BookItem
        {
            Title = "Galaxia 2", SeriesName = "Galaxia", Tags = "Espacio", Format = BookFormat.Cbz,
            Status = ReadingStatus.Reading, CurrentPage = 8, TotalPages = 50, CoverThumbnailPath = "match.jpg"
        };
        var wrongFormat = new BookItem { Title = "Galaxia 1", SeriesName = "Galaxia", Tags = "Privada", Format = BookFormat.Pdf, Status = ReadingStatus.Reading };
        var wrongStatus = new BookItem { Title = "Galaxia 3", SeriesName = "Galaxia", Format = BookFormat.Cbz, Status = ReadingStatus.Completed };

        var group = Assert.Single(LibraryBrowseService.FilterGroups(
            [wrongFormat, matching, wrongStatus], "galaxia", BookFormat.Cbz, ReadingStatus.Reading));

        Assert.True(group.IsGroup);
        Assert.Equal("Galaxia", group.DisplayName);
        Assert.Same(matching, Assert.Single(group.Items));
        Assert.DoesNotContain("Privada", group.CommonTags);
        var card = LibraryDisplayItem.FromSeriesGroup(group.DisplayName, group.Items, group.CommonTags);
        Assert.Equal(1, card.BookCount);
        Assert.Equal("match.jpg", card.Cover1);
        Assert.Null(card.Cover2);
        Assert.Equal(8, matching.CurrentPage);
    }

    [Theory]
    [InlineData(BookFormat.Cbz)]
    [InlineData(BookFormat.Cbr)]
    [InlineData(BookFormat.Pdf)]
    public void EveryFormatCanBeFiltered(BookFormat format)
    {
        var books = new[] { BookFormat.Cbz, BookFormat.Cbr, BookFormat.Pdf }
            .Select((value, i) => new BookItem { Title = $"Documento {i}", Format = value });

        var result = LibraryBrowseService.FilterGroups(books, format: format);

        Assert.Equal(format, Assert.Single(Assert.Single(result).Items).Format);
    }

    [Theory]
    [InlineData(ReadingStatus.Unread)]
    [InlineData(ReadingStatus.Reading)]
    [InlineData(ReadingStatus.Completed)]
    public void EveryReadingStatusCanBeFiltered(ReadingStatus status)
    {
        var books = new[] { ReadingStatus.Unread, ReadingStatus.Reading, ReadingStatus.Completed }
            .Select((value, i) => new BookItem { Title = $"Documento {i}", Status = value });

        var result = LibraryBrowseService.FilterGroups(books, status: status);

        Assert.Equal(status, Assert.Single(Assert.Single(result).Items).Status);
    }

    [Fact]
    public void ClearingCriteriaRestoresAllBooksWithoutChangingSourceOrProgress()
    {
        var book = new BookItem { Title = "Comic", Format = BookFormat.Cbr, CurrentPage = 17 };
        var books = new[] { book, new BookItem { Title = "Manual", Format = BookFormat.Pdf } };

        Assert.Empty(LibraryBrowseService.FilterGroups(books, "inexistente", BookFormat.Cbz));
        var restored = LibraryBrowseService.FilterGroups(books, " ").SelectMany(g => g.Items).ToList();

        Assert.Equal(2, restored.Count);
        Assert.Contains(book, restored);
        Assert.Equal(17, book.CurrentPage);
    }

    [Fact]
    public void ContinueReadingOrdersRecentFirstAndExcludesUnreadAndCompleted()
    {
        var date = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var older = new BookItem { Title = "Anterior", Status = ReadingStatus.Reading, LastReadAtUtc = date };
        var recent = new BookItem { Title = "Reciente", Status = ReadingStatus.Reading, LastReadAtUtc = date.AddHours(1) };
        var completed = new BookItem { Status = ReadingStatus.Completed, LastReadAtUtc = date.AddHours(2) };
        var unread = new BookItem { Status = ReadingStatus.Unread };
        var atEnd = new BookItem { Status = ReadingStatus.Reading, CurrentPage = 20, TotalPages = 20 };

        Assert.Equal([recent, older], LibraryBrowseService.GetContinueReading([older, completed, unread, atEnd, recent]));

        older.LastReadAtUtc = date.AddHours(3);
        recent.Status = ReadingStatus.Completed;
        Assert.Equal([older], LibraryBrowseService.GetContinueReading([older, recent, completed]));
    }

    [Fact]
    public void ContinueReadingKeepsUnknownTotalsAndPutsMissingDatesLast()
    {
        var unknownDate = new BookItem { Status = ReadingStatus.Reading, TotalPages = 0, CurrentPage = 9 };
        var dated = new BookItem { Status = ReadingStatus.Reading, LastReadAtUtc = DateTime.UtcNow };

        Assert.Equal([dated, unknownDate], LibraryBrowseService.GetContinueReading([unknownDate, dated]));
        var card = LibraryDisplayItem.FromSingleBook(unknownDate);
        Assert.Same(unknownDate, card.Book);
        Assert.False(card.HasKnownPageCount);
        Assert.DoesNotContain("%", card.SubtitleText);
        Assert.Contains("Total desconocido", card.SubtitleText);
        Assert.Contains("9", card.SubtitleText);
    }

    [Fact]
    public void ReadingCardIncludesSavedPageTotalPercentageAndCover()
    {
        var book = new BookItem { CurrentPage = 25, TotalPages = 100, CoverThumbnailPath = "cover.jpg" };

        var card = LibraryDisplayItem.FromSingleBook(book);

        Assert.Same(book, card.Book);
        Assert.True(card.HasKnownPageCount);
        Assert.Equal(0.25, card.Progress);
        Assert.Contains("25/100", card.SubtitleText);
        Assert.Contains("25", card.SubtitleText.Split('·')[1]);
        Assert.Equal("cover.jpg", card.Cover1);
    }

    [Theory]
    [InlineData(-5, 100, 0)]
    [InlineData(200, 100, 1)]
    [InlineData(5, 0, 0)]
    public void ProgressIsBoundedAndUnknownTotalsDoNotInventPercentages(int page, int total, double expected)
    {
        Assert.Equal(expected, LibraryBrowseService.GetProgress(new BookItem { CurrentPage = page, TotalPages = total }));
    }
}
