using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp.Views.Maui.Controls;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace NebulaReader.Views;

public partial class ReaderPage
{
    private bool _isPointerDragging;
    private Windows.Foundation.Point _lastDragDip;
    private UIElement? _attachedNativeElement;
    private UIElement? _attachedWindowRoot;

    private partial void AttachPlatformInput(SKCanvasView canvasView)
    {
        if (canvasView.Handler?.PlatformView is UIElement nativeElement && !ReferenceEquals(_attachedNativeElement, nativeElement))
        {
            _attachedNativeElement = nativeElement;
            nativeElement.PointerWheelChanged += OnNativePointerWheelChanged;
            nativeElement.PointerPressed += OnNativePointerPressed;
            nativeElement.PointerMoved += OnNativePointerMoved;
            nativeElement.PointerReleased += OnNativePointerReleased;
            nativeElement.PointerCanceled += OnNativePointerReleased;
        }

        if (GetNativeWindow()?.Content is UIElement rootElement && !ReferenceEquals(_attachedWindowRoot, rootElement))
        {
            _attachedWindowRoot = rootElement;
            rootElement.PreviewKeyDown += OnWindowPreviewKeyDown;
        }
    }

    private partial void DetachPlatformInput()
    {
        if (_attachedNativeElement != null)
        {
            _attachedNativeElement.PointerWheelChanged -= OnNativePointerWheelChanged;
            _attachedNativeElement.PointerPressed -= OnNativePointerPressed;
            _attachedNativeElement.PointerMoved -= OnNativePointerMoved;
            _attachedNativeElement.PointerReleased -= OnNativePointerReleased;
            _attachedNativeElement.PointerCanceled -= OnNativePointerReleased;
            _attachedNativeElement = null;
        }

        if (_attachedWindowRoot != null)
        {
            _attachedWindowRoot.PreviewKeyDown -= OnWindowPreviewKeyDown;
            _attachedWindowRoot = null;
        }

        RestoreWindowedMode();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        switch (e.Key)
        {
            case VirtualKey.Left:
                ResetZoomAndVerticalPosition();
                _ = vm.GoPreviousVisualCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case VirtualKey.Right:
                ResetZoomAndVerticalPosition();
                _ = vm.GoNextVisualCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case VirtualKey.PageUp:
                ResetZoomAndVerticalPosition();
                _ = vm.PreviousPageCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case VirtualKey.PageDown:
                ResetZoomAndVerticalPosition();
                _ = vm.NextPageCommand.ExecuteAsync(null);
                e.Handled = true;
                break;

            case VirtualKey.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                RestoreWindowedMode();
                _ = vm.CloseReaderCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }

    private Microsoft.UI.Xaml.Window? GetNativeWindow()
    {
        return (this.Window?.Handler?.PlatformView ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView) as Microsoft.UI.Xaml.Window;
    }

    private AppWindow? GetAppWindow()
    {
        var nativeWindow = GetNativeWindow();
        if (nativeWindow == null) return null;

        var hwnd = WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void ToggleFullScreen()
    {
        var appWindow = GetAppWindow();
        if (appWindow == null) return;

        var nextKind = appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Default
            : AppWindowPresenterKind.FullScreen;
        appWindow.SetPresenter(nextKind);
    }

    private void RestoreWindowedMode()
    {
        var appWindow = GetAppWindow();
        if (appWindow != null && appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
    }

    private void OnNativePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element) return;

        var point = e.GetCurrentPoint(element);
        var wheelDelta = point.Properties.MouseWheelDelta;
        if (wheelDelta == 0) return;

        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var isCtrlPressed = ctrlState.HasFlag(CoreVirtualKeyStates.Down);
        var pixelScale = DipToPixelScale();

        if (isCtrlPressed)
        {
            var factor = wheelDelta > 0 ? 1.15f : 1f / 1.15f;
            var focalX = (float)(point.Position.X * pixelScale);
            var focalY = (float)(point.Position.Y * pixelScale);
            ApplyZoomAtFocalPoint(_zoom * factor, focalX, focalY);
            e.Handled = true;
            return;
        }

        var vm = ViewModel;
        if (vm == null) return;

        if (vm.IsContinuousVertical || _zoom > 1.02f)
        {
            var stepPx = (wheelDelta > 0 ? 90f : -90f) * pixelScale;
            ApplyPanDelta(0f, stepPx);
            e.Handled = true;
        }
        else
        {
            ResetZoomAndVerticalPosition();
            var cmd = wheelDelta < 0 ? vm.NextPageCommand : vm.PreviousPageCommand;
            _ = cmd.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void OnNativePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element) return;

        var point = e.GetCurrentPoint(element);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (_zoom <= 1.02f && !(ViewModel?.IsContinuousVertical ?? false))
        {
            return;
        }

        _isPointerDragging = true;
        _lastDragDip = point.Position;
        element.CapturePointer(e.Pointer);
    }

    private void OnNativePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDragging || sender is not UIElement element) return;

        var point = e.GetCurrentPoint(element);
        var dx = point.Position.X - _lastDragDip.X;
        var dy = point.Position.Y - _lastDragDip.Y;
        _lastDragDip = point.Position;

        var pixelScale = DipToPixelScale();
        ApplyPanDelta((float)(dx * pixelScale), (float)(dy * pixelScale));
        e.Handled = true;
    }

    private void OnNativePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDragging) return;

        _isPointerDragging = false;
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }
    }
}
