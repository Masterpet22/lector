using Android.Provider;
using Microsoft.Win32.SafeHandles;
using AndroidUri = Android.Net.Uri;
using AppContext = Android.App.Application;

namespace NebulaReader.Services;

public partial class BookSourceFactory
{
    private partial Task<(Stream Stream, string Title, string? FileName)> OpenPlatformUriAsync(string uriString, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var uri = AndroidUri.Parse(uriString)
            ?? throw new InvalidOperationException($"URI inválida: {uriString}");

        var resolver = AppContext.Context.ContentResolver
            ?? throw new InvalidOperationException("ContentResolver no disponible en Android.");

        var fileName = ResolveDisplayName(resolver, uri) ?? uri.LastPathSegment ?? "documento";
        var title = Path.GetFileNameWithoutExtension(fileName);
        var stream = TryOpenSeekableDescriptorStream(resolver, uri)
            ?? resolver.OpenInputStream(uri)
            ?? throw new FileNotFoundException($"No se pudo abrir la URI: {uriString}");

        return Task.FromResult((stream, string.IsNullOrWhiteSpace(title) ? fileName : title, (string?)fileName));
    }

    private static Stream? TryOpenSeekableDescriptorStream(Android.Content.ContentResolver resolver, AndroidUri uri)
    {
        try
        {
            using var pfd = resolver.OpenFileDescriptor(uri, "r");
            if (pfd == null)
            {
                return null;
            }

            // DetachFd transfiere la propiedad del descriptor al SafeFileHandle para lectura seekable sin copias en caché.
            var rawFd = pfd.DetachFd();
            var handle = new SafeFileHandle(new IntPtr(rawFd), ownsHandle: true);
            var fileStream = new FileStream(handle, FileAccess.Read, bufferSize: 65536, isAsync: false);

            if (!fileStream.CanSeek || fileStream.Length <= 0)
            {
                fileStream.Dispose();
                return null;
            }

            return fileStream;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveDisplayName(Android.Content.ContentResolver resolver, AndroidUri uri)
    {
        using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        if (cursor != null && cursor.MoveToFirst())
        {
            var idx = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
            if (idx >= 0)
            {
                return cursor.GetString(idx);
            }
        }

        return null;
    }
}

