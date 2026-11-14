using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class BookmarkPersistenceTests
{
    [Fact]
    public async Task ExistingLibraryMigratesAndBookmarksSurviveProgressAndRelocation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nebula_bookmarks_{Guid.NewGuid():N}.db3");
        var legacy = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        try
        {
            await legacy.CreateTableAsync<BookItem>();
            var book = new BookItem { Title = "Comic", FilePath = "old.cbz", TotalPages = 12, CurrentPage = 3 };
            var otherBook = new BookItem { Title = "Otro", FilePath = "other.pdf", TotalPages = 12 };
            await legacy.InsertAsync(book);
            await legacy.InsertAsync(otherBook);
            await legacy.CloseAsync();

            var database = new LibraryDatabase(path);
            Assert.Empty(await database.GetBookmarkedPagesAsync(book.Id));
            await database.SetBookmarkAsync(book.Id, 3, true);
            await database.SetBookmarkAsync(book.Id, 3, true);
            await database.SetBookmarkAsync(book.Id, 9, true);
            await database.SetBookmarkAsync(otherBook.Id, 3, true);
            await database.UpdateProgressAsync(new ReadingProgress { BookId = book.Id, CurrentPage = 5, TotalPages = 12 });
            await database.RelocateBookAsync(book.Id, "new.cbz");

            var reopened = new LibraryDatabase(path);
            Assert.Equal([3, 9], await reopened.GetBookmarkedPagesAsync(book.Id));
            Assert.Equal([3], await reopened.GetBookmarkedPagesAsync(otherBook.Id));
            var reloadedBook = await reopened.GetBookByIdAsync(book.Id);
            Assert.NotNull(reloadedBook);
            Assert.Equal("new.cbz", reloadedBook.FilePath);
            Assert.Equal(5, reloadedBook.CurrentPage);

            await reopened.SetBookmarkAsync(book.Id, 3, false);
            Assert.Equal([9], await database.GetBookmarkedPagesAsync(book.Id));
            await reopened.DeleteBookAsync(book.Id);
            Assert.Empty(await database.GetBookmarkedPagesAsync(book.Id));
            Assert.Equal([3], await database.GetBookmarkedPagesAsync(otherBook.Id));
        }
        finally
        {
            await legacy.CloseAsync();
            File.Delete(path);
        }
    }
}
