using NebulaReader.Models;
using SQLite;

namespace NebulaReader.Services;

/// <summary>
/// Persistencia local de biblioteca y progreso de lectura sobre SQLite.
/// </summary>
public sealed class LibraryDatabase : ILibraryDatabase
{
    private const SQLiteOpenFlags DbFlags =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache;

    private readonly string _databasePath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public LibraryDatabase()
#if WINDOWS
        : this(Path.Combine(AppContext.BaseDirectory, "Data", "nebulareader_library.db3"))
#elif ANDROID
        : this(Path.Combine(FileSystem.AppDataDirectory, "nebulareader_library.db3"))
#else
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaReader", "nebulareader_library.db3"))
#endif
    {
    }

    public LibraryDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    public async Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        var items = await conn.Table<BookItem>().ToListAsync().ConfigureAwait(false);

        return items
            .OrderBy(b => b.Title, NaturalSortComparer.Instance)
            .ThenBy(b => b.FilePath, NaturalSortComparer.Instance)
            .ToList();
    }

    public async Task<BookItem?> GetBookByPathAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ct.ThrowIfCancellationRequested();

        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<BookItem>()
            .Where(b => b.FilePath == filePath)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task SaveBookAsync(BookItem book, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ct.ThrowIfCancellationRequested();

        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.InsertOrReplaceAsync(book).ConfigureAwait(false);
    }

    public async Task UpdateBookMetadataAsync(BookItem book, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        // Una portada puede terminar después de una lectura: no guardar su copia antigua del progreso.
        await conn.ExecuteAsync(
            "UPDATE Books SET CoverThumbnailPath = ?, TotalPages = CASE WHEN ? > 0 THEN ? ELSE TotalPages END, " +
            "SeriesName = CASE WHEN SeriesEditedManually = 1 THEN SeriesName ELSE ? END, " +
            "Tags = CASE WHEN TagsEditedManually = 1 THEN Tags ELSE ? END, MetadataExtracted = ? WHERE Id = ? AND FilePath = ?",
            book.CoverThumbnailPath, book.TotalPages, book.TotalPages,
            book.SeriesName, book.Tags, book.MetadataExtracted, book.Id, book.FilePath).ConfigureAwait(false);
    }

    public async Task ClearCoverCacheReferencesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET CoverThumbnailPath = NULL WHERE CoverThumbnailPath IS NOT NULL")
            .ConfigureAwait(false);
    }

    public async Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ct.ThrowIfCancellationRequested();

        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        var book = await conn.Table<BookItem>()
            .Where(b => b.Id == progress.BookId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (book == null)
        {
            return;
        }

        book.CurrentPage = progress.CurrentPage;
        if (progress.TotalPages > 0)
        {
            book.TotalPages = progress.TotalPages;
        }

        book.LastReadAtUtc = progress.UpdatedAtUtc == default ? DateTime.UtcNow : progress.UpdatedAtUtc;
        book.Status = book.TotalPages > 0 && book.CurrentPage >= book.TotalPages
            ? ReadingStatus.Completed
            : ReadingStatus.Reading;

        // No sobrescribir portadas o metadatos que hayan cambiado durante la lectura.
        await conn.ExecuteAsync(
            "UPDATE Books SET CurrentPage = ?, " +
            "TotalPages = CASE WHEN ? > 0 THEN ? ELSE TotalPages END, LastReadAtUtc = ?, Status = ? WHERE Id = ?",
            book.CurrentPage, progress.TotalPages, progress.TotalPages, book.LastReadAtUtc, book.Status, book.Id)
            .ConfigureAwait(false);
    }

    public void UpdateProgressSynchronously(ReadingProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var conn = _connection?.GetConnection();
        if (conn != null)
        {
            using (conn.Lock())
            {
                UpdateProgressCore(conn, progress);
            }
            return;
        }

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var direct = new SQLiteConnection(_databasePath, DbFlags);
        direct.CreateTable<BookItem>();
        UpdateProgressCore(direct, progress);
    }

    private static void UpdateProgressCore(SQLiteConnection db, ReadingProgress progress)
    {
        var book = db.Find<BookItem>(progress.BookId);
        if (book == null)
        {
            return;
        }

        var currentPage = progress.CurrentPage;
        var totalPages = progress.TotalPages > 0 ? progress.TotalPages : book.TotalPages;
        var lastReadAtUtc = progress.UpdatedAtUtc == default ? DateTime.UtcNow : progress.UpdatedAtUtc;
        var status = totalPages > 0 && currentPage >= totalPages
            ? ReadingStatus.Completed
            : ReadingStatus.Reading;

        db.Execute(
            "UPDATE Books SET CurrentPage = ?, " +
            "TotalPages = CASE WHEN ? > 0 THEN ? ELSE TotalPages END, LastReadAtUtc = ?, Status = ? WHERE Id = ?",
            currentPage, progress.TotalPages, progress.TotalPages, lastReadAtUtc, status, book.Id);
    }

    public async Task<IReadOnlyList<int>> GetBookmarkedPagesAsync(string bookId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        var bookmarks = await conn.Table<BookBookmark>()
            .Where(b => b.BookId == bookId)
            .ToListAsync().ConfigureAwait(false);
        return bookmarks.Select(b => b.PageNumber).Order().ToArray();
    }

    public async Task SetBookmarkAsync(string bookId, int pageNumber, bool bookmarked, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        var id = BookBookmark.CreateId(bookId, pageNumber);
        if (bookmarked)
        {
            var book = await GetBookByIdAsync(bookId, ct).ConfigureAwait(false);
            if (book == null || (book.TotalPages > 0 && pageNumber > book.TotalPages))
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "La página no pertenece al documento.");
            await conn.InsertOrReplaceAsync(new BookBookmark { Id = id, BookId = bookId, PageNumber = pageNumber })
                .ConfigureAwait(false);
        }
        else
        {
            await conn.DeleteAsync<BookBookmark>(id).ConfigureAwait(false);
        }
    }

    public async Task<BookReaderPreferences?> GetReaderPreferencesAsync(string bookId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<BookReaderPreferences>()
            .Where(p => p.BookId == bookId).FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task SaveReaderPreferencesAsync(BookReaderPreferences preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(preferences.BookId);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        var book = await GetBookByIdAsync(preferences.BookId, ct).ConfigureAwait(false);
        if (book == null) throw new InvalidOperationException("El documento ya no está en la biblioteca.");
        await conn.InsertOrReplaceAsync(preferences).ConfigureAwait(false);
    }

    public async Task DeleteReaderPreferencesAsync(string bookId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.DeleteAsync<BookReaderPreferences>(bookId).ConfigureAwait(false);
    }

    public async Task<BackupImportResult> MergeBackupAsync(LibraryBackup backup, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        var added = 0;
        var merged = 0;
        var backupIds = backup.Books.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var uniqueNames = backup.Books.Where(entry => !string.IsNullOrWhiteSpace(entry.FileName))
            .GroupBy(entry => entry.FileName!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        await conn.RunInTransactionAsync(db =>
        {
            var localBooks = db.Table<BookItem>().ToList();
            var claimedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in backup.Books)
            {
                ct.ThrowIfCancellationRequested();
                var book = db.Find<BookItem>(entry.Id);
                if (book == null && entry.FileName is { } name && uniqueNames.Contains(name))
                {
                    var candidates = localBooks.Where(local =>
                        !local.FilePath.StartsWith("backup://", StringComparison.OrdinalIgnoreCase) &&
                        !backupIds.Contains(local.Id) &&
                        !claimedIds.Contains(local.Id) && local.Format == entry.Format &&
                        string.Equals(BackupBook.GetFileName(local.FilePath), name, StringComparison.OrdinalIgnoreCase) &&
                        (local.TotalPages <= 0 || entry.TotalPages <= 0 || local.TotalPages == entry.TotalPages) &&
                        (string.IsNullOrWhiteSpace(local.ContentFingerprint) ||
                         string.IsNullOrWhiteSpace(entry.ContentFingerprint) ||
                         string.Equals(local.ContentFingerprint, entry.ContentFingerprint, StringComparison.Ordinal)))
                        .Take(2).ToList();
                    if (candidates.Count == 1) book = candidates[0];
                }
                if (book == null)
                {
                    book = new BookItem
                    {
                        Id = entry.Id,
                        FilePath = $"backup://{Uri.EscapeDataString(entry.Id)}",
                        OriginalFilePath = entry.FileName,
                        Title = entry.Title,
                        SeriesName = entry.SeriesName,
                        Tags = entry.Tags,
                        Format = entry.Format,
                        CurrentPage = entry.CurrentPage,
                        TotalPages = entry.TotalPages,
                        Status = entry.Status,
                        AddedAtUtc = entry.AddedAtUtc == default ? DateTime.UtcNow : entry.AddedAtUtc,
                        LastReadAtUtc = entry.LastReadAtUtc,
                        TitleEditedManually = entry.TitleEditedManually,
                        SeriesEditedManually = entry.SeriesEditedManually,
                        TagsEditedManually = entry.TagsEditedManually,
                        ContentFingerprint = entry.ContentFingerprint
                    };
                    db.Insert(book);
                    added++;
                }
                else
                {
                    // La ruta local gana. El progreso más reciente gana; en empate, gana el local.
                    if (entry.LastReadAtUtc.HasValue &&
                        (!book.LastReadAtUtc.HasValue || entry.LastReadAtUtc.Value > book.LastReadAtUtc.Value))
                    {
                        if (book.TotalPages <= 0) book.TotalPages = entry.TotalPages;
                        book.CurrentPage = book.TotalPages > 0
                            ? Math.Clamp(entry.CurrentPage, 1, book.TotalPages)
                            : entry.CurrentPage;
                        book.Status = entry.Status;
                        book.LastReadAtUtc = entry.LastReadAtUtc;
                    }
                    if (entry.TitleEditedManually && !book.TitleEditedManually)
                    {
                        book.Title = entry.Title;
                        book.TitleEditedManually = true;
                    }
                    if (entry.SeriesEditedManually && !book.SeriesEditedManually)
                    {
                        book.SeriesName = entry.SeriesName;
                        book.SeriesEditedManually = true;
                    }
                    if (entry.TagsEditedManually && !book.TagsEditedManually)
                    {
                        book.Tags = entry.Tags;
                        book.TagsEditedManually = true;
                    }
                    else if (!book.TagsEditedManually && string.IsNullOrWhiteSpace(book.Tags)) book.Tags = entry.Tags;
                    if (string.IsNullOrWhiteSpace(book.ContentFingerprint)) book.ContentFingerprint = entry.ContentFingerprint;
                    db.Update(book);
                    merged++;
                }

                claimedIds.Add(book.Id);

                foreach (var page in entry.BookmarkedPages)
                    db.Execute("INSERT OR IGNORE INTO BookBookmarks (Id, BookId, PageNumber) VALUES (?, ?, ?)",
                        BookBookmark.CreateId(book.Id, page), book.Id, page);

                if (entry.Preferences != null && db.Find<BookReaderPreferences>(book.Id) == null)
                    db.Insert(new BookReaderPreferences
                    {
                        BookId = book.Id,
                        LayoutMode = entry.Preferences.LayoutMode,
                        PageFitMode = entry.Preferences.PageFitMode,
                        RightToLeftManga = entry.Preferences.RightToLeftManga,
                        VisualFilter = entry.Preferences.VisualFilter
                    });
            }
        }).ConfigureAwait(false);
        return new BackupImportResult(added, merged);
    }

    public async Task DeleteBookAsync(string bookId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ct.ThrowIfCancellationRequested();

        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM BookBookmarks WHERE BookId = ?", bookId).ConfigureAwait(false);
        await conn.DeleteAsync<BookReaderPreferences>(bookId).ConfigureAwait(false);
        await conn.DeleteAsync<BookItem>(bookId).ConfigureAwait(false);
    }

    public async Task<BookItem?> GetBookByIdAsync(string bookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<BookItem>().Where(b => b.Id == bookId).FirstOrDefaultAsync().ConfigureAwait(false);
    }

    public async Task UpdateDetailsAsync(string bookId, string title, string? series, bool titleEdited, bool seriesEdited, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET Title = ?, SeriesName = ?, TitleEditedManually = ?, SeriesEditedManually = ? WHERE Id = ?",
            title, series, titleEdited, seriesEdited, bookId).ConfigureAwait(false);
    }

    public async Task UpdateSeriesAsync(IReadOnlyCollection<string> bookIds, string? series, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        ct.ThrowIfCancellationRequested();
        var ids = bookIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new ArgumentException("Selecciona al menos un documento.", nameof(bookIds));
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.RunInTransactionAsync(db =>
        {
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                if (db.Execute("UPDATE Books SET SeriesName = ?, SeriesEditedManually = 1 WHERE Id = ?", series, id) != 1)
                    throw new InvalidOperationException("Uno de los documentos ya no está en la biblioteca.");
            }
        }).ConfigureAwait(false);
    }

    public async Task UpdateTagsAsync(string bookId, string? tags, bool editedManually, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET Tags = ?, TagsEditedManually = ? WHERE Id = ?",
            tags, editedManually, bookId).ConfigureAwait(false);
    }

    public async Task SetReadingStatusAsync(string bookId, ReadingStatus status, int page, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET Status = ?, CurrentPage = ? WHERE Id = ?", status, page, bookId).ConfigureAwait(false);
    }

    public async Task RelocateBookAsync(string bookId, string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET FilePath = ?, OriginalFilePath = NULL, CoverThumbnailPath = NULL, CoverError = NULL WHERE Id = ?",
            filePath, bookId).ConfigureAwait(false);
    }

    public async Task UpdateCoverResultAsync(string bookId, string? coverPath, int pageCount, string? error, string? expectedPath = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET CoverThumbnailPath = ?, CoverError = ?, TotalPages = CASE WHEN ? > 0 THEN ? ELSE TotalPages END WHERE Id = ? AND (? IS NULL OR FilePath = ?)",
            coverPath, error, pageCount, pageCount, bookId, expectedPath, expectedPath).ConfigureAwait(false);
    }

    public async Task UpdateFingerprintAsync(string bookId, string fingerprint, string expectedPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync("UPDATE Books SET ContentFingerprint = ? WHERE Id = ? AND FilePath = ?", fingerprint, bookId, expectedPath).ConfigureAwait(false);
    }

    private async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection != null)
        {
            return _connection;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection != null)
            {
                return _connection;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connection = new SQLiteAsyncConnection(_databasePath, DbFlags);
            await connection.CreateTableAsync<BookItem>().ConfigureAwait(false);
            await connection.CreateTableAsync<BookBookmark>().ConfigureAwait(false);
            await connection.CreateTableAsync<BookReaderPreferences>().ConfigureAwait(false);
            _connection = connection;
            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }
}

