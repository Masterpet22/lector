using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryManagementTests
{
    [Fact]
    public async Task ExistingDatabaseGainsManagementColumnsWithoutLosingProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nebula_legacy_{Guid.NewGuid():N}.db3");
        try
        {
            using (var legacy = new SQLiteConnection(path))
            {
                legacy.Execute("CREATE TABLE Books (Id TEXT PRIMARY KEY NOT NULL, Title TEXT, FilePath TEXT, Format INTEGER, TotalPages INTEGER, CurrentPage INTEGER, CoverThumbnailPath TEXT, SeriesName TEXT, Tags TEXT, MetadataExtracted INTEGER, AddedAtUtc TEXT, LastReadAtUtc TEXT, Status INTEGER)");
                legacy.Execute("INSERT INTO Books (Id, Title, FilePath, Format, TotalPages, CurrentPage, Status) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    "legacy", "Libro", "legacy.cbz", BookFormat.Cbz, 40, 12, ReadingStatus.Reading);
            }
            var database = new LibraryDatabase(path);
            var book = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("legacy", book.Id);
            Assert.Equal(12, book.CurrentPage);
            await database.UpdateDetailsAsync(book.Id, "Editado", "Serie", true, true);
            var reloaded = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("Editado", reloaded.Title);
            Assert.Equal(12, reloaded.CurrentPage);
            await new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EditsStatusAndRelocationPersistWithoutChangingIdentityOrOriginalFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nebula_manage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var oldPath = Path.Combine(directory, "old.cbz");
        var newPath = Path.Combine(directory, "new.cbz");
        await File.WriteAllTextAsync(oldPath, "original");
        await File.WriteAllTextAsync(newPath, "moved");
        var dbPath = Path.Combine(directory, "library.db3");
        var database = new LibraryDatabase(dbPath);
        try
        {
            var book = new BookItem { Title = "Original", SeriesName = "Extraída", FilePath = oldPath,
                Format = BookFormat.Cbz, TotalPages = 20, CurrentPage = 7, Status = ReadingStatus.Reading };
            await database.SaveBookAsync(book);
            await database.UpdateDetailsAsync(book.Id, "Corregido", "Mi serie", true, true);
            book.SeriesName = "Extracción tardía";
            book.CoverThumbnailPath = "cover.jpg";
            await database.UpdateBookMetadataAsync(book);
            var edited = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("Corregido", edited.Title);
            Assert.Equal("Mi serie", edited.SeriesName);
            Assert.Equal(7, edited.CurrentPage);

            await database.SetReadingStatusAsync(book.Id, ReadingStatus.Completed, 20);
            await database.RelocateBookAsync(book.Id, newPath);
            var relocated = Assert.Single(await database.GetBooksAsync());
            Assert.Equal(book.Id, relocated.Id);
            Assert.Equal(newPath, relocated.FilePath);
            Assert.Equal(20, relocated.CurrentPage);
            Assert.Equal(ReadingStatus.Completed, relocated.Status);
            Assert.Null(relocated.CoverThumbnailPath);
            Assert.True(File.Exists(oldPath));

            await database.SetReadingStatusAsync(book.Id, ReadingStatus.Unread, 1);
            var unread = Assert.Single(await database.GetBooksAsync());
            Assert.Equal(1, unread.CurrentPage);
            Assert.Equal(ReadingStatus.Unread, unread.Status);
            await database.DeleteBookAsync(book.Id);
            Assert.Empty(await database.GetBooksAsync());
            Assert.True(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
        }
        finally
        {
            await new SQLiteAsyncConnection(dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
