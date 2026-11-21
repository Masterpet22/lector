using System.Collections.Concurrent;
using NebulaReader.Models;
using System.Diagnostics;
using System.Security.Cryptography;

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
    private readonly WeakEventManager _coverCountWeakEvent = new();

    public int PendingCoverCount => _queuedBookIds.Count;

    public event EventHandler<int>? PendingCoverCountChanged
    {
        add => _coverCountWeakEvent.AddEventHandler(value);
        remove => _coverCountWeakEvent.RemoveEventHandler(value);
    }

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
            if (NeedsBackgroundEnrichment(book) && book.CoverError == null)
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

    public async Task<FolderImportResult> ImportFolderAsync(
        string folderPath, IProgress<FolderImportProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        try
        {
            var files = await _filePickerService.EnumerateBookFilesAsync(folderPath, ct).ConfigureAwait(false);
            return await RetryImportFailuresAsync(files, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new FolderImportResult(0, 0, 0, 0, 0, true, []);
        }
    }

    public async Task<FolderImportResult> RetryImportFailuresAsync(
        IReadOnlyList<string> files, IProgress<FolderImportProgress>? progress = null, CancellationToken ct = default)
    {
        return await FolderImportRunner.RunAsync(files, async (file, token) =>
        {
            if (await _database.GetBookByPathAsync(file, token).ConfigureAwait(false) != null) return false;
            // Un archivo roto no debe convertirse en una entrada de la biblioteca.
            using var source = await _bookSourceFactory.CreateAsync(file, token).ConfigureAwait(false);
            if (source.PageCount <= 0) throw new InvalidDataException("El documento no contiene páginas legibles.");
            await ImportFileAsync(file, token).ConfigureAwait(false);
            return true;
        }, () => PendingCoverCount, progress, ct).ConfigureAwait(false);
    }

    public Task UpdateProgressAsync(ReadingProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return _database.UpdateProgressAsync(progress, ct);
    }

    public void UpdateProgressSynchronously(ReadingProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _database.UpdateProgressSynchronously(progress);
    }

    public async Task ClearCoverCacheAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Run(_coverThumbnailService.ClearCache, ct).ConfigureAwait(false);
        await _database.ClearCoverCacheReferencesAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<int>> GetBookmarkedPagesAsync(string bookId, CancellationToken ct = default) =>
        _database.GetBookmarkedPagesAsync(bookId, ct);

    public Task SetBookmarkAsync(string bookId, int pageNumber, bool bookmarked, CancellationToken ct = default) =>
        _database.SetBookmarkAsync(bookId, pageNumber, bookmarked, ct);

    public Task<BookReaderPreferences?> GetReaderPreferencesAsync(string bookId, CancellationToken ct = default) =>
        _database.GetReaderPreferencesAsync(bookId, ct);

    public Task SaveReaderPreferencesAsync(BookReaderPreferences preferences, CancellationToken ct = default) =>
        _database.SaveReaderPreferencesAsync(preferences, ct);

    public Task DeleteReaderPreferencesAsync(string bookId, CancellationToken ct = default) =>
        _database.DeleteReaderPreferencesAsync(bookId, ct);

    public Task RemoveBookAsync(string bookId, CancellationToken ct = default) => _database.DeleteBookAsync(bookId, ct);

    public async Task<BookItem> SetReadingStatusAsync(string bookId, ReadingStatus status, CancellationToken ct = default)
    {
        var book = await RequireBookAsync(bookId, ct).ConfigureAwait(false);
        var page = status switch
        {
            ReadingStatus.Unread => 1,
            ReadingStatus.Completed when book.TotalPages > 0 => book.TotalPages,
            ReadingStatus.Completed => throw new InvalidOperationException("No se puede marcar como leído sin conocer el total de páginas."),
            _ => book.CurrentPage
        };
        await _database.SetReadingStatusAsync(bookId, status, page, ct).ConfigureAwait(false);
        return (await RequireBookAsync(bookId, ct).ConfigureAwait(false));
    }

    public async Task<BookItem> EditDetailsAsync(string bookId, string title, string? series, CancellationToken ct = default)
    {
        title = title.Trim();
        if (title.Length == 0) throw new ArgumentException("El título no puede estar vacío.", nameof(title));
        var book = await RequireBookAsync(bookId, ct).ConfigureAwait(false);
        await _database.UpdateDetailsAsync(bookId, title, string.IsNullOrWhiteSpace(series) ? null : series.Trim(),
            true, true, ct).ConfigureAwait(false);
        return await RequireBookAsync(bookId, ct).ConfigureAwait(false);
    }

    public Task UpdateSeriesAsync(IReadOnlyCollection<string> bookIds, string? series, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bookIds);
        var normalized = string.IsNullOrWhiteSpace(series) ? null : series.Trim();
        if (normalized?.Length > 255)
            throw new ArgumentException("La serie no puede superar los 255 caracteres.", nameof(series));
        return _database.UpdateSeriesAsync(bookIds, normalized, ct);
    }

    public async Task<BookItem> EditTagsAsync(string bookId, string? tags, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(tags) ? null : string.Join(", ",
            tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32));
        if (normalized?.Length > 1000)
            throw new ArgumentException("Las etiquetas no pueden superar los 1000 caracteres.", nameof(tags));
        await RequireBookAsync(bookId, ct).ConfigureAwait(false);
        await _database.UpdateTagsAsync(bookId, normalized, editedManually: true, ct).ConfigureAwait(false);
        return await RequireBookAsync(bookId, ct).ConfigureAwait(false);
    }

    public async Task<BookItem> RelocateBookAsync(string bookId, string newPath, CancellationToken ct = default)
    {
        var book = await RequireBookAsync(bookId, ct).ConfigureAwait(false);
        if (await _database.GetBookByPathAsync(newPath, ct).ConfigureAwait(false) is { } duplicate && duplicate.Id != bookId)
            throw new InvalidOperationException("El archivo elegido ya figura en la biblioteca.");
        using var source = await _bookSourceFactory.CreateAsync(newPath, ct).ConfigureAwait(false);
        var format = await _bookSourceFactory.DetectFormatAsync(newPath, ct).ConfigureAwait(false);
        if (format != book.Format || source.PageCount <= 0 ||
            (book.TotalPages > 0 && book.TotalPages != source.PageCount))
            throw new InvalidOperationException("El documento elegido no coincide en formato y número de páginas con el original.");
        if (!string.IsNullOrWhiteSpace(book.ContentFingerprint) &&
            !string.Equals(book.ContentFingerprint, await FingerprintAsync(source, ct).ConfigureAwait(false), StringComparison.Ordinal))
            throw new InvalidOperationException("La primera página del archivo elegido no coincide con el documento original.");
        await _database.RelocateBookAsync(bookId, newPath, ct).ConfigureAwait(false);
        var updated = await RequireBookAsync(bookId, ct).ConfigureAwait(false);
        if (!_queuedBookIds.ContainsKey(bookId))
        {
            _coverThumbnailService.Invalidate(bookId);
            QueueBackgroundCoverGeneration(updated);
        }
        return updated;
    }

    public async Task RetryCoverAsync(string bookId, CancellationToken ct = default)
    {
        var book = await RequireBookAsync(bookId, ct).ConfigureAwait(false);
        await _database.UpdateCoverResultAsync(bookId, book.CoverThumbnailPath, book.TotalPages, null, ct: ct).ConfigureAwait(false);
        book.CoverError = null;
        QueueBackgroundCoverGeneration(book);
    }

    public async Task<BookAccessState> CheckAccessAsync(BookItem book, CancellationToken ct = default)
    {
        if (book.FilePath.StartsWith("backup://", StringComparison.OrdinalIgnoreCase))
            return BookAccessState.Missing;
        try
        {
            using var source = await _bookSourceFactory.CreateAsync(book.FilePath, ct).ConfigureAwait(false);
            return source.PageCount > 0 ? BookAccessState.Available : BookAccessState.Damaged;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return BookAccessState.Missing; }
        catch (UnauthorizedAccessException) { return BookAccessState.PermissionDenied; }
#if ANDROID
        catch (Java.Lang.SecurityException) { return BookAccessState.PermissionDenied; }
        catch (Java.IO.FileNotFoundException) { return BookAccessState.Missing; }
#endif
        catch (Exception ex) when (ex is not OperationCanceledException) { return BookAccessState.Damaged; }
    }

    private async Task<BookItem> RequireBookAsync(string bookId, CancellationToken ct) =>
        await _database.GetBookByIdAsync(bookId, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException("El documento ya no está en la biblioteca.");

    private static async Task<string> FingerprintAsync(IBookSource source, CancellationToken ct)
    {
        await using var page = await source.GetPageStreamAsync(0, ct).ConfigureAwait(false);
        var hash = await SHA256.HashDataAsync(page, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private bool NeedsBackgroundEnrichment(BookItem book)
    {
        if (book.FilePath.StartsWith("backup://", StringComparison.OrdinalIgnoreCase)) return false;
        var missingCover = _coverThumbnailService.NeedsRefresh(book.Id, book.CoverThumbnailPath)
            || book.TotalPages <= 0;
        return missingCover || !book.MetadataExtracted || string.IsNullOrWhiteSpace(book.ContentFingerprint);
    }

    private void QueueBackgroundCoverGeneration(BookItem book)
    {
        if (AppLifetime.IsShuttingDown || !_queuedBookIds.TryAdd(book.Id, 0))
        {
            return;
        }

        _coverCountWeakEvent.HandleEvent(this, PendingCoverCount, nameof(PendingCoverCountChanged));

        _ = Task.Run(async () =>
        {
            await _coverThrottle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (AppLifetime.IsShuttingDown) return;
                var changed = false;

                if (_coverThumbnailService.NeedsRefresh(book.Id, book.CoverThumbnailPath)
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

                if (AppLifetime.IsShuttingDown) return;

                if (!book.MetadataExtracted)
                {
                    var metadata = await _bookSourceFactory
                        .ExtractMetadataAsync(book.FilePath)
                        .ConfigureAwait(false);

                    if (!book.SeriesEditedManually && !string.IsNullOrWhiteSpace(metadata.SeriesName) &&
                        !string.Equals(book.SeriesName, metadata.SeriesName, StringComparison.Ordinal))
                    {
                        book.SeriesName = metadata.SeriesName;
                        changed = true;
                    }

                    var formattedTags = metadata.FormattedTags;
                    if (!book.TagsEditedManually && !string.IsNullOrWhiteSpace(formattedTags) &&
                        !string.Equals(book.Tags, formattedTags, StringComparison.Ordinal))
                    {
                        book.Tags = formattedTags;
                        changed = true;
                    }

                    book.MetadataExtracted = true;
                    changed = true;
                }

                if (AppLifetime.IsShuttingDown) return;

                if (book.TotalPages > 0 && string.IsNullOrWhiteSpace(book.ContentFingerprint))
                {
                    using var source = await _bookSourceFactory.CreateAsync(book.FilePath).ConfigureAwait(false);
                    var fingerprint = await FingerprintAsync(source, CancellationToken.None).ConfigureAwait(false);
                    if (AppLifetime.IsShuttingDown) return;
                    await _database.UpdateFingerprintAsync(book.Id, fingerprint, book.FilePath).ConfigureAwait(false);
                    book.ContentFingerprint = fingerprint;
                }

                if (AppLifetime.IsShuttingDown) return;

                if (changed)
                {
                    await _database.UpdateBookMetadataAsync(book).ConfigureAwait(false);
                    await _database.UpdateCoverResultAsync(book.Id, book.CoverThumbnailPath, book.TotalPages, null, book.FilePath).ConfigureAwait(false);
                    var current = await _database.GetBookByPathAsync(book.FilePath).ConfigureAwait(false);
                    if (current != null && !AppLifetime.IsShuttingDown)
                    {
                        _bookUpdatedWeakEvent.HandleEvent(this, current, nameof(BookUpdated));
                    }
                }
                if (string.IsNullOrWhiteSpace(book.CoverThumbnailPath))
                    throw new InvalidDataException("No se encontró una página válida para crear la portada.");
            }
            catch (Exception ex)
            {
                if (AppLifetime.IsShuttingDown) return;
                Trace.TraceError("Error al generar portada del libro {0} ({1}): {2}", book.Id, book.FilePath, ex);
                try
                {
                    await _database.UpdateCoverResultAsync(book.Id, book.CoverThumbnailPath, book.TotalPages, ex.Message, book.FilePath).ConfigureAwait(false);
                    var current = await _database.GetBookByIdAsync(book.Id).ConfigureAwait(false);
                    if (current != null && !AppLifetime.IsShuttingDown)
                        _bookUpdatedWeakEvent.HandleEvent(this, current, nameof(BookUpdated));
                }
                catch (Exception persistError) { Trace.TraceError("Error al registrar fallo de portada {0}: {1}", book.Id, persistError); }
            }
            finally
            {
                _queuedBookIds.TryRemove(book.Id, out _);
                if (!AppLifetime.IsShuttingDown)
                    _coverCountWeakEvent.HandleEvent(this, PendingCoverCount, nameof(PendingCoverCountChanged));
                _coverThrottle.Release();
                if (!AppLifetime.IsShuttingDown)
                {
                    try
                    {
                        var current = await _database.GetBookByIdAsync(book.Id).ConfigureAwait(false);
                        if (current != null && !string.Equals(current.FilePath, book.FilePath, StringComparison.Ordinal))
                        {
                            _coverThumbnailService.Invalidate(book.Id);
                            QueueBackgroundCoverGeneration(current);
                        }
                    }
                    catch (Exception ex) { Trace.TraceError("Error al refrescar portada reubicada {0}: {1}", book.Id, ex); }
                }
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
