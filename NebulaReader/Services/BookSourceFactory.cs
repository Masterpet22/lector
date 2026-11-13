using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Inspecciona extensión y cabecera (magic bytes) de archivos o URIs para devolver el IBookSource adecuado.
/// </summary>
public partial class BookSourceFactory : IBookSourceFactory
{
    private const int HeaderInspectLength = 1024;
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] RarMagicPrefix = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    public async Task<IBookSource> CreateAsync(string filePathOrUri, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathOrUri);
        ct.ThrowIfCancellationRequested();

        var (stream, title, extensionHint, isDirectPathOrContentUri) =
            await OpenSourceStreamAsync(filePathOrUri, ct).ConfigureAwait(false);

        try
        {
            var header = new byte[HeaderInspectLength];
            var bytesRead = await ReadHeaderBytesAsync(stream, header, ct).ConfigureAwait(false);
            var format = DetectFormat(header.AsSpan(0, bytesRead), extensionHint);

            if (format == BookFormat.Pdf)
            {
                if (isDirectPathOrContentUri)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    return await PdfBookSource.OpenAsync(filePathOrUri, title, ct).ConfigureAwait(false);
                }

                var seekablePdfStream = await EnsureSeekableAsync(stream, ct).ConfigureAwait(false);
                try
                {
                    return await PdfBookSource.OpenAsync(title, seekablePdfStream, ct).ConfigureAwait(false);
                }
                finally
                {
                    await seekablePdfStream.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (format is BookFormat.Cbz or BookFormat.Cbr)
            {
                var seekableStream = await EnsureSeekableAsync(stream, ct).ConfigureAwait(false);
                try
                {
                    return format == BookFormat.Cbz
                        ? new CbzBookSource(title, seekableStream, leaveOpen: false)
                        : new CbrBookSource(title, seekableStream, leaveOpen: false);
                }
                catch
                {
                    await seekableStream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            await stream.DisposeAsync().ConfigureAwait(false);
            throw new NotSupportedException($"Formato no soportado para '{filePathOrUri}'. Se esperaba CBZ, CBR o PDF.");
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<BookFormat> DetectFormatAsync(string filePathOrUri, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathOrUri);
        ct.ThrowIfCancellationRequested();

        var (stream, _, extensionHint, _) = await OpenSourceStreamAsync(filePathOrUri, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var header = new byte[HeaderInspectLength];
            var bytesRead = await ReadHeaderBytesAsync(stream, header, ct).ConfigureAwait(false);
            return DetectFormat(header.AsSpan(0, bytesRead), extensionHint);
        }
    }

    public BookFormat DetectFormat(ReadOnlySpan<byte> headerBytes, string? fileNameOrExtension = null)
    {
        // 1. Inspección por magic bytes (prevalece frente a extensiones mal renombradas)
        if (IsZipSignature(headerBytes))
        {
            return BookFormat.Cbz;
        }

        if (headerBytes.Length >= RarMagicPrefix.Length &&
            headerBytes[..RarMagicPrefix.Length].SequenceEqual(RarMagicPrefix))
        {
            return BookFormat.Cbr;
        }

        if (headerBytes.Length >= PdfMagic.Length &&
            headerBytes.IndexOf(PdfMagic) >= 0)
        {
            return BookFormat.Pdf;
        }

        // 2. Respaldo por extensión cuando no fue posible determinar por cabecera
        return DetectFormatByExtension(fileNameOrExtension);
    }

    private static bool IsZipSignature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
        {
            return false;
        }

        return (bytes[2] == 0x03 && bytes[3] == 0x04)
            || (bytes[2] == 0x05 && bytes[3] == 0x06)
            || (bytes[2] == 0x07 && bytes[3] == 0x08);
    }

    private static BookFormat DetectFormatByExtension(string? fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            return BookFormat.Unknown;
        }

        var ext = fileNameOrExtension.StartsWith('.')
            ? fileNameOrExtension
            : Path.GetExtension(fileNameOrExtension);

        return ext.ToLowerInvariant() switch
        {
            ".cbz" or ".zip" => BookFormat.Cbz,
            ".cbr" or ".rar" => BookFormat.Cbr,
            ".pdf" => BookFormat.Pdf,
            _ => BookFormat.Unknown
        };
    }

    private async Task<(Stream Stream, string Title, string? ExtensionHint, bool IsDirectPathOrContentUri)> OpenSourceStreamAsync(
        string filePathOrUri,
        CancellationToken ct)
    {
        if (filePathOrUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = new Uri(filePathOrUri).LocalPath;
            return (File.OpenRead(localPath), Path.GetFileNameWithoutExtension(localPath), Path.GetFileName(localPath), true);
        }

        if (!Uri.TryCreate(filePathOrUri, UriKind.Absolute, out var parsedUri) || parsedUri.IsFile)
        {
            return (File.OpenRead(filePathOrUri), Path.GetFileNameWithoutExtension(filePathOrUri), Path.GetFileName(filePathOrUri), true);
        }

        var (platformStream, title, fileName) = await OpenPlatformUriAsync(filePathOrUri, ct).ConfigureAwait(false);
        return (platformStream, title, fileName, true);
    }

    private static async Task<int> ReadHeaderBytesAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return totalRead;
    }

    private static async Task<Stream> EnsureSeekableAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        // Respalda streams no buscables (p. ej. Android SAF content://) en caché en disco con DeleteOnClose
        // para evitar cargar archivos CBR/CBZ grandes completos en RAM.
        var tempDir = Path.Combine(FileSystem.CacheDirectory, "stream_cache");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"nebula_{Guid.NewGuid():N}.tmp");

        var backingStream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        try
        {
            await stream.CopyToAsync(backingStream, 81920, ct).ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
            backingStream.Position = 0;
            return backingStream;
        }
        catch
        {
            await backingStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private partial Task<(Stream Stream, string Title, string? FileName)> OpenPlatformUriAsync(string uriString, CancellationToken ct);
}

