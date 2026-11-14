using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Contrato de persistencia SQLite para la biblioteca local y el progreso de lectura.
/// </summary>
public interface ILibraryDatabase
{
    Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default);
    Task<BookItem?> GetBookByPathAsync(string filePath, CancellationToken ct = default);
    Task SaveBookAsync(BookItem book, CancellationToken ct = default);
    Task UpdateBookMetadataAsync(BookItem book, CancellationToken ct = default);
    Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetBookmarkedPagesAsync(string bookId, CancellationToken ct = default);
    Task SetBookmarkAsync(string bookId, int pageNumber, bool bookmarked, CancellationToken ct = default);
    Task<BookReaderPreferences?> GetReaderPreferencesAsync(string bookId, CancellationToken ct = default);
    Task SaveReaderPreferencesAsync(BookReaderPreferences preferences, CancellationToken ct = default);
    Task DeleteReaderPreferencesAsync(string bookId, CancellationToken ct = default);
    Task<BackupImportResult> MergeBackupAsync(LibraryBackup backup, CancellationToken ct = default);
    Task DeleteBookAsync(string bookId, CancellationToken ct = default);
    Task<BookItem?> GetBookByIdAsync(string bookId, CancellationToken ct = default);
    Task UpdateDetailsAsync(string bookId, string title, string? series, bool titleEdited, bool seriesEdited, CancellationToken ct = default);
    Task SetReadingStatusAsync(string bookId, ReadingStatus status, int page, CancellationToken ct = default);
    Task RelocateBookAsync(string bookId, string filePath, CancellationToken ct = default);
    Task UpdateCoverResultAsync(string bookId, string? coverPath, int pageCount, string? error, string? expectedPath = null, CancellationToken ct = default);
    Task UpdateFingerprintAsync(string bookId, string fingerprint, string expectedPath, CancellationToken ct = default);
}

