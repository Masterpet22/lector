using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Gestiona el catálogo de cómics/documentos, indexación de carpetas, portadas y progreso persistido en SQLite.
/// </summary>
public interface ILibraryService
{
    event EventHandler<BookItem>? BookUpdated;
    event EventHandler<int>? PendingCoverCountChanged;
    int PendingCoverCount { get; }

    Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default);
    Task<BookItem> ImportFileAsync(string filePath, CancellationToken ct = default);
    Task<FolderImportResult> ImportFolderAsync(string folderPath, IProgress<FolderImportProgress>? progress = null, CancellationToken ct = default);
    Task<FolderImportResult> RetryImportFailuresAsync(IReadOnlyList<string> files, IProgress<FolderImportProgress>? progress = null, CancellationToken ct = default);
    Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetBookmarkedPagesAsync(string bookId, CancellationToken ct = default);
    Task SetBookmarkAsync(string bookId, int pageNumber, bool bookmarked, CancellationToken ct = default);
    Task<BookReaderPreferences?> GetReaderPreferencesAsync(string bookId, CancellationToken ct = default);
    Task SaveReaderPreferencesAsync(BookReaderPreferences preferences, CancellationToken ct = default);
    Task DeleteReaderPreferencesAsync(string bookId, CancellationToken ct = default);
    Task RemoveBookAsync(string bookId, CancellationToken ct = default);
    Task<BookItem> SetReadingStatusAsync(string bookId, ReadingStatus status, CancellationToken ct = default);
    Task<BookItem> EditDetailsAsync(string bookId, string title, string? series, CancellationToken ct = default);
    Task<BookItem> RelocateBookAsync(string bookId, string newPath, CancellationToken ct = default);
    Task RetryCoverAsync(string bookId, CancellationToken ct = default);
    Task<BookAccessState> CheckAccessAsync(BookItem book, CancellationToken ct = default);
}
