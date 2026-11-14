using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryPersistenceTests
{
    [Fact]
    public async Task ProgressSurvivesReloadAndDelayedMetadataAndCompletedBooksLeaveContinueReading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nebula_library_{Guid.NewGuid():N}.db3");
        var database = new LibraryDatabase(path);
        try
        {
            var book = new BookItem { Title = "Comic", FilePath = "comic.cbz", Format = BookFormat.Cbz };
            await database.SaveBookAsync(book);
            var openedAt = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);
            await database.UpdateProgressAsync(new ReadingProgress
            {
                BookId = book.Id, CurrentPage = 17, TotalPages = 100, UpdatedAtUtc = openedAt
            });

            // Simular una portada que termina con el objeto previo a la lectura.
            book.TotalPages = 100;
            book.CoverThumbnailPath = "cover.jpg";
            book.SeriesName = "Galaxia";
            book.Tags = "Espacio";
            book.MetadataExtracted = true;
            await database.UpdateBookMetadataAsync(book);

            var reloaded = Assert.Single(await database.GetBooksAsync());
            Assert.Equal(17, reloaded.CurrentPage);
            Assert.Equal(openedAt, reloaded.LastReadAtUtc);
            Assert.Equal(ReadingStatus.Reading, reloaded.Status);
            Assert.Equal("cover.jpg", reloaded.CoverThumbnailPath);
            Assert.Equal("Galaxia", reloaded.SeriesName);
            Assert.True(reloaded.MetadataExtracted);
            Assert.Same(reloaded, Assert.Single(LibraryBrowseService.GetContinueReading([reloaded])));

            await database.UpdateProgressAsync(new ReadingProgress
            {
                BookId = book.Id, CurrentPage = 100, TotalPages = 100, UpdatedAtUtc = openedAt.AddMinutes(1)
            });
            await database.UpdateBookMetadataAsync(book);

            var completed = Assert.Single(await database.GetBooksAsync());
            Assert.Equal(ReadingStatus.Completed, completed.Status);
            Assert.Equal(100, completed.CurrentPage);
            Assert.Equal("cover.jpg", completed.CoverThumbnailPath);
            Assert.Empty(LibraryBrowseService.GetContinueReading([completed]));

            var unknown = new BookItem { Title = "Sin total", FilePath = "unknown.pdf", Format = BookFormat.Pdf };
            await database.SaveBookAsync(unknown);
            await database.UpdateProgressAsync(new ReadingProgress { BookId = unknown.Id, CurrentPage = 4 });
            var recent = Assert.Single(LibraryBrowseService.GetContinueReading(await database.GetBooksAsync()));
            Assert.Equal(unknown.Id, recent.Id);
            Assert.Equal(0, recent.TotalPages);
            Assert.DoesNotContain("%", LibraryBrowseService.GetProgressText(recent));
        }
        finally
        {
            // Cerrar la conexión compartida antes de eliminar la base temporal en Windows.
            await new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
            File.Delete(path);
        }
    }
}
