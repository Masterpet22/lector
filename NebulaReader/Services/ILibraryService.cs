using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Gestiona el catálogo de cómics/documentos, indexación de carpetas, portadas y progreso persistido en SQLite.
/// </summary>
public interface ILibraryService
{
    event EventHandler<BookItem>? BookUpdated;

    Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default);
    Task<BookItem> ImportFileAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<BookItem>> ImportFolderAsync(string folderPath, CancellationToken ct = default);
    Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default);
}
