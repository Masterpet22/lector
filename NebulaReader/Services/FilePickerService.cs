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

#if ANDROID
        return await PickBookFilePlatformAsync(ct).ConfigureAwait(false);
#else

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
#endif
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

    public async Task<Stream?> PickBackupAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/json", "text/plain", "*/*" } },
            { DevicePlatform.WinUI, new[] { ".json" } }
        });
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Selecciona un respaldo de Nebula Reader (.json)",
            FileTypes = fileTypes
        });
        return result == null ? null : await result.OpenReadAsync();
    }

    public Task<bool> SaveBackupAsync(Stream source, string suggestedName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedName);
        return SaveBackupPlatformAsync(source, suggestedName, ct);
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
    private partial Task<string?> PickBookFilePlatformAsync(CancellationToken ct);
    private partial Task<IReadOnlyList<string>> EnumerateBookFilesPlatformAsync(string folderPathOrUri, CancellationToken ct);
    private partial Task<bool> SaveBackupPlatformAsync(Stream source, string suggestedName, CancellationToken ct);
}
