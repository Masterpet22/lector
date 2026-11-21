using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class ExternalDocumentTests
{
    [Fact]
    public void AndroidTreeAndDocumentUrisIdentifyTheSameFile()
    {
        var document = AndroidDocumentIdentity.TryCreate("content://provider/document/primary%3ABooks%2FMy%20Comic.cbz");
        var fromTree = AndroidDocumentIdentity.TryCreate("content://provider/tree/primary%3ABooks/document/primary%3ABooks%2FMy%20Comic.cbz");
        Assert.NotNull(document);
        Assert.Equal(document, fromTree);
        Assert.Equal(document.SourceKey, fromTree!.SourceKey);
        Assert.NotEqual(document, AndroidDocumentIdentity.TryCreate("content://other/document/primary%3ABooks%2FMy%20Comic.cbz"));
        Assert.NotEqual(document, AndroidDocumentIdentity.TryCreate("content://provider/document/primary%3ABooks%2FAnother.cbz"));
    }

    [Theory]
    [InlineData("https://provider/document/1")]
    [InlineData("content://provider/tree/folder")]
    [InlineData("content://provider/document/folder/file.cbz")]
    public void DoesNotConfuseNonDocumentUrisWithAndroidDocumentIds(string value) =>
        Assert.Null(AndroidDocumentIdentity.TryCreate(value));

    [Theory]
    [InlineData("comic.cbz")]
    [InlineData("comic.CBR")]
    [InlineData("document with spaces.PDF")]
    public void RecognizesSupportedCommandLineFiles(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        Assert.Equal([path], DocumentFileTypes.GetPathsFromArguments(["--debug", $"\"{path}\""]));
    }

    [Fact]
    public void ParsesFileUrisAndDeduplicatesWithoutFetchingWebLinks()
    {
        var path = Path.Combine(Path.GetTempPath(), "comic con espacios.cbz");
        Assert.Equal([path], DocumentFileTypes.GetPathsFromArguments(
            [new Uri(path).AbsoluteUri, path, "https://example.com/remote.pdf", "--open", "notes.txt", ""]));
    }

    [Fact]
    public async Task ColdLaunchWaitsForReadyAndProcessesRequestsInOrder()
    {
        var opened = new List<string>();
        var queue = new DocumentOpenQueue(_ => throw new Xunit.Sdk.XunitException("Unexpected error"));
        queue.Enqueue(() => { opened.Add("cold.cbz"); return Task.CompletedTask; });
        queue.Enqueue(() => { opened.Add("next.pdf"); return Task.CompletedTask; });
        await queue.DrainAsync();
        Assert.Empty(opened);
        Assert.True(queue.HasPendingRequests);
        queue.MarkReady();
        await queue.DrainAsync();
        Assert.Equal(["cold.cbz", "next.pdf"], opened);
        Assert.False(queue.HasPendingRequests);
        queue.Enqueue(() => { opened.Add("warm.cbr"); return Task.CompletedTask; });
        await queue.DrainAsync();
        Assert.Equal("warm.cbr", opened[^1]);
    }

    [Fact]
    public async Task ConcurrentDrainsDoNotOpenTwoDocumentsAtOnceAndFailuresDoNotBlockNextRequest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new List<string>();
        var errors = new List<Exception>();
        var queue = new DocumentOpenQueue(error => { errors.Add(error); return Task.CompletedTask; });
        queue.MarkReady();
        queue.Enqueue(async () =>
        {
            opened.Add("first");
            started.SetResult();
            await finish.Task;
        });
        var firstDrain = queue.DrainAsync();
        await started.Task;
        queue.Enqueue(() => throw new FileNotFoundException("missing.pdf"));
        queue.Enqueue(() => { opened.Add("last"); return Task.CompletedTask; });
        await queue.DrainAsync();
        Assert.Equal(["first"], opened);
        finish.SetResult();
        await firstDrain;
        Assert.Equal(["first", "last"], opened);
        Assert.IsType<FileNotFoundException>(Assert.Single(errors));
        Assert.False(queue.HasPendingRequests);
    }

    [Theory]
    [InlineData("../../comic.cbz")]
    [InlineData("folder\\document.pdf")]
    [InlineData("CON.cbr")]
    [InlineData("../bad:document?.pdf")]
    public async Task IncomingContentIsCopiedInsidePrivateStorageWithASafeName(string displayName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"nebula_external_{Guid.NewGuid():N}");
        try
        {
            var storage = new ImportedDocumentStorage(root);
            var path = await storage.StoreAsync("content://provider/123", displayName,
                () => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ReopeningSameUriKeepsLibraryPathAndDifferentUrisDoNotOverwriteEachOther()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nebula_external_{Guid.NewGuid():N}");
        try
        {
            var storage = new ImportedDocumentStorage(root);
            var first = await storage.StoreAsync("content://provider/1", "comic.cbz", Read);
            var second = await storage.StoreAsync("content://provider/2", "comic.cbz", Read);
            var reopened = await storage.StoreAsync("content://provider/1", "comic.cbz", Read);
            Assert.Equal(first, reopened);
            Assert.NotEqual(first, second);
            Assert.True(File.Exists(second));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EmptyOrCancelledCopiesPreserveExistingDocumentAndRemoveTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nebula_external_{Guid.NewGuid():N}");
        try
        {
            var storage = new ImportedDocumentStorage(root);
            var original = await storage.StoreAsync("content://provider/1", "comic.cbz", Read);
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.StoreAsync("content://provider/1", "comic.cbz",
                () => Task.FromResult<Stream>(new MemoryStream())));
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.StoreAsync("content://provider/1", "comic.cbz", Read,
                validate: _ => Task.FromException(new InvalidDataException("Documento inválido"))));
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.StoreAsync("content://provider/1", "comic.cbz",
                () => { cancellation.Cancel(); return Read(); }, cancellation.Token));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(original));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RegistrationOffersEveryFormatAndQuotesExecutableAndDocumentArguments()
    {
        var exe = Path.Combine(Path.GetTempPath(), "Nebula Reader", "NebulaReader.exe");
        var entries = FileAssociationRegistration.Build(exe);
        foreach (var extension in DocumentFileTypes.Extensions)
        {
            var progId = "NebulaReader." + extension[1..].ToUpperInvariant();
            Assert.Contains(entries, e => e.Key.EndsWith(@"\Capabilities\FileAssociations") && e.Name == extension && e.Value == progId);
            Assert.Contains(entries, e => e.Key == @"Software\Classes\" + extension + @"\OpenWithProgids" && e.Name == progId);
            Assert.Contains(entries, e => e.Key == @"Software\Classes\" + progId + @"\shell\open\command" &&
                e.Value == $"\"{exe}\" \"%1\"");
        }
        Assert.DoesNotContain(entries, e => e.Key.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => DocumentFileTypes.Extensions.Any(extension =>
            e.Key == @"Software\Classes\" + extension && e.Name == ""));
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData("https://example.com/app.exe")]
    public void RegistrationRejectsInvalidExecutablePaths(string path) =>
        Assert.Throws<ArgumentException>(() => FileAssociationRegistration.Build(path));

    private static Task<Stream> Read() => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
}
