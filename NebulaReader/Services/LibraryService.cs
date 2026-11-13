using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Implementación base del gestor de biblioteca registrada como Singleton.
/// La persistencia SQLite y generación de portadas se completarán en la Fase 3.
/// </summary>
public class LibraryService : ILibraryService
{
    private readonly IBookSourceFactory _bookSourceFactory;
    private readonly List<BookItem> _books = new();
    private readonly Dictionary<string, ReadingProgress> _progressMap = new();

    public LibraryService(IBookSourceFactory bookSourceFactory)
    {
        _bookSourceFactory = bookSourceFactory;
    }

    public Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<BookItem>>(_books.ToList());
    }

    public async Task<BookItem> ImportFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var existing = _books.FirstOrDefault(b => string.Equals(b.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        BookFormat format;
        try
        {
            format = await _bookSourceFactory.DetectFormatAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or NotSupportedException)
        {
            format = _bookSourceFactory.DetectFormat([], filePath);
        }

        var item = new BookItem
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            Format = format
        };

        _books.Add(item);
        return item;
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
}

