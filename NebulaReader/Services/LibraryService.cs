using System.Collections.Concurrent;
using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Implementación Singleton de biblioteca respaldada por SQLite y generación de portadas en segundo plano.
/// </summary>
public class LibraryService : ILibraryService
{
    private readonly ILibraryDatabase _database;
    private readonly IBookSourceFactory _bookSourceFactory;
    private readonly IFilePickerService _filePickerService;
    private readonly ICoverThumbnailService _coverThumbnailService;
    private readonly SemaphoreSlim _coverThrottle = new(2, 2);
    private readonly ConcurrentDictionary<string, byte> _queuedBookIds = new(StringComparer.Ordinal);
    private readonly WeakEventManager _bookUpdatedWeakEvent = new();

    public event EventHandler<BookItem>? BookUpdated
    {
        add => _bookUpdatedWeakEvent.AddEventHandler(value);
        remove => _bookUpdatedWeakEvent.RemoveEventHandler(value);
    }

    public LibraryService(
        ILibraryDatabase database,
        IBookSourceFactory bookSourceFactory,
        IFilePickerService filePickerService,
        ICoverThumbnailService coverThumbnailService)
    {
        _database = database;
        _bookSourceFactory = bookSourceFactory;
        _filePickerService = filePickerService;
        _coverThumbnailService = coverThumbnailService;
    }

    public async Task<IReadOnlyList<BookItem>> GetBooksAsync(CancellationToken ct = default)
    {
        var books = await _database.GetBooksAsync(ct).ConfigureAwait(false);
        foreach (var book in books)
        {
            if (NeedsBackgroundEnrichment(book))
            {
                QueueBackgroundCoverGeneration(book);
            }
        }
        return books;
    }

    public async Task<BookItem> ImportFileAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ct.ThrowIfCancellationRequested();

        var existing = await _database.GetBookByPathAsync(filePath, ct).ConfigureAwait(false);
        if (existing != null)
        {
            if (NeedsBackgroundEnrichment(existing))
            {
                QueueBackgroundCoverGeneration(existing);
            }
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
            Title = ResolveDisplayTitle(filePath),
            FilePath = filePath,
            Format = format
        };

        await _database.SaveBookAsync(item, ct).ConfigureAwait(false);
        QueueBackgroundCoverGeneration(item);
        return item;
    }

    public async Task<IReadOnlyList<BookItem>> ImportFolderAsync(string folderPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ct.ThrowIfCancellationRequested();

        var files = await _filePickerService.EnumerateBookFilesAsync(folderPath, ct).ConfigureAwait(false);
        var imported = new List<BookItem>(files.Count);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var book = await ImportFileAsync(file, ct).ConfigureAwait(false);
            imported.Add(book);
        }

        return imported;
    }

    public Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return _database.UpdateProgressAsync(progress, ct);
    }

    private static bool NeedsBackgroundEnrichment(BookItem book)
    {
        var missingCover = string.IsNullOrWhiteSpace(book.CoverThumbnailPath)
            || !File.Exists(book.CoverThumbnailPath)
            || book.TotalPages <= 0;
        return missingCover || !book.MetadataExtracted;
    }

    private void QueueBackgroundCoverGeneration(BookItem book)
    {
        if (!_queuedBookIds.TryAdd(book.Id, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _coverThrottle.WaitAsync().ConfigureAwait(false);
            try
            {
                var changed = false;

                if (string.IsNullOrWhiteSpace(book.CoverThumbnailPath)
                    || !File.Exists(book.CoverThumbnailPath)
                    || book.TotalPages <= 0)
                {
                    var (thumbnailPath, pageCount) = await _coverThumbnailService
                        .GenerateCoverAsync(book.Id, book.FilePath)
                        .ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(thumbnailPath) &&
                        !string.Equals(book.CoverThumbnailPath, thumbnailPath, StringComparison.Ordinal))
                    {
                        book.CoverThumbnailPath = thumbnailPath;
                        changed = true;
                    }

                    if (pageCount > 0 && book.TotalPages != pageCount)
                    {
                        book.TotalPages = pageCount;
                        changed = true;
                    }
                }

                if (!book.MetadataExtracted)
                {
                    var metadata = await _bookSourceFactory
                        .ExtractMetadataAsync(book.FilePath)
                        .ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(metadata.SeriesName) &&
                        !string.Equals(book.SeriesName, metadata.SeriesName, StringComparison.Ordinal))
                    {
                        book.SeriesName = metadata.SeriesName;
                        changed = true;
                    }

                    var formattedTags = metadata.FormattedTags;
                    if (!string.IsNullOrWhiteSpace(formattedTags) &&
                        !string.Equals(book.Tags, formattedTags, StringComparison.Ordinal))
                    {
                        book.Tags = formattedTags;
                        changed = true;
                    }

                    book.MetadataExtracted = true;
                    changed = true;
                }

                if (changed)
                {
                    await _database.UpdateBookMetadataAsync(book).ConfigureAwait(false);
                    var current = await _database.GetBookByPathAsync(book.FilePath).ConfigureAwait(false);
                    if (current != null)
                    {
                        _bookUpdatedWeakEvent.HandleEvent(this, current, nameof(BookUpdated));
                    }
                }
            }
            catch
            {
                // Si un archivo está dañado o bloqueado, no debe interrumpir la carga del resto de la biblioteca.
            }
            finally
            {
                _queuedBookIds.TryRemove(book.Id, out _);
                _coverThrottle.Release();
            }
        });
    }

    private static string ResolveDisplayTitle(string filePathOrUri)
    {
        var unescaped = Uri.UnescapeDataString(filePathOrUri);
        var normalized = unescaped.Replace('\\', '/');
        var lastSegment = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? unescaped;
        var title = Path.GetFileNameWithoutExtension(lastSegment);
        return string.IsNullOrWhiteSpace(title) ? lastSegment : title;
    }
}
