using System.IO.Compression;

namespace NebulaReader.Services;

/// <summary>
/// Implementación de IBookSource para cómics en formato CBZ / ZIP con ordenamiento natural.
/// </summary>
public sealed class CbzBookSource : IBookSource
{
    private readonly ZipArchive _archive;
    private readonly IReadOnlyList<ZipArchiveEntry> _pages;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public string Title { get; }
    public int PageCount => _pages.Count;

    public CbzBookSource(string filePath)
        : this(Path.GetFileNameWithoutExtension(filePath), File.OpenRead(filePath))
    {
    }

    public CbzBookSource(string title, Stream archiveStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        Title = string.IsNullOrWhiteSpace(title) ? "Sin título" : title;
        _archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: leaveOpen);
        _pages = _archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) && ComicArchiveEntryFilter.IsValidImageEntry(entry.FullName))
            .OrderBy(entry => entry.FullName, NaturalSortComparer.Instance)
            .ToArray();
    }

    public Task<Stream> GetPageStreamAsync(int pageIndex, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)pageIndex >= (uint)_pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"El índice de página debe estar entre 0 y {Math.Max(0, _pages.Count - 1)}.");
        }

        ct.ThrowIfCancellationRequested();

        return Task.Run<Stream>(() =>
        {
            ct.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var memoryStream = new MemoryStream();
                using var entryStream = _pages[pageIndex].Open();
                entryStream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
        }, ct);
    }

    public Task<Stream> GetCoverThumbnailAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pages.Count == 0)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        return GetPageStreamAsync(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            _archive.Dispose();
        }
    }
}
