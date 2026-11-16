namespace NebulaReader.Models;

public sealed class ImportedFolderEntry
{
    public required string PathOrUri { get; init; }

    public string DisplayName
    {
        get
        {
            if (PathOrUri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var decoded = Uri.UnescapeDataString(PathOrUri);
                var lastSegment = decoded.Split('/').LastOrDefault() ?? decoded;
                return lastSegment.Split(':').LastOrDefault() ?? lastSegment;
            }

            var name = Path.GetFileName(PathOrUri.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? PathOrUri : name;
        }
    }
}
