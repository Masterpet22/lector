using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Implementación base del gestor de biblioteca registrada como Singleton.
/// La persistencia SQLite y generación de portadas se completarán en la Fase 3.
/// </summary>
public class LibraryService : ILibraryService
{
    private readonly List<BookItem> _books = new();
    private readonly Dictionary<string, ReadingProgress> _progressMap = new();

    public Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<BookItem>>(_books.ToList());
    }

    public Task<BookItem> ImportFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var existing = _books.FirstOrDefault(b => string.Equals(b.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return Task.FromResult(existing);
        }

        var item = new BookItem
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            Format = DetectFormat(filePath)
        };

        _books.Add(item);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<BookItem>> ImportFolderAsync(string folderPath, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<BookItem>>(Array.Empty<BookItem>());
    }

    public Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progressMap[progress.BookId] = progress;

        var book = _books.FirstOrDefault(b => b.Id == progress.BookId);
        if (book != null)
        {
            book.CurrentPage = progress.CurrentPage;
            book.TotalPages = progress.TotalPages;
            book.LastReadAtUtc = DateTime.UtcNow;
            book.Status = progress.TotalPages > 0 && progress.CurrentPage >= progress.TotalPages
                ? ReadingStatus.Completed
                : ReadingStatus.Reading;
        }

        return Task.CompletedTask;
    }

    private static BookFormat DetectFormat(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cbz" or ".zip" => BookFormat.Cbz,
            ".cbr" or ".rar" => BookFormat.Cbr,
            ".pdf" => BookFormat.Pdf,
            _ => BookFormat.Unknown
        };
    }
}

