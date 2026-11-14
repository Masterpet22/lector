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
    Task DeleteBookAsync(string bookId, CancellationToken ct = default);
}

