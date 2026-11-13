using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NebulaReader.Services;

public partial class FilePickerService
{
    private async partial Task<string?> PickFolderPlatformAsync(CancellationToken ct)
    {
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
}

