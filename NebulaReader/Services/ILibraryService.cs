using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Gestiona el catálogo de cómics/documentos y el progreso de lectura persistido.
/// </summary>
public interface ILibraryService
{
    Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default);
    Task<BookItem> ImportFileAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<BookItem>> ImportFolderAsync(string folderPath, CancellationToken ct = default);
    Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default);
}

