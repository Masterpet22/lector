using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryManagementTests
{
    [Fact]
    public async Task EditingOtherMetadataKeepsAutomaticSeriesAndTagsAvailable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nebula_metadata_{Guid.NewGuid():N}.db3");
        try
        {
            var database = new LibraryDatabase(path);
            var book = new BookItem { Id = "book", Title = "Título", FilePath = "book.pdf",
                SeriesName = "Serie detectada", Tags = "Ciencia" };
            await database.SaveBookAsync(book);
            await database.UpdateDocumentMetadataAsync(book.Id, new DocumentMetadataEdit(
                book.Title, book.SeriesName, "Autora", null, null, null, null, book.Tags));
            var saved = (await database.GetBookByIdAsync(book.Id))!;
            Assert.Equal("Autora", saved.Author);
            Assert.False(saved.TitleEditedManually);
            Assert.False(saved.SeriesEditedManually);
            Assert.False(saved.TagsEditedManually);
        }
        finally
        {
            await new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GroupSeriesEditUpdatesOnlyMembersAndRollsBackIfOneIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nebula_series_group_{Guid.NewGuid():N}.db3");
        try
        {
            var database = new LibraryDatabase(path);
            await database.SaveBookAsync(new BookItem { Id = "a", Title = "Volumen 1", FilePath = "a.cbz", SeriesName = "Anterior" });
            await database.SaveBookAsync(new BookItem { Id = "b", Title = "Volumen 2", FilePath = "b.cbz", SeriesName = "Anterior" });
            await database.SaveBookAsync(new BookItem { Id = "other", Title = "Otro", FilePath = "other.cbz", SeriesName = "Otra" });

            await database.UpdateSeriesAsync(["a", "b"], "Nueva serie");
            var books = await database.GetBooksAsync();
            Assert.All(books.Where(book => book.Id is "a" or "b"), book =>
            {
                Assert.Equal("Nueva serie", book.SeriesName);
                Assert.True(book.SeriesEditedManually);
            });
            Assert.Equal("Otra", books.Single(book => book.Id == "other").SeriesName);

            await Assert.ThrowsAsync<InvalidOperationException>(() => database.UpdateSeriesAsync(["a", "missing"], "No aplicar"));
            Assert.Equal("Nueva serie", (await database.GetBookByIdAsync("a"))!.SeriesName);

            var metadata = (await database.GetBookByIdAsync("a"))!;
            metadata.SeriesName = "Serie detectada";
            await database.UpdateBookMetadataAsync(metadata);
            Assert.Equal("Nueva serie", (await database.GetBookByIdAsync("a"))!.SeriesName);
        }
        finally
        {
            await new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
            File.Delete(path);
        }
    }

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
            await database.UpdateTagsAsync(book.Id, "Aventura, Manga", true);
            var reloaded = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("Editado", reloaded.Title);
            Assert.Equal("Aventura, Manga", reloaded.Tags);
            Assert.True(reloaded.TagsEditedManually);
            Assert.Equal(12, reloaded.CurrentPage);
            await database.UpdateDocumentMetadataAsync(book.Id, new DocumentMetadataEdit(
                "Documento", "Serie nueva", "Autora", new DateTime(2024, 5, 17),
                "Editorial", "Español", "Resumen", "Historia, Ciencia"));
            var withMetadata = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("Documento", withMetadata.Title);
            Assert.Equal("Serie nueva", withMetadata.SeriesName);
            Assert.Equal("Autora", withMetadata.Author);
            Assert.Equal(new DateTime(2024, 5, 17), withMetadata.PublicationDate);
            Assert.Equal("Editorial", withMetadata.Publisher);
            Assert.Equal("Español", withMetadata.Language);
            Assert.Equal("Resumen", withMetadata.Description);
            Assert.Equal("Historia, Ciencia", withMetadata.Tags);
            Assert.Equal(12, withMetadata.CurrentPage);
            withMetadata.Author = "Autor extraído después";
            withMetadata.Publisher = "Otra editorial";
            withMetadata.DetailedMetadataExtracted = true;
            await database.UpdateBookMetadataAsync(withMetadata);
            var afterExtraction = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("Autora", afterExtraction.Author);
            Assert.Equal("Editorial", afterExtraction.Publisher);
            Assert.True(afterExtraction.DetailedMetadataExtracted);
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
            await database.UpdateTagsAsync(book.Id, "Personalizada", true);
            book.SeriesName = "Extracción tardía";
            book.CoverThumbnailPath = "cover.jpg";
            book.Tags = "Extracción tardía";
            await database.UpdateBookMetadataAsync(book);
            var edited = Assert.Single(await database.GetBooksAsync());
            Assert.Equal("Corregido", edited.Title);
            Assert.Equal("Mi serie", edited.SeriesName);
            Assert.Equal("Personalizada", edited.Tags);
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
