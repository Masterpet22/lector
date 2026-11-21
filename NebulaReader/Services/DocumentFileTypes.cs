namespace NebulaReader.Services;

public static class DocumentFileTypes
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[] { ".cbz", ".cbr", ".pdf" });

    public static IReadOnlyList<string> GetPathsFromArguments(IEnumerable<string> arguments)
    {
        var paths = new List<string>();
        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument)) continue;
            var path = argument.Trim().Trim('"');
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                {
                    if (!uri.IsFile) continue;
                    path = uri.LocalPath;
                }
                if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                path = Path.GetFullPath(path);
                if (!paths.Contains(path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    paths.Add(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignorar parámetros de arranque que no son rutas de documentos.
            }
        }
        return paths;
    }
}

/// <summary>Unifica las URI de documento y de árbol de un mismo proveedor de Android.</summary>
internal sealed record AndroidDocumentIdentity(string Authority, string DocumentId)
{
    public string SourceKey => $"content://{Authority}/document/{Uri.EscapeDataString(DocumentId)}";

    public static AndroidDocumentIdentity? TryCreate(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || uri.Scheme != "content") return null;
        var parts = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).Split('/');
        if (parts.Length < 2 || parts[^2] != "document" || string.IsNullOrEmpty(parts[^1])) return null;
        return new AndroidDocumentIdentity(uri.GetComponents(UriComponents.StrongAuthority, UriFormat.UriEscaped).ToLowerInvariant(),
            Uri.UnescapeDataString(parts[^1]));
    }
}
