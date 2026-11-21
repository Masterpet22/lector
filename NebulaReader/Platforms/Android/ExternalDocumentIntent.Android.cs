using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace NebulaReader.Services;

internal static class ExternalDocumentIntent
{
    public static void Handle(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data is not { } uri) return;
        var services = IPlatformApplication.Current?.Services;
        if (services == null) return;
        var uriString = uri.ToString();
        if (string.IsNullOrWhiteSpace(uriString)) return;
        var mimeType = intent.Type;
        services.GetRequiredService<ExternalDocumentOpenService>().Enqueue(() => PrepareAsync(uriString, mimeType));
    }

    private static async Task<string> PrepareAsync(string uriString, string? mimeType)
    {
        var uri = AndroidUri.Parse(uriString) ?? throw new InvalidDataException("La ubicación del documento no es válida.");
        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
            return uri.Path ?? throw new FileNotFoundException("No se recibió la ruta del documento.");
        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Solo se pueden abrir archivos locales o documentos compartidos por otra aplicación.");

        var resolver = Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("No se pudo acceder al documento recibido.");
        try
        {
            var existingPath = await FindExistingReadableDocumentAsync(resolver, uriString);
            if (existingPath != null) return existingPath;
            string? displayName = null;
            using (var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null))
            {
                if (cursor?.MoveToFirst() == true)
                {
                    var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                    if (index >= 0) displayName = cursor.GetString(index);
                }
            }
            displayName ??= (mimeType ?? resolver.GetType(uri)) switch
            {
                "application/pdf" => "documento.pdf",
                "application/vnd.comicbook+zip" or "application/x-cbz" or "application/zip" => "documento.cbz",
                "application/vnd.comicbook-rar" or "application/x-cbr" or "application/vnd.rar" or
                    "application/x-rar-compressed" => "documento.cbr",
                _ => "documento"
            };

            // ACTION_VIEW suele conceder acceso solo mientras la actividad está abierta.
            // Mantener una copia privada permite reanudar el libro tras cerrar la aplicación.
            var storage = new ImportedDocumentStorage(Path.Combine(FileSystem.AppDataDirectory, "ImportedDocuments"));
            var factory = IPlatformApplication.Current!.Services.GetRequiredService<IBookSourceFactory>();
            var sourceId = AndroidDocumentIdentity.TryCreate(uriString)?.SourceKey ?? uriString;
            return await storage.StoreAsync(sourceId, displayName, () => Task.FromResult(
                resolver.OpenInputStream(uri) ?? throw new FileNotFoundException("No se pudo leer el documento recibido.")),
                validate: async path =>
                {
                    using var source = await factory.CreateAsync(path);
                    if (source.PageCount == 0)
                        throw new InvalidDataException("El documento no contiene páginas compatibles.");
                });
        }
        catch (Java.Lang.SecurityException ex)
        {
            throw new UnauthorizedAccessException("La aplicación de origen no concedió acceso al documento. Vuelve a abrirlo desde su gestor de archivos.", ex);
        }
    }

    private static async Task<string?> FindExistingReadableDocumentAsync(ContentResolver resolver, string receivedUri)
    {
        var grants = resolver.PersistedUriPermissions?
            .Where(permission => permission.IsReadPermission)
            .Select(permission => permission.Uri?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (grants == null || grants.Length == 0) return null;
        var database = IPlatformApplication.Current!.Services.GetRequiredService<ILibraryDatabase>();
        var identity = AndroidDocumentIdentity.TryCreate(receivedUri);
        foreach (var book in await database.GetBooksAsync())
        {
            if (!book.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)) continue;
            if (book.FilePath != receivedUri && (identity == null || AndroidDocumentIdentity.TryCreate(book.FilePath) != identity)) continue;
            if (!grants.Any(grant => book.FilePath == grant || book.FilePath.StartsWith(grant!.TrimEnd('/') + "/", StringComparison.Ordinal))) continue;
            try
            {
                using var stream = resolver.OpenInputStream(AndroidUri.Parse(book.FilePath)!);
                if (stream != null) return book.FilePath;
            }
            catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.IO.FileNotFoundException or IOException or UnauthorizedAccessException)
            {
                // Si el acceso persistente ya no funciona, conservar una copia del documento recibido.
            }
        }
        return null;
    }
}
