using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>Reconcilia archivos de las carpetas guardadas con el catálogo al entrar en la app.</summary>
public sealed class LibrarySyncService
{
    private readonly ILibraryService _library;
    private readonly IFilePickerService _filePicker;
    private readonly IImportedFolderStore _folders;

    public LibrarySyncService(ILibraryService library, IFilePickerService filePicker, IImportedFolderStore folders)
    {
        _library = library;
        _filePicker = filePicker;
        _folders = folders;
    }

    public async Task<int> RemoveFolderAsync(string folderPathOrUri, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPathOrUri);
        var registeredFolders = _folders.GetFolders();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!registeredFolders.Any(folder => string.Equals(folder, folderPathOrUri, comparison))) return 0;

        var remainingFolders = registeredFolders
            .Where(folder => !string.Equals(folder, folderPathOrUri, comparison))
            .ToArray();
        var books = await _library.GetBooksAsync(ct).ConfigureAwait(false);
        var removedBooks = LibrarySyncScope.GetBooksForRemovedFolder(books, folderPathOrUri, remainingFolders);
        foreach (var book in removedBooks)
            await _library.RemoveBookAsync(book.Id, ct).ConfigureAwait(false);

        _folders.Remove(folderPathOrUri);
        return removedBooks.Count;
    }

    public async Task<LibrarySyncResult> RunAsync(IProgress<FolderImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var books = await _library.GetBooksAsync(ct).ConfigureAwait(false);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var knownPaths = new HashSet<string>(books.Select(book => book.FilePath), pathComparer);
        var registeredFolders = _folders.GetFolders();
        var foundPaths = new HashSet<string>(pathComparer);
        var scannedFolders = new List<string>();
        var unavailableFolders = new List<string>();

        foreach (var folder in registeredFolders)
        {
            if (ct.IsCancellationRequested) return new LibrarySyncResult(0, 0, 0, 0, unavailableFolders.Count, true);
            try
            {
                var files = await _filePicker.EnumerateBookFilesAsync(folder, ct).ConfigureAwait(false);
                scannedFolders.Add(folder);
                foundPaths.UnionWith(files);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new LibrarySyncResult(0, 0, 0, 0, unavailableFolders.Count, true);
            }
            catch (Exception)
            {
                unavailableFolders.Add(folder);
            }
        }

        var unchanged = foundPaths.Count(knownPaths.Contains);
        var newPaths = foundPaths.Where(path => !knownPaths.Contains(path)).ToArray();
        var import = await _library.RetryImportFailuresAsync(newPaths, progress, ct).ConfigureAwait(false);
        if (import.Cancelled || ct.IsCancellationRequested)
            return new LibrarySyncResult(import.Added, 0, unchanged, import.Failed, unavailableFolders.Count, true);

        var removed = 0;
        foreach (var book in LibrarySyncScope.GetMissingBooks(books, foundPaths, scannedFolders, unavailableFolders))
        {
            if (ct.IsCancellationRequested) break;
            await _library.RemoveBookAsync(book.Id, ct).ConfigureAwait(false);
            removed++;
        }

        foreach (var book in books)
        {
            if (ct.IsCancellationRequested) break;
            if (book.FilePath.StartsWith("backup://", StringComparison.OrdinalIgnoreCase) ||
                registeredFolders.Any(folder => LibrarySyncScope.Contains(folder, book.FilePath))) continue;

            if (!book.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(book.FilePath)) continue;
                var parent = Path.GetDirectoryName(book.FilePath);
                if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) continue;
            }

            if (await _library.CheckAccessAsync(book, ct).ConfigureAwait(false) != BookAccessState.Missing) continue;
            await _library.RemoveBookAsync(book.Id, ct).ConfigureAwait(false);
            removed++;
        }

        return new LibrarySyncResult(import.Added, removed, unchanged, import.Failed,
            unavailableFolders.Count, ct.IsCancellationRequested);
    }
}
