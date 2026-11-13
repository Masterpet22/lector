using Android.Provider;
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
        var stream = resolver.OpenInputStream(uri)
            ?? throw new FileNotFoundException($"No se pudo abrir la URI: {uriString}");

        return Task.FromResult((stream, string.IsNullOrWhiteSpace(title) ? fileName : title, (string?)fileName));
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

