using System.Text;
using NebulaReader.Models;
using NebulaReader.Services;
using SQLite;
using Xunit;

namespace NebulaReader.Tests;

public class DeliveryFiveTests
{
    [Fact]
    public async Task BackupRoundTripMergesWithoutOverwritingNewerLocalProgressOrLocation()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"nebula_backup_source_{Guid.NewGuid():N}.db3");
        var targetPath = Path.Combine(Path.GetTempPath(), $"nebula_backup_target_{Guid.NewGuid():N}.db3");
        try
        {
            var source = new LibraryDatabase(sourcePath);
            var readAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
            var manga = new BookItem { Id = "manga", Title = "Manga corregido", FilePath = "old/manga.cbz",
                Format = BookFormat.Cbz, TotalPages = 12, CurrentPage = 4, Status = ReadingStatus.Reading,
                LastReadAtUtc = readAt, TitleEditedManually = true, SeriesName = "Serie" };
            var pdf = new BookItem { Id = "pdf", Title = "Manual", FilePath = "old/manual.pdf",
                Format = BookFormat.Pdf, TotalPages = 30, CurrentPage = 5 };
            await source.SaveBookAsync(manga);
            await source.SaveBookAsync(pdf);
            await source.SetBookmarkAsync(manga.Id, 4, true);
            await source.SaveReaderPreferencesAsync(new BookReaderPreferences
            {
                BookId = manga.Id, LayoutMode = ReaderLayoutMode.SinglePage,
                PageFitMode = PageFitMode.FitWidth, RightToLeftManga = true,
                VisualFilter = ReaderVisualFilter.Sepia
            });
            var sourceSettings = new TestReaderSettings
            {
                LayoutMode = ReaderLayoutMode.ContinuousVertical,
                PageFitMode = PageFitMode.FitWidth,
                RightToLeftMangaMode = true,
                VisualFilter = ReaderVisualFilter.Sepia
            };
            var sourceTagSettings = new TestOnlineTagSettings { Language = "fr" };
            using var data = new MemoryStream();
            Assert.Equal(2, await new LibraryBackupService(source, sourceSettings, sourceTagSettings).ExportAsync(data));
            var json = Encoding.UTF8.GetString(data.ToArray());
            Assert.DoesNotContain("old/manga.cbz", json);
            Assert.DoesNotContain("old/manual.pdf", json);
            Assert.DoesNotContain("locationHint", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"onlineTagLanguage\": \"fr\"", json);
            var legacyJson = json.Replace("\"title\": \"Manga corregido\"",
                "\"title\": \"Manga corregido\", \"locationHint\": \"old/manga.cbz\"",
                StringComparison.Ordinal);
            Assert.NotEqual(json, legacyJson);

            var target = new LibraryDatabase(targetPath);
            var targetSettings = new TestReaderSettings();
            var targetTagSettings = new TestOnlineTagSettings();
            using var legacyData = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson));
            Assert.Equal(new BackupImportResult(2, 0), await new LibraryBackupService(target, targetSettings, targetTagSettings).ImportAsync(legacyData));
            Assert.Equal(ReaderLayoutMode.ContinuousVertical, targetSettings.LayoutMode);
            Assert.Equal(PageFitMode.FitWidth, targetSettings.PageFitMode);
            Assert.True(targetSettings.RightToLeftMangaMode);
            Assert.Equal(ReaderVisualFilter.Sepia, targetSettings.VisualFilter);
            Assert.Equal("fr", targetTagSettings.Language);
            var restored = await target.GetBookByIdAsync(manga.Id);
            Assert.NotNull(restored);
            Assert.StartsWith("backup://", restored.FilePath);
            Assert.Equal("manga.cbz", restored.OriginalFilePath);
            Assert.Equal(4, restored.CurrentPage);
            Assert.Equal("Manga corregido", restored.Title);
            Assert.Equal([4], await target.GetBookmarkedPagesAsync(manga.Id));
            Assert.True((await target.GetReaderPreferencesAsync(manga.Id))!.RightToLeftManga);

            await target.RelocateBookAsync(manga.Id, "new/manga.cbz");
            await target.UpdateProgressAsync(new ReadingProgress { BookId = manga.Id, CurrentPage = 8,
                TotalPages = 12, UpdatedAtUtc = readAt.AddHours(1) });
            await target.SetBookmarkAsync(manga.Id, 8, true);
            data.Position = 0;
            Assert.Equal(new BackupImportResult(0, 2), await new LibraryBackupService(target).ImportAsync(data));
            restored = await target.GetBookByIdAsync(manga.Id);
            Assert.Equal("new/manga.cbz", restored!.FilePath);
            Assert.Null(restored.OriginalFilePath);
            Assert.Equal(8, restored.CurrentPage);
            Assert.Equal([4, 8], await target.GetBookmarkedPagesAsync(manga.Id));
            Assert.Equal(2, (await target.GetBooksAsync()).Count);

            var invalid = Encoding.UTF8.GetBytes("{\"schema\":\"nebula-reader-data\",\"version\":1,\"books\":[{\"id\":\"bad\",\"title\":\"\"}]}");
            await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryBackupService(target).ImportAsync(new MemoryStream(invalid)));
            await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryBackupService(target).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes("{}"))));
            Assert.Equal(2, (await target.GetBooksAsync()).Count);
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

    [Fact]
    public async Task FolderScanContinuesAfterFailuresAndCancellationKeepsProcessedItems()
    {
        var known = new HashSet<string>();
        var updates = new List<FolderImportProgress>();
        var files = new[] { "one.cbz", "broken.cbr", "two.pdf", "one.cbz" };
        var result = await FolderImportRunner.RunAsync(files, (file, _) =>
        {
            if (file == "broken.cbr") throw new InvalidDataException("Archivo dañado");
            return Task.FromResult(known.Add(file));
        }, () => 2, new InlineProgress<FolderImportProgress>(updates.Add));
        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Existing);
        Assert.Equal(1, result.Failed);
        Assert.Equal(4, result.Processed);
        Assert.Equal("broken.cbr", Assert.Single(result.Failures).FilePath);
        Assert.Equal(0, updates[0].Processed);
        Assert.Equal(4, updates[^1].Processed);
        Assert.Equal(2, updates[^1].PendingCovers);

        using var cts = new CancellationTokenSource();
        var cancelled = await FolderImportRunner.RunAsync(new[] { "a", "b", "c" }, (file, _) =>
        {
            known.Add(file);
            return Task.FromResult(true);
        }, () => 0, new InlineProgress<FolderImportProgress>(p =>
        {
            if (p.Processed == 1) cts.Cancel();
        }), cts.Token);
        Assert.True(cancelled.Cancelled);
        Assert.Equal(1, cancelled.Processed);
        Assert.Contains("a", known);
        Assert.DoesNotContain("b", known);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TestReaderSettings : IReaderSettingsService
    {
        public event EventHandler? SettingsChanged { add { } remove { } }
        public bool NightReadingModeEnabled { get; set; }
        public ReaderVisualFilter VisualFilter { get; set; }
        public bool RightToLeftMangaMode { get; set; }
        public bool ContinuousVerticalScroll { get; set; }
        public bool DoublePageSpreadEnabled { get; set; }
        public ReaderLayoutMode LayoutMode { get; set; }
        public PageFitMode PageFitMode { get; set; }
    }

    private sealed class TestOnlineTagSettings : IOnlineTagSettings
    {
        public string Language { get; set; } = "es";
        public Task<string?> GetCredentialAsync(OnlineTagSource source, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
