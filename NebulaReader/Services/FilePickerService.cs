namespace NebulaReader.Services;

/// <summary>
/// Selector de archivos y carpetas multiplataforma mediante clases parciales por plataforma.
/// </summary>
public partial class FilePickerService : IFilePickerService
{
    internal static readonly HashSet<string> SupportedBookExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz",
        ".cbr",
        ".pdf"
    };

    private static readonly string[] PickerExtensions = [".cbz", ".cbr", ".pdf"];

    public async Task<string?> PickBookFileAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/pdf", "application/vnd.comicbook+zip", "application/vnd.comicbook-rar", "application/zip", "application/x-rar-compressed", "*/*" } },
            { DevicePlatform.WinUI, PickerExtensions }
        });

        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Selecciona un cómic (.cbz, .cbr) o PDF",
            FileTypes = fileTypes
        });

        return result?.FullPath;
    }

    public Task<string?> PickFolderAsync(CancellationToken ct = default)
    {
        return PickFolderPlatformAsync(ct);
    }

    public Task<IReadOnlyList<string>> EnumerateBookFilesAsync(string folderPathOrUri, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPathOrUri);
        return EnumerateBookFilesPlatformAsync(folderPathOrUri, ct);
    }

    internal static bool IsSupportedBookFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith('.'))
        {
            return false;
        }

        return SupportedBookExtensions.Contains(Path.GetExtension(fileName));
    }

    private partial Task<string?> PickFolderPlatformAsync(CancellationToken ct);
    private partial Task<IReadOnlyList<string>> EnumerateBookFilesPlatformAsync(string folderPathOrUri, CancellationToken ct);
}
