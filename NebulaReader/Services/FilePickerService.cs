namespace NebulaReader.Services;

/// <summary>
/// Selector de archivos y carpetas multiplataforma mediante clases parciales por plataforma.
/// </summary>
public partial class FilePickerService : IFilePickerService
{
    private static readonly string[] SupportedExtensions = [".cbz", ".cbr", ".pdf"];

    public async Task<string?> PickBookFileAsync(CancellationToken ct = default)
    {
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/pdf", "application/vnd.comicbook+zip", "application/vnd.comicbook-rar", "*/*" } },
            { DevicePlatform.WinUI, SupportedExtensions }
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

    private partial Task<string?> PickFolderPlatformAsync(CancellationToken ct);
}

