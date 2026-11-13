using NebulaReader.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace NebulaReader.Views;

/// <summary>
/// Visor de lectura basado en SKCanvasView con SKMatrix para zoom/pan, gestos táctiles,
/// soporte de ratón en Windows, modos de lectura y HUD translúcido.
/// </summary>
public partial class ReaderPage : ContentPage
{
    private const float MinZoom = 1.0f;
    private const float MaxZoom = 5.0f;
    private const float DoubleTapZoom = 2.25f;
    private const float PageGapPx = 16.0f;
    private static readonly SKSamplingOptions PageSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private float _zoom = 1.0f;
    private float _panX;
    private float _panY;
    private float _verticalScrollOffset;
    private float _lastCurrentPageFittedHeight;
    private float _lastPrevPageFittedHeight;

    private double _lastPanTotalX;
    private double _lastPanTotalY;
    private double _swipeAccumulatedX;
    private bool _sliderDragActive;
    private int _pendingSliderPage = 1;
    private bool _platformInputAttached;

    private ReaderViewModel? ViewModel => BindingContext as ReaderViewModel;

    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.PageContentReady += OnPageContentReady;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.CurrentPageIndex) && !(ViewModel?.IsContinuousVertical ?? false))
        {
            ResetZoomAndVerticalPosition();
            ReaderCanvas.InvalidateSurface();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel?.UpdateViewportSize(Width, Height);
        ReaderCanvas.InvalidateSurface();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ViewModel?.UpdateViewportSize(width, height);
        ClampPan();
        ReaderCanvas.InvalidateSurface();
    }

    private void OnCanvasHandlerChanged(object? sender, EventArgs e)
    {
        if (_platformInputAttached || ReaderCanvas.Handler?.PlatformView == null)
        {
            return;
        }

        _platformInputAttached = true;
        AttachPlatformInput(ReaderCanvas);
    }

    private void OnPageContentReady(object? sender, EventArgs e)
    {
        ReaderCanvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var vm = ViewModel;
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        var backgroundColor = vm?.IsNightMode ?? true
            ? new SKColor(0x10, 0x12, 0x18)
            : new SKColor(0xEC, 0xEE, 0xF2);
        canvas.Clear(backgroundColor);

        if (vm == null || vm.TotalPages <= 0)
        {
            return;
        }

        float width = info.Width;
        float height = info.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ClampPan(width, height);

        var transform = BuildTransformMatrix(width, height);
        canvas.Save();
        canvas.SetMatrix(transform);

        if (vm.IsContinuousVertical)
        {
            DrawContinuousVertical(canvas, vm, width, height);
        }
        else if (vm.IsDoublePageActive)
        {
            DrawDoublePageSpread(canvas, vm, width, height);
        }
        else
        {
            DrawSinglePage(canvas, vm, vm.CurrentPageIndex, width, height);
        }

        canvas.Restore();
    }

    private void DrawSinglePage(SKCanvas canvas, ReaderViewModel vm, int pageIndex, float canvasWidth, float canvasHeight)
    {
        vm.RequestPageLoadIfNeeded(pageIndex);

        vm.TryDrawCachedPage(pageIndex, bitmap =>
        {
            var dest = CalculateAspectFitRect(bitmap.Width, bitmap.Height, 0, 0, canvasWidth, canvasHeight);
            canvas.DrawBitmap(bitmap, dest, PageSampling);
        });
    }

    private void DrawDoublePageSpread(SKCanvas canvas, ReaderViewModel vm, float canvasWidth, float canvasHeight)
    {
        var currentIndex = vm.CurrentPageIndex;
        var nextIndex = currentIndex + 1 < vm.TotalPages ? currentIndex + 1 : -1;

        if (nextIndex < 0)
        {
            DrawSinglePage(canvas, vm, currentIndex, canvasWidth, canvasHeight);
            return;
        }

        var leftIndex = vm.IsRightToLeftManga ? nextIndex : currentIndex;
        var rightIndex = vm.IsRightToLeftManga ? currentIndex : nextIndex;

        vm.RequestPageLoadIfNeeded(leftIndex);
        vm.RequestPageLoadIfNeeded(rightIndex);

        var halfWidth = (canvasWidth - PageGapPx) / 2f;

        vm.TryDrawCachedPage(leftIndex, leftBitmap =>
        {
            var dest = CalculateAspectFitRect(leftBitmap.Width, leftBitmap.Height, 0, 0, halfWidth, canvasHeight);
            // Alinear la página izquierda hacia el lomo central
            var aligned = new SKRect(halfWidth - dest.Width, dest.Top, halfWidth, dest.Bottom);
            canvas.DrawBitmap(leftBitmap, aligned, PageSampling);
        });

        vm.TryDrawCachedPage(rightIndex, rightBitmap =>
        {
            var rightX = halfWidth + PageGapPx;
            var dest = CalculateAspectFitRect(rightBitmap.Width, rightBitmap.Height, rightX, 0, halfWidth, canvasHeight);
            // Alinear la página derecha hacia el lomo central
            var aligned = new SKRect(rightX, dest.Top, rightX + dest.Width, dest.Bottom);
            canvas.DrawBitmap(rightBitmap, aligned, PageSampling);
        });
    }

    private void DrawContinuousVertical(SKCanvas canvas, ReaderViewModel vm, float canvasWidth, float canvasHeight)
    {
        var current = vm.CurrentPageIndex;
        var maxPageWidth = Math.Min(canvasWidth, canvasHeight * 1.15f);
        var defaultPageHeight = maxPageWidth * 1.414f;

        float currentHeight = defaultPageHeight;
        vm.RequestPageLoadIfNeeded(current);
        vm.TryDrawCachedPage(current, bitmap =>
        {
            currentHeight = maxPageWidth * (bitmap.Height / (float)Math.Max(1, bitmap.Width));
            var x = (canvasWidth - maxPageWidth) / 2f;
            var y = -_verticalScrollOffset;
            if (y + currentHeight >= 0 && y <= canvasHeight)
            {
                canvas.DrawBitmap(bitmap, new SKRect(x, y, x + maxPageWidth, y + currentHeight), PageSampling);
            }
        });
        _lastCurrentPageFittedHeight = currentHeight;

        // Página anterior [actual - 1]
        if (current - 1 >= 0)
        {
            float prevHeight = defaultPageHeight;
            vm.RequestPageLoadIfNeeded(current - 1);
            vm.TryDrawCachedPage(current - 1, prevBitmap =>
            {
                prevHeight = maxPageWidth * (prevBitmap.Height / (float)Math.Max(1, prevBitmap.Width));
                var x = (canvasWidth - maxPageWidth) / 2f;
                var y = -_verticalScrollOffset - PageGapPx - prevHeight;
                if (y + prevHeight >= 0 && y <= canvasHeight)
                {
                    canvas.DrawBitmap(prevBitmap, new SKRect(x, y, x + maxPageWidth, y + prevHeight), PageSampling);
                }
            });
            _lastPrevPageFittedHeight = prevHeight;
        }
        else
        {
            _lastPrevPageFittedHeight = 0;
        }

        // Página siguiente [actual + 1]
        if (current + 1 < vm.TotalPages)
        {
            vm.RequestPageLoadIfNeeded(current + 1);
            vm.TryDrawCachedPage(current + 1, nextBitmap =>
            {
                var nextHeight = maxPageWidth * (nextBitmap.Height / (float)Math.Max(1, nextBitmap.Width));
                var x = (canvasWidth - maxPageWidth) / 2f;
                var y = -_verticalScrollOffset + currentHeight + PageGapPx;
                if (y + nextHeight >= 0 && y <= canvasHeight)
                {
                    canvas.DrawBitmap(nextBitmap, new SKRect(x, y, x + maxPageWidth, y + nextHeight), PageSampling);
                }
            });
        }
    }

    private static SKRect CalculateAspectFitRect(float bmpWidth, float bmpHeight, float areaX, float areaY, float areaWidth, float areaHeight)
    {
        var scale = Math.Min(areaWidth / Math.Max(1f, bmpWidth), areaHeight / Math.Max(1f, bmpHeight));
        var drawW = bmpWidth * scale;
        var drawH = bmpHeight * scale;
        var x = areaX + (areaWidth - drawW) / 2f;
        var y = areaY + (areaHeight - drawH) / 2f;
        return new SKRect(x, y, x + drawW, y + drawH);
    }

    private SKMatrix BuildTransformMatrix(float canvasWidth, float canvasHeight)
    {
        var scale = SKMatrix.CreateScale(_zoom, _zoom, canvasWidth / 2f, canvasHeight / 2f);
        return SKMatrix.Concat(SKMatrix.CreateTranslation(_panX, _panY), scale);
    }

    private void ApplyZoomAtFocalPoint(float newZoom, float focalXPx, float focalYPx)
    {
        var canvasWidth = ReaderCanvas.CanvasSize.Width;
        var canvasHeight = ReaderCanvas.CanvasSize.Height;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        var clampedZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(clampedZoom - _zoom) < 0.001f) return;

        var cx = canvasWidth / 2f;
        var cy = canvasHeight / 2f;
        var worldX = (focalXPx - cx - _panX) / _zoom + cx;
        var worldY = (focalYPx - cy - _panY) / _zoom + cy;

        _zoom = clampedZoom;
        _panX = focalXPx - cx - (worldX - cx) * _zoom;
        _panY = focalYPx - cy - (worldY - cy) * _zoom;

        ClampPan(canvasWidth, canvasHeight);
        ReaderCanvas.InvalidateSurface();
    }

    private void ApplyPanDelta(float deltaXPx, float deltaYPx)
    {
        var vm = ViewModel;
        if (vm == null) return;

        if (vm.IsContinuousVertical && _zoom <= 1.02f)
        {
            ScrollContinuousVertical(-deltaYPx);
            return;
        }

        if (_zoom <= 1.02f)
        {
            return;
        }

        _panX += deltaXPx;
        _panY += deltaYPx;
        ClampPan();
        ReaderCanvas.InvalidateSurface();
    }

    private void ScrollContinuousVertical(float deltaScrollPx)
    {
        var vm = ViewModel;
        if (vm == null || vm.TotalPages <= 0) return;

        _verticalScrollOffset += deltaScrollPx;

        var currentSpan = (_lastCurrentPageFittedHeight > 0 ? _lastCurrentPageFittedHeight : ReaderCanvas.CanvasSize.Height) + PageGapPx;
        if (_verticalScrollOffset >= currentSpan && vm.CurrentPageIndex + 1 < vm.TotalPages)
        {
            _verticalScrollOffset -= currentSpan;
            _ = vm.GoToPageAsync(vm.CurrentPageIndex + 1);
            return;
        }

        if (_verticalScrollOffset < 0)
        {
            if (vm.CurrentPageIndex > 0)
            {
                var prevSpan = (_lastPrevPageFittedHeight > 0 ? _lastPrevPageFittedHeight : ReaderCanvas.CanvasSize.Height) + PageGapPx;
                _verticalScrollOffset += prevSpan;
                _ = vm.GoToPageAsync(vm.CurrentPageIndex - 1);
                return;
            }

            _verticalScrollOffset = 0;
        }

        if (vm.CurrentPageIndex == vm.TotalPages - 1)
        {
            var maxScroll = Math.Max(0f, currentSpan - ReaderCanvas.CanvasSize.Height);
            _verticalScrollOffset = Math.Min(_verticalScrollOffset, maxScroll);
        }

        ReaderCanvas.InvalidateSurface();
    }

    private void ResetZoomAndVerticalPosition()
    {
        _zoom = 1.0f;
        _panX = 0f;
        _panY = 0f;
        _verticalScrollOffset = 0f;
    }

    private void ClampPan()
    {
        var width = ReaderCanvas.CanvasSize.Width;
        var height = ReaderCanvas.CanvasSize.Height;
        if (width > 0 && height > 0)
        {
            ClampPan(width, height);
        }
    }

    private void ClampPan(float canvasWidth, float canvasHeight)
    {
        if (_zoom <= 1.001f)
        {
            _zoom = 1.0f;
            _panX = 0f;
            _panY = 0f;
            return;
        }

        var maxPanX = canvasWidth * (_zoom - 1f) / 2f;
        var maxPanY = canvasHeight * (_zoom - 1f) / 2f;
        _panX = Math.Clamp(_panX, -maxPanX, maxPanX);
        _panY = Math.Clamp(_panY, -maxPanY, maxPanY);
    }

    private float DipToPixelScale()
    {
        if (ReaderCanvas.Width <= 0 || ReaderCanvas.CanvasSize.Width <= 0)
        {
            return (float)DeviceDisplay.Current.MainDisplayInfo.Density;
        }

        return (float)(ReaderCanvas.CanvasSize.Width / ReaderCanvas.Width);
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running) return;

        var canvasWidth = ReaderCanvas.CanvasSize.Width;
        var canvasHeight = ReaderCanvas.CanvasSize.Height;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        var focalX = (float)(e.ScaleOrigin.X * canvasWidth);
        var focalY = (float)(e.ScaleOrigin.Y * canvasHeight);
        ApplyZoomAtFocalPoint(_zoom * (float)e.Scale, focalX, focalY);
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        var scale = DipToPixelScale();

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _lastPanTotalX = 0;
                _lastPanTotalY = 0;
                _swipeAccumulatedX = 0;
                break;

            case GestureStatus.Running:
                var dx = e.TotalX - _lastPanTotalX;
                var dy = e.TotalY - _lastPanTotalY;
                _lastPanTotalX = e.TotalX;
                _lastPanTotalY = e.TotalY;

                if (_zoom > 1.02f || vm.IsContinuousVertical)
                {
                    ApplyPanDelta((float)(dx * scale), (float)(dy * scale));
                }
                else
                {
                    _swipeAccumulatedX += dx;
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_zoom <= 1.02f && !vm.IsContinuousVertical && Math.Abs(_swipeAccumulatedX) > 65)
                {
                    ResetZoomAndVerticalPosition();
                    var swipeLeft = _swipeAccumulatedX < 0;
                    var command = swipeLeft ? vm.GoNextVisualCommand : vm.GoPreviousVisualCommand;
                    _ = command.ExecuteAsync(null);
                }
                _swipeAccumulatedX = 0;
                break;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var canvasWidth = ReaderCanvas.CanvasSize.Width;
        var canvasHeight = ReaderCanvas.CanvasSize.Height;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        var pos = e.GetPosition(ReaderCanvas);
        var scale = DipToPixelScale();
        var focalX = pos.HasValue ? (float)(pos.Value.X * scale) : canvasWidth / 2f;
        var focalY = pos.HasValue ? (float)(pos.Value.Y * scale) : canvasHeight / 2f;

        var targetZoom = _zoom > 1.15f ? MinZoom : DoubleTapZoom;
        ApplyZoomAtFocalPoint(targetZoom, focalX, focalY);
    }

    private void OnSingleTapped(object? sender, TappedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || ReaderCanvas.Width <= 0) return;

        var pos = e.GetPosition(ReaderCanvas);
        if (!pos.HasValue)
        {
            vm.ToggleHudCommand.Execute(null);
            return;
        }

        var normalizedX = pos.Value.X / ReaderCanvas.Width;
        if (normalizedX < 0.25 && !vm.IsContinuousVertical)
        {
            ResetZoomAndVerticalPosition();
            _ = vm.GoPreviousVisualCommand.ExecuteAsync(null);
        }
        else if (normalizedX > 0.75 && !vm.IsContinuousVertical)
        {
            ResetZoomAndVerticalPosition();
            _ = vm.GoNextVisualCommand.ExecuteAsync(null);
        }
        else
        {
            vm.ToggleHudCommand.Execute(null);
        }
    }

    private void OnPageSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || vm.TotalPages <= 0) return;

        var targetPage = (int)Math.Round(e.NewValue);
        if (targetPage == vm.CurrentPageNumber) return;

        _sliderDragActive = true;
        _pendingSliderPage = targetPage;
        vm.PageIndicator = $"Página {targetPage} / {vm.TotalPages}";
    }

    private async void OnPageSliderDragCompleted(object? sender, EventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        _sliderDragActive = false;
        ResetZoomAndVerticalPosition();
        await vm.OnSliderPageSelectedAsync(_pendingSliderPage);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_sliderDragActive)
        {
            _sliderDragActive = false;
        }
    }

    private partial void AttachPlatformInput(SKCanvasView canvasView);
}
