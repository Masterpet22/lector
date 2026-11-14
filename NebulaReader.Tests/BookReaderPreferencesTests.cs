using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class BookReaderPreferencesTests
{
    [Fact]
    public async Task PreferencesArePerBookSurviveRelocationAndResetWithoutLosingProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nebula_preferences_{Guid.NewGuid():N}.db3");
        try
        {
            // Simula una biblioteca creada antes de existir la tabla de preferencias.
            using (var legacy = new SQLiteConnection(path))
            {
                legacy.CreateTable<BookItem>();
                legacy.Insert(new BookItem { Id = "manga", Title = "Manga", FilePath = "manga.cbz", TotalPages = 20, CurrentPage = 7 });
                legacy.Insert(new BookItem { Id = "pdf", Title = "PDF", FilePath = "manual.pdf", TotalPages = 40, CurrentPage = 3 });
            }

            var database = new LibraryDatabase(path);
            Assert.Null(await database.GetReaderPreferencesAsync("manga"));
            Assert.Null(await database.GetReaderPreferencesAsync("pdf"));

            await database.SaveReaderPreferencesAsync(new BookReaderPreferences
            {
                BookId = "manga", LayoutMode = ReaderLayoutMode.SinglePage,
                PageFitMode = PageFitMode.FitPage, RightToLeftManga = true,
                VisualFilter = ReaderVisualFilter.Sepia
            });
            await database.SaveReaderPreferencesAsync(new BookReaderPreferences
            {
                BookId = "pdf", LayoutMode = ReaderLayoutMode.ContinuousVertical,
                PageFitMode = PageFitMode.FitWidth, RightToLeftManga = false,
                VisualFilter = ReaderVisualFilter.None
            });
            await database.RelocateBookAsync("manga", "moved.cbz");
            await database.UpdateProgressAsync(new ReadingProgress { BookId = "manga", CurrentPage = 9, TotalPages = 20 });

            var reopened = new LibraryDatabase(path);
            var manga = await reopened.GetReaderPreferencesAsync("manga");
            var pdf = await reopened.GetReaderPreferencesAsync("pdf");
            Assert.NotNull(manga);
            Assert.NotNull(pdf);
            Assert.True(manga.RightToLeftManga);
            Assert.Equal(ReaderLayoutMode.SinglePage, manga.LayoutMode);
            Assert.Equal(ReaderVisualFilter.Sepia, manga.VisualFilter);
            Assert.Equal(PageFitMode.FitWidth, pdf.PageFitMode);
            Assert.False(pdf.RightToLeftManga);
            Assert.Equal("moved.cbz", (await reopened.GetBookByIdAsync("manga"))!.FilePath);
            Assert.Equal(9, (await reopened.GetBookByIdAsync("manga"))!.CurrentPage);

            await reopened.DeleteReaderPreferencesAsync("manga");
            Assert.Null(await database.GetReaderPreferencesAsync("manga"));
            Assert.NotNull(await database.GetReaderPreferencesAsync("pdf"));
            Assert.Equal(9, (await database.GetBookByIdAsync("manga"))!.CurrentPage);

            await reopened.DeleteBookAsync("pdf");
            Assert.Null(await database.GetReaderPreferencesAsync("pdf"));
        }
        finally
        {
            await new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
            File.Delete(path);
        }
    }
}
