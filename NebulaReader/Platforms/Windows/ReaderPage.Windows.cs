using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using SkiaSharp.Views.Maui.Controls;
using Windows.System;
using Windows.UI.Core;

namespace NebulaReader.Views;

public partial class ReaderPage
{
    private bool _isPointerDragging;
    private Windows.Foundation.Point _lastDragDip;

    private partial void AttachPlatformInput(SKCanvasView canvasView)
    {
        if (canvasView.Handler?.PlatformView is not UIElement nativeElement)
        {
            return;
        }

        nativeElement.PointerWheelChanged += OnNativePointerWheelChanged;
        nativeElement.PointerPressed += OnNativePointerPressed;
        nativeElement.PointerMoved += OnNativePointerMoved;
        nativeElement.PointerReleased += OnNativePointerReleased;
        nativeElement.PointerCanceled += OnNativePointerReleased;
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
            // Rueda sin Ctrl: desplazamiento vertical sobre la página ampliada o modo continuo
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

