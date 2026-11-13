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
        : this(Path.Combine(FileSystem.AppDataDirectory, "nebulareader_library.db3"))
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

        await conn.UpdateAsync(book).ConfigureAwait(false);
    }

    public async Task DeleteBookAsync(string bookId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ct.ThrowIfCancellationRequested();

        var conn = await GetConnectionAsync(ct).ConfigureAwait(false);
        await conn.DeleteAsync<BookItem>(bookId).ConfigureAwait(false);
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
            _connection = connection;
            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }
}

