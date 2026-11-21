namespace NebulaReader.Services;

/// <summary>Determina si un documento pertenece a una carpeta registrada.</summary>
public static class LibrarySyncScope
{
    public static IReadOnlyList<NebulaReader.Models.BookItem> GetBooksForRemovedFolder(
        IEnumerable<NebulaReader.Models.BookItem> books,
        string removedFolder,
        IEnumerable<string> remainingFolders) => books
        .Where(book => Contains(removedFolder, book.FilePath))
        .Where(book => !remainingFolders.Any(folder => Contains(folder, book.FilePath)))
        .ToArray();

    public static IReadOnlyList<NebulaReader.Models.BookItem> GetMissingBooks(
        IEnumerable<NebulaReader.Models.BookItem> books,
        IReadOnlySet<string> foundPaths,
        IReadOnlyList<string> scannedFolders,
        IReadOnlyList<string> unavailableFolders) => books
        .Where(book => !foundPaths.Contains(book.FilePath))
        .Where(book => scannedFolders.Any(folder => Contains(folder, book.FilePath)))
        .Where(book => !unavailableFolders.Any(folder => Contains(folder, book.FilePath)))
        .ToArray();

    public static bool Contains(string folderPathOrUri, string filePathOrUri)
    {
        if (folderPathOrUri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            // Los documentos SAF creados con BuildDocumentUriUsingTree conservan el URI del árbol.
            return filePathOrUri.StartsWith(folderPathOrUri.TrimEnd('/') + "/document/",
                StringComparison.Ordinal);
        }

        if (filePathOrUri.StartsWith("content://", StringComparison.OrdinalIgnoreCase) ||
            filePathOrUri.StartsWith("backup://", StringComparison.OrdinalIgnoreCase)) return false;

        var prefix = Path.GetFullPath(folderPathOrUri)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(filePathOrUri).StartsWith(prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
