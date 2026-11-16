using NebulaReader.Services;
using NebulaReader.Models;
using Xunit;

namespace NebulaReader.Tests;

public class LibrarySyncScopeTests
{
    [Fact]
    public void WindowsPathRequiresFolderBoundary()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Comics");
        Assert.True(LibrarySyncScope.Contains(folder, Path.Combine(folder, "Series", "one.cbz")));
        Assert.False(LibrarySyncScope.Contains(folder, Path.Combine(Path.GetTempPath(), "ComicsExtra", "two.cbz")));
    }

    [Fact]
    public void AndroidTreeMatchesOnlyDocumentsFromThatTree()
    {
        const string tree = "content://com.android.externalstorage.documents/tree/primary%3AComics";
        Assert.True(LibrarySyncScope.Contains(tree, tree + "/document/primary%3AComics%2Fone.cbz"));
        Assert.False(LibrarySyncScope.Contains(tree, "content://com.android.externalstorage.documents/tree/primary%3AOther/document/primary%3AOther%2Fone.cbz"));
    }

    [Fact]
    public void BackupPlaceholderNeverBelongsToARealFolder()
    {
        Assert.False(LibrarySyncScope.Contains(Path.GetTempPath(), "backup://unlinked"));
    }

    [Fact]
    public void MissingBooksAreRemovedOnlyFromSuccessfullyScannedFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "Comics");
        var unavailable = Path.Combine(root, "Disconnected");
        var existing = new BookItem { FilePath = Path.Combine(root, "exists.cbz") };
        var missing = new BookItem { FilePath = Path.Combine(root, "missing.cbz") };
        var protectedBook = new BookItem { FilePath = Path.Combine(unavailable, "offline.cbz") };
        var standalone = new BookItem { FilePath = Path.Combine(Path.GetTempPath(), "standalone.cbz") };
        var found = new HashSet<string>([existing.FilePath], StringComparer.OrdinalIgnoreCase);

        var removed = LibrarySyncScope.GetMissingBooks(
            [existing, missing, protectedBook, standalone], found, [root], [unavailable]);

        Assert.Equal(missing.Id, Assert.Single(removed).Id);
    }
}
