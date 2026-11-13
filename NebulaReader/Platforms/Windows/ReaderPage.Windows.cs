using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using CompositionTarget = Microsoft.UI.Xaml.Media.CompositionTarget;

namespace NebulaReader.Views;

public partial class ReaderPage
{
    private bool _isPointerDragging;
    private Windows.Foundation.Point _lastDragDip;
    private UIElement? _attachedNativeElement;
    private UIElement? _attachedWindowRoot;
    private bool _vsyncRunning;

    private partial void AttachPlatformInput(View surfaceView)
    {
        if (surfaceView.Handler?.PlatformView is UIElement nativeElement && !ReferenceEquals(_attachedNativeElement, nativeElement))
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
        StopVSyncLoop();

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
    }

    private partial void StartVSyncLoop()
    {
        if (_vsyncRunning) return;
        _vsyncRunning = true;
        CompositionTarget.Rendering += OnWindowsCompositionRendering;
    }

    private partial void StopVSyncLoop()
    {
        if (!_vsyncRunning) return;
        _vsyncRunning = false;
        CompositionTarget.Rendering -= OnWindowsCompositionRendering;
    }

    private void OnWindowsCompositionRendering(object? sender, object e)
    {
        if (!_vsyncRunning) return;
        AdvanceVSyncAnimation();
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

            case VirtualKey.Down:
                if (vm.IsContinuousVertical)
                {
                    var stepPx = Math.Max(140f, CanvasPixelHeight * 0.16f);
                    QueueSmoothVerticalScroll(stepPx);
                    e.Handled = true;
                }
                else if (CanPanY())
                {
                    var stepPx = Math.Max(120f, CanvasPixelHeight * 0.16f);
                    ApplyPanDelta(0f, -stepPx);
                    e.Handled = true;
                }
                break;

            case VirtualKey.Up:
                if (vm.IsContinuousVertical)
                {
                    var stepPx = Math.Max(140f, CanvasPixelHeight * 0.16f);
                    QueueSmoothVerticalScroll(-stepPx);
                    e.Handled = true;
                }
                else if (CanPanY())
                {
                    var stepPx = Math.Max(120f, CanvasPixelHeight * 0.16f);
                    ApplyPanDelta(0f, stepPx);
                    e.Handled = true;
                }
                break;

            case VirtualKey.Space:
                if (vm.IsContinuousVertical)
                {
                    var stepPx = Math.Max(260f, CanvasPixelHeight * 0.72f);
                    QueueSmoothVerticalScroll(stepPx);
                    e.Handled = true;
                }
                break;

            case VirtualKey.F11:
                vm.ToggleFullScreenCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (vm.IsFullScreen)
                {
                    vm.ToggleFullScreenCommand.Execute(null);
                }
                else
                {
                    _ = vm.CloseReaderCommand.ExecuteAsync(null);
                }
                e.Handled = true;
                break;
        }
    }

    private Microsoft.UI.Xaml.Window? GetNativeWindow()
    {
        return (this.Window?.Handler?.PlatformView ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView) as Microsoft.UI.Xaml.Window;
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

        if (vm.IsContinuousVertical && _zoom <= 1.02f)
        {
            var stepScrollPx = (wheelDelta > 0 ? -115f : 115f) * pixelScale;
            QueueSmoothVerticalScroll(stepScrollPx);
            e.Handled = true;
        }
        else if (_zoom > 1.02f || CanPanY())
        {
            var stepPx = (wheelDelta > 0 ? 115f : -115f) * pixelScale;
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

        if (!CanPanContent() && !(ViewModel?.IsContinuousVertical ?? false))
        {
            return;
        }

        StopFlingInertia();
        _isActivelyInteracting = true;
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
        _isActivelyInteracting = false;
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }
        InvalidateCanvasSurface();
    }
}
