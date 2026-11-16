using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NebulaReader.Services;

public partial class FilePickerService
{
    private partial Task<string?> PickBookFilePlatformAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    private async partial Task<string?> PickFolderPlatformAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        folderPicker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);
        }

        var folder = await folderPicker.PickSingleFolderAsync().AsTask(ct);
        return folder?.Path;
    }

    private partial Task<IReadOnlyList<string>> EnumerateBookFilesPlatformAsync(string folderPathOrUri, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(folderPathOrUri))
            {
                throw new DirectoryNotFoundException($"La carpeta no está disponible: {folderPathOrUri}");
            }

            return Directory
                .EnumerateFiles(folderPathOrUri, "*.*", new EnumerationOptions
                {
                    IgnoreInaccessible = false,
                    RecurseSubdirectories = true
                })
                .Where(path => IsSupportedBookFileName(Path.GetFileName(path)))
                .OrderBy(path => path, NaturalSortComparer.Instance)
                .ToArray();
        }, ct);
    }

    private async partial Task<bool> SaveBackupPlatformAsync(Stream source, string suggestedName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add("Respaldo JSON", [".json"]);
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (window != null)
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSaveFileAsync().AsTask(ct);
        if (file == null) return false;
        using var bytes = new MemoryStream();
        if (source.CanSeek) source.Position = 0;
        await source.CopyToAsync(bytes, ct);
        await Windows.Storage.FileIO.WriteBytesAsync(file, bytes.ToArray()).AsTask(ct);
        return true;
    }
}
