using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace NebulaReader.Services;

public sealed partial class FullScreenService
{
    private partial void ApplyFullScreenPlatform(bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var nativeWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow == null)
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow == null)
            {
                return;
            }

            var targetKind = enabled ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default;
            if (appWindow.Presenter.Kind != targetKind)
            {
                appWindow.SetPresenter(targetKind);
            }
        });
    }
}

