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
    public void RemovingFolderKeepsBooksCoveredByAnotherRegisteredFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "Comics");
        var child = Path.Combine(root, "Series");
        var rootBook = new BookItem { FilePath = Path.Combine(root, "one.cbz") };
        var childBook = new BookItem { FilePath = Path.Combine(child, "two.cbz") };
        var neighborBook = new BookItem { FilePath = Path.Combine(Path.GetTempPath(), "ComicsExtra", "three.cbz") };
        var backupBook = new BookItem { FilePath = "backup://pending" };

        var removed = LibrarySyncScope.GetBooksForRemovedFolder(
            [rootBook, childBook, neighborBook, backupBook], root, [child]);

        Assert.Equal(rootBook.Id, Assert.Single(removed).Id);
    }

    [Fact]
    public void RemovingAndroidTreeSelectsOnlyItsDocuments()
    {
        const string tree = "content://com.android.externalstorage.documents/tree/primary%3AComics";
        var inTree = new BookItem { FilePath = tree + "/document/primary%3AComics%2Fone.cbz" };
        var elsewhere = new BookItem { FilePath = "content://com.android.externalstorage.documents/tree/primary%3AOther/document/primary%3AOther%2Ftwo.cbz" };

        Assert.Equal(inTree.Id, Assert.Single(LibrarySyncScope.GetBooksForRemovedFolder(
            [inTree, elsewhere], tree, [])).Id);
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
