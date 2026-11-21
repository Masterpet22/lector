using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class BackupFileNameTests
{
    [Fact]
    public async Task AmbiguousFileNameStaysPending()
    {
        var path = Path.Combine(Path.GetTempPath(), $"backup_names_ambiguous_{Guid.NewGuid():N}.db3");
        try
        {
            var target = new LibraryDatabase(path);
            await target.SaveBookAsync(new BookItem { Id = "first", FilePath = "one/issue.cbz", Title = "One", Format = BookFormat.Cbz });
            await target.SaveBookAsync(new BookItem { Id = "second", FilePath = "two/issue.cbz", Title = "Two", Format = BookFormat.Cbz });
            var json = """
                {"schema":"nebula-reader-data","version":1,"books":[{"id":"exported","title":"Issue","fileName":"issue.cbz","format":"Cbz","currentPage":1,"bookmarkedPages":[]}]}
                """;
            using var backup = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            Assert.Equal(new BackupImportResult(1, 0), await new LibraryBackupService(target).ImportAsync(backup));
            Assert.Equal("issue.cbz", (await target.GetBookByIdAsync("exported"))!.OriginalFilePath);
            Assert.Equal(3, (await target.GetBooksAsync()).Count);
        }
        finally
        {
            await new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportMatchesUniqueFileNameAcrossDifferentBookIds()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"backup_names_source_{Guid.NewGuid():N}.db3");
        var targetPath = Path.Combine(Path.GetTempPath(), $"backup_names_target_{Guid.NewGuid():N}.db3");
        try
        {
            var source = new LibraryDatabase(sourcePath);
            var target = new LibraryDatabase(targetPath);
            var readAt = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            await source.SaveBookAsync(new BookItem
            {
                Id = "exported", Title = "Título editado", FilePath = @"C:\Comics\Capitulo 01.cbz",
                Format = BookFormat.Cbz, TotalPages = 12, CurrentPage = 7,
                LastReadAtUtc = readAt, TitleEditedManually = true
            });
            await source.SetBookmarkAsync("exported", 7, true);
            await source.SaveReaderPreferencesAsync(new BookReaderPreferences
            {
                BookId = "exported", LayoutMode = ReaderLayoutMode.SinglePage
            });
            await target.SaveBookAsync(new BookItem
            {
                Id = "local", Title = "Capitulo 01", FilePath = @"D:\Lecturas\CAPITULO 01.cbz",
                Format = BookFormat.Cbz, TotalPages = 12
            });

            using var backup = new MemoryStream();
            await new LibraryBackupService(source).ExportAsync(backup);
            Assert.Contains("\"fileName\": \"Capitulo 01.cbz\"", System.Text.Encoding.UTF8.GetString(backup.ToArray()));
            backup.Position = 0;
            Assert.Equal(new BackupImportResult(0, 1), await new LibraryBackupService(target).ImportAsync(backup));

            var books = await target.GetBooksAsync();
            Assert.Single(books);
            Assert.Equal("local", books[0].Id);
            Assert.Equal(@"D:\Lecturas\CAPITULO 01.cbz", books[0].FilePath);
            Assert.Equal("Título editado", books[0].Title);
            Assert.Equal(7, books[0].CurrentPage);
            Assert.Equal([7], await target.GetBookmarkedPagesAsync("local"));
            Assert.Equal(ReaderLayoutMode.SinglePage, (await target.GetReaderPreferencesAsync("local"))!.LayoutMode);
        }
        finally
        {
            foreach (var path in new[] { sourcePath, targetPath })
            {
                await new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache).CloseAsync();
                File.Delete(path);
            }
        }
    }
}
