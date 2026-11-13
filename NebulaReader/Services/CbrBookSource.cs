using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Readers;

namespace NebulaReader.Services;

/// <summary>
/// Implementación de IBookSource para cómics en formato CBR / RAR usando SharpCompress.
/// </summary>
public sealed class CbrBookSource : IBookSource
{
    private readonly Stream _archiveStream;
    private readonly bool _leaveOpen;
    private readonly IRarArchive _archive;
    private readonly IReadOnlyList<IArchiveEntry> _pages;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public string Title { get; }
    public int PageCount => _pages.Count;

    public CbrBookSource(string filePath)
        : this(Path.GetFileNameWithoutExtension(filePath), File.OpenRead(filePath))
    {
    }

    public CbrBookSource(string title, Stream archiveStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        Title = string.IsNullOrWhiteSpace(title) ? "Sin título" : title;
        _archiveStream = archiveStream;
        _leaveOpen = leaveOpen;
        _archive = RarArchive.OpenArchive(archiveStream, new ReaderOptions { LeaveStreamOpen = true });
        _pages = _archive.Entries
            .Where(entry => !entry.IsDirectory && ComicArchiveEntryFilter.IsValidImageEntry(entry.Key))
            .OrderBy(entry => entry.Key ?? string.Empty, NaturalSortComparer.Instance)
            .Cast<IArchiveEntry>()
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
                using var entryStream = _pages[pageIndex].OpenEntryStream();
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
            if (!_leaveOpen)
            {
                _archiveStream.Dispose();
            }
        }
    }
}

