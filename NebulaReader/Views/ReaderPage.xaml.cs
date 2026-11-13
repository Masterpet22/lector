using System.Diagnostics;
using NebulaReader.Models;
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
    private static readonly SKSamplingOptions HighQualitySampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private static readonly SKSamplingOptions FastGestureSampling = new(SKFilterMode.Linear, SKMipmapMode.None);
    private static readonly float[] SoftNightInvertMatrix =
    [
        -0.80f,  0.00f,  0.00f, 0.00f, 0.88f,
         0.00f, -0.80f,  0.00f, 0.00f, 0.88f,
         0.00f,  0.00f, -0.78f, 0.00f, 0.90f,
         0.00f,  0.00f,  0.00f, 1.00f, 0.00f
    ];
    private static readonly float[] SepiaMatrix =
    [
        0.393f, 0.769f, 0.189f, 0.00f, -0.04f,
        0.349f, 0.686f, 0.168f, 0.00f, -0.05f,
        0.272f, 0.534f, 0.131f, 0.00f, -0.08f,
        0.000f, 0.000f, 0.000f, 1.00f,  0.00f
    ];

    // Instancias reutilizables para evitar asignaciones GC por frame
    private static readonly SKPaint NightInvertPaint = new()
    {
        ColorFilter = SKColorFilter.CreateColorMatrix(SoftNightInvertMatrix)
    };
    private static readonly SKPaint SepiaPaint = new()
    {
        ColorFilter = SKColorFilter.CreateColorMatrix(SepiaMatrix)
    };
    private static readonly SKPaint PlaceholderFillPaint = new()
    {
        Color = new SKColor(0x1B, 0x1E, 0x28),
        Style = SKPaintStyle.Fill
    };
    private static readonly SKPaint PlaceholderBorderPaint = new()
    {
        Color = new SKColor(0x2E, 0x34, 0x46),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2f
    };
    private static readonly SKPaint PlaceholderTextPaint = new()
    {
        Color = new SKColor(0xA0, 0xA8, 0xB8),
        IsAntialias = true
    };
    private static readonly SKFont PlaceholderFont = new()
    {
        Size = 28f
    };

    private float _zoom = 1.0f;
    private float _panX;
    private float _panY;
    private float _verticalScrollOffset;
    private float _lastCurrentPageFittedHeight;
    private float _lastPrevPageFittedHeight;

#if ANDROID
    private readonly SKGLView _readerSurfaceView = new();
#else
    private readonly SKCanvasView _readerSurfaceView = new();
#endif

    private double _lastPanTotalX;
    private double _lastPanTotalY;
    private double _swipeAccumulatedX;
    private readonly (long TimestampTicks, float TotalYPx)[] _panSamples = new (long, float)[16];
    private int _panSampleCount;
    private int _panSampleWriteIndex;
    private float _flingVelocityPxPerSec;
    private float _pendingSmoothScrollPx;
    private long _lastVSyncTickTimestamp;
    private bool _isActivelyInteracting;
    private bool _sliderDragActive;
    private int _pendingSliderPage = 1;
    private bool _platformInputAttached;

    private SKSamplingOptions CurrentSampling => _isActivelyInteracting ? FastGestureSampling : HighQualitySampling;

    private float CanvasPixelWidth => _readerSurfaceView.CanvasSize.Width;
    private float CanvasPixelHeight => _readerSurfaceView.CanvasSize.Height;

    private void InvalidateCanvasSurface() => _readerSurfaceView.InvalidateSurface();

    private ReaderViewModel? ViewModel => BindingContext as ReaderViewModel;

    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        ConfigureSurfaceView();
        BindingContext = viewModel;
        viewModel.PageContentReady += OnPageContentReady;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void ConfigureSurfaceView()
    {
#if ANDROID
        _readerSurfaceView.PaintSurface += OnGlPaintSurface;
#else
        _readerSurfaceView.PaintSurface += OnCpuPaintSurface;
#endif
        _readerSurfaceView.HandlerChanged += OnCanvasHandlerChanged;

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += OnDoubleTapped;

        var singleTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        singleTap.Tapped += OnSingleTapped;

        _readerSurfaceView.GestureRecognizers.Add(pinch);
        _readerSurfaceView.GestureRecognizers.Add(pan);
        _readerSurfaceView.GestureRecognizers.Add(doubleTap);
        _readerSurfaceView.GestureRecognizers.Add(singleTap);

        CanvasHost.Content = _readerSurfaceView;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.CurrentPageIndex) && !(ViewModel?.IsContinuousVertical ?? false))
        {
            ResetZoomAndVerticalPosition();
            InvalidateCanvasSurface();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel?.UpdateViewportSize(Width, Height);
        InvalidateCanvasSurface();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ViewModel?.UpdateViewportSize(width, height);
        ClampPan();
        InvalidateCanvasSurface();
    }

    private void OnCanvasHandlerChanged(object? sender, EventArgs e)
    {
        if (_platformInputAttached || _readerSurfaceView.Handler?.PlatformView == null)
        {
            return;
        }

        _platformInputAttached = true;
        AttachPlatformInput(_readerSurfaceView);
    }

    private void OnPageContentReady(object? sender, EventArgs e)
    {
        InvalidateCanvasSurface();
    }

#if ANDROID
    private void OnGlPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        RenderSurface(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }
#else
    private void OnCpuPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        RenderSurface(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }
#endif

    private void RenderSurface(SKCanvas canvas, int pixelWidth, int pixelHeight)
    {
        var vm = ViewModel;

        var backgroundColor = vm?.IsNightMode ?? true
            ? new SKColor(0x10, 0x12, 0x18)
            : new SKColor(0xEC, 0xEE, 0xF2);
        canvas.Clear(backgroundColor);

        if (vm == null || vm.TotalPages <= 0)
        {
            return;
        }

        float width = pixelWidth;
        float height = pixelHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ClampPan(width, height);

        var transform = BuildTransformMatrix(width, height);
        canvas.Save();
        canvas.SetMatrix(transform);

        var filterPaint = GetCachedFilterPaint(vm.VisualFilter);

        if (vm.IsContinuousVertical)
        {
            DrawContinuousVertical(canvas, vm, width, height, filterPaint);
        }
        else if (vm.IsDoublePageActive)
        {
            DrawDoublePageSpread(canvas, vm, width, height, filterPaint);
        }
        else
        {
            DrawSinglePage(canvas, vm, vm.CurrentPageIndex, width, height, filterPaint);
        }

        canvas.Restore();
    }

    private static SKPaint? GetCachedFilterPaint(ReaderVisualFilter filter)
    {
        return filter switch
        {
            ReaderVisualFilter.NightInvert => NightInvertPaint,
            ReaderVisualFilter.Sepia => SepiaPaint,
            _ => null
        };
    }

    private void DrawSurfaceBitmapOrImage(SKCanvas canvas, SKImage? image, SKBitmap bitmap, SKRect dest, SKPaint? paint)
    {
#if ANDROID
        if (image != null)
        {
            canvas.DrawImage(image, dest, CurrentSampling, paint);
            return;
        }
#endif
        canvas.DrawBitmap(bitmap, dest, CurrentSampling, paint);
    }

    private void DrawSinglePage(SKCanvas canvas, ReaderViewModel vm, int pageIndex, float canvasWidth, float canvasHeight, SKPaint? paint)
    {
        vm.RequestPageLoadIfNeeded(pageIndex);

        if (!vm.TryDrawCachedPage(pageIndex, preferredFitWidthPx: null, (image, bitmap) =>
        {
            var dest = CalculateAspectFitRect(bitmap.Width, bitmap.Height, 0, 0, canvasWidth, canvasHeight);
            DrawSurfaceBitmapOrImage(canvas, image, bitmap, dest, paint);
        }))
        {
            DrawPlaceholderCard(canvas, pageIndex, 0, 0, canvasWidth, canvasHeight);
        }
    }

    private void DrawDoublePageSpread(SKCanvas canvas, ReaderViewModel vm, float canvasWidth, float canvasHeight, SKPaint? paint)
    {
        var currentIndex = vm.CurrentPageIndex;
        var nextIndex = currentIndex + 1 < vm.TotalPages ? currentIndex + 1 : -1;

        if (nextIndex < 0)
        {
            DrawSinglePage(canvas, vm, currentIndex, canvasWidth, canvasHeight, paint);
            return;
        }

        var leftIndex = vm.IsRightToLeftManga ? nextIndex : currentIndex;
        var rightIndex = vm.IsRightToLeftManga ? currentIndex : nextIndex;

        vm.RequestPageLoadIfNeeded(leftIndex);
        vm.RequestPageLoadIfNeeded(rightIndex);

        var halfWidth = (canvasWidth - PageGapPx) / 2f;

        vm.TryDrawCachedPage(leftIndex, preferredFitWidthPx: null, (image, leftBitmap) =>
        {
            var dest = CalculateAspectFitRect(leftBitmap.Width, leftBitmap.Height, 0, 0, halfWidth, canvasHeight);
            var aligned = new SKRect(halfWidth - dest.Width, dest.Top, halfWidth, dest.Bottom);
            DrawSurfaceBitmapOrImage(canvas, image, leftBitmap, aligned, paint);
        });

        vm.TryDrawCachedPage(rightIndex, preferredFitWidthPx: null, (image, rightBitmap) =>
        {
            var rightX = halfWidth + PageGapPx;
            var dest = CalculateAspectFitRect(rightBitmap.Width, rightBitmap.Height, rightX, 0, halfWidth, canvasHeight);
            var aligned = new SKRect(rightX, dest.Top, rightX + dest.Width, dest.Bottom);
            DrawSurfaceBitmapOrImage(canvas, image, rightBitmap, aligned, paint);
        });
    }

    private static float GetContinuousMaxPageWidth(float canvasWidth, float canvasHeight)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0) return Math.Max(1f, canvasWidth);
        return Math.Min(canvasWidth, canvasHeight * 1.15f);
    }

    private static float GetContinuousPageHeight(ReaderViewModel vm, int pageIndex, float maxPageWidth)
    {
        return maxPageWidth * vm.GetExpectedAspectRatio(pageIndex);
    }

    private int? GetContinuousFitWidthPx(float maxPageWidth)
    {
#if ANDROID
        // En Android SKGLView dibuja texturas directamente en GPU sin necesidad de copia CPU escalada
        return null;
#else
        if (_zoom > 1.02f || maxPageWidth <= 64f) return null;
        return (int)Math.Ceiling(maxPageWidth);
#endif
    }

    private void DrawContinuousVertical(SKCanvas canvas, ReaderViewModel vm, float canvasWidth, float canvasHeight, SKPaint? paint)
    {
        var current = vm.CurrentPageIndex;
        var maxPageWidth = GetContinuousMaxPageWidth(canvasWidth, canvasHeight);
        var fitWidthPx = GetContinuousFitWidthPx(maxPageWidth);
        if (fitWidthPx.HasValue)
        {
            vm.SetPreferredFitWidthPx(fitWidthPx.Value);
        }
        var x = (canvasWidth - maxPageWidth) / 2f;

        // Dibuja todos los espacios de página visibles hacia abajo [actual, actual + 1, actual + 2, ...]
        var topY = -_verticalScrollOffset;
        for (var pageIdx = current; pageIdx < vm.TotalPages && topY <= canvasHeight; pageIdx++)
        {
            var slotHeight = GetContinuousPageHeight(vm, pageIdx, maxPageWidth);
            if (pageIdx == current)
            {
                _lastCurrentPageFittedHeight = slotHeight;
            }

            if (topY + slotHeight >= 0)
            {
                vm.RequestPageLoadIfNeeded(pageIdx);
                var currentY = topY;
                var currentSlotHeight = slotHeight;

                var drew = vm.TryDrawCachedPage(pageIdx, fitWidthPx, (image, bitmap) =>
                {
                    currentSlotHeight = maxPageWidth * (bitmap.Height / (float)Math.Max(1, bitmap.Width));
                    if (pageIdx == current)
                    {
                        _lastCurrentPageFittedHeight = currentSlotHeight;
                    }
                    DrawSurfaceBitmapOrImage(
                        canvas,
                        image,
                        bitmap,
                        new SKRect(x, currentY, x + maxPageWidth, currentY + currentSlotHeight),
                        paint);
                });

                if (!drew)
                {
                    DrawPlaceholderCard(canvas, pageIdx, x, currentY, maxPageWidth, currentSlotHeight);
                }

                slotHeight = currentSlotHeight;
            }

            topY += slotHeight + PageGapPx;
        }

        // Dibuja los espacios visibles hacia arriba [actual - 1, actual - 2, ...]
        _lastPrevPageFittedHeight = 0;
        var bottomY = -_verticalScrollOffset - PageGapPx;
        for (var prevIdx = current - 1; prevIdx >= 0 && bottomY >= 0; prevIdx--)
        {
            var prevHeight = GetContinuousPageHeight(vm, prevIdx, maxPageWidth);
            vm.RequestPageLoadIfNeeded(prevIdx);

            float drawnHeight = prevHeight;
            var drawn = vm.TryDrawCachedPage(prevIdx, fitWidthPx, (image, prevBitmap) =>
            {
                drawnHeight = maxPageWidth * (prevBitmap.Height / (float)Math.Max(1, prevBitmap.Width));
                var prevTopY = bottomY - drawnHeight;
                if (bottomY >= 0 && prevTopY <= canvasHeight)
                {
                    DrawSurfaceBitmapOrImage(
                        canvas,
                        image,
                        prevBitmap,
                        new SKRect(x, prevTopY, x + maxPageWidth, bottomY),
                        paint);
                }
            });

            if (!drawn)
            {
                var prevTopY = bottomY - drawnHeight;
                if (bottomY >= 0 && prevTopY <= canvasHeight)
                {
                    DrawPlaceholderCard(canvas, prevIdx, x, prevTopY, maxPageWidth, drawnHeight);
                }
            }

            if (prevIdx == current - 1)
            {
                _lastPrevPageFittedHeight = drawnHeight;
            }

            bottomY -= drawnHeight + PageGapPx;
        }
    }

    private static void DrawPlaceholderCard(SKCanvas canvas, int pageIndex, float x, float y, float width, float height)
    {
        var rect = new SKRect(x, y, x + width, y + height);
        canvas.DrawRect(rect, PlaceholderFillPaint);
        canvas.DrawRect(rect, PlaceholderBorderPaint);
        canvas.DrawText(
            $"Página {pageIndex + 1} · Cargando...",
            x + (width / 2f),
            y + (height / 2f),
            SKTextAlign.Center,
            PlaceholderFont,
            PlaceholderTextPaint);
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
        var canvasWidth = CanvasPixelWidth;
        var canvasHeight = CanvasPixelHeight;
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
        InvalidateCanvasSurface();
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
        var prevPanY = _panY;
        _panY += deltaYPx;

        var width = CanvasPixelWidth;
        var height = CanvasPixelHeight;
        if (width > 0 && height > 0)
        {
            var maxPanY = height * (_zoom - 1f) / 2f;
            var unclampedPanY = _panY;
            ClampPan(width, height);

            // En lectura continua con zoom, al tocar el borde superior/inferior el resto del desplazamiento
            // continúa avanzando de arriba a abajo entre páginas.
            if (vm.IsContinuousVertical && Math.Abs(unclampedPanY) > maxPanY)
            {
                var overflowY = unclampedPanY - _panY;
                if (Math.Abs(overflowY) > 0.5f && Math.Abs(prevPanY - _panY) < Math.Abs(deltaYPx))
                {
                    ScrollContinuousVertical(-overflowY / _zoom);
                    return;
                }
            }
        }
        else
        {
            ClampPan();
        }

        InvalidateCanvasSurface();
    }

    private void ScrollContinuousVertical(float deltaScrollPx)
    {
        var vm = ViewModel;
        if (vm == null || vm.TotalPages <= 0) return;

        var canvasWidth = CanvasPixelWidth;
        var canvasHeight = CanvasPixelHeight;
        var maxPageWidth = GetContinuousMaxPageWidth(canvasWidth, canvasHeight);

        _verticalScrollOffset += deltaScrollPx;

        var targetIndex = vm.CurrentPageIndex;
        var currentSpan = GetContinuousPageHeight(vm, targetIndex, maxPageWidth) + PageGapPx;

        while (_verticalScrollOffset >= currentSpan && targetIndex + 1 < vm.TotalPages)
        {
            _verticalScrollOffset -= currentSpan;
            targetIndex++;
            currentSpan = GetContinuousPageHeight(vm, targetIndex, maxPageWidth) + PageGapPx;
        }

        while (_verticalScrollOffset < 0 && targetIndex > 0)
        {
            targetIndex--;
            var prevSpan = GetContinuousPageHeight(vm, targetIndex, maxPageWidth) + PageGapPx;
            _verticalScrollOffset += prevSpan;
            currentSpan = prevSpan;
        }

        if (targetIndex == 0 && _verticalScrollOffset < 0)
        {
            _verticalScrollOffset = 0;
        }

        if (targetIndex == vm.TotalPages - 1)
        {
            var maxScroll = Math.Max(0f, currentSpan - canvasHeight);
            _verticalScrollOffset = Math.Min(_verticalScrollOffset, maxScroll);
        }

        if (targetIndex != vm.CurrentPageIndex)
        {
            vm.SyncContinuousPage(targetIndex);
        }

        InvalidateCanvasSurface();
    }

    private void ResetZoomAndVerticalPosition()
    {
        StopFlingInertia();
        _zoom = 1.0f;
        _panX = 0f;
        _panY = 0f;
        _verticalScrollOffset = 0f;
    }

    private void QueueSmoothVerticalScroll(float deltaScrollPx)
    {
        if (Math.Abs(deltaScrollPx) < 0.5f) return;

        _flingVelocityPxPerSec = 0f;
        _pendingSmoothScrollPx += deltaScrollPx;
        _isActivelyInteracting = true;
        _lastVSyncTickTimestamp = Stopwatch.GetTimestamp();
        StartVSyncLoop();
    }

    private void StartFlingInertia(float velocityPxPerSec)
    {
        StopFlingInertia();
        _flingVelocityPxPerSec = Math.Clamp(velocityPxPerSec, -28000f, 28000f);
        if (Math.Abs(_flingVelocityPxPerSec) < 180f)
        {
            _flingVelocityPxPerSec = 0f;
            return;
        }

        _isActivelyInteracting = true;
        _lastVSyncTickTimestamp = Stopwatch.GetTimestamp();
        StartVSyncLoop();
    }

    private void AdvanceVSyncAnimation()
    {
        if (ViewModel == null || !ViewModel.IsContinuousVertical)
        {
            StopFlingInertia();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var dt = (float)((now - _lastVSyncTickTimestamp) / (double)Stopwatch.Frequency);
        dt = Math.Clamp(dt, 0.001f, 0.05f);
        _lastVSyncTickTimestamp = now;

        if (Math.Abs(_pendingSmoothScrollPx) > 0.5f)
        {
            var ease = 1f - MathF.Exp(-20f * dt);
            var step = _pendingSmoothScrollPx * ease;
            _pendingSmoothScrollPx -= step;
            ScrollContinuousVertical(step);
            if (Math.Abs(_pendingSmoothScrollPx) <= 0.5f)
            {
                if (Math.Abs(_pendingSmoothScrollPx) > 0.01f)
                {
                    ScrollContinuousVertical(_pendingSmoothScrollPx);
                }
                _pendingSmoothScrollPx = 0f;
                StopFlingInertia();
            }
            return;
        }

        if (Math.Abs(_flingVelocityPxPerSec) < 45f)
        {
            StopFlingInertia();
            return;
        }

        var flingStepPx = _flingVelocityPxPerSec * dt;
        ApplyPanDelta(0f, flingStepPx);
        _flingVelocityPxPerSec *= MathF.Exp(-2.6f * dt);
    }

    private void StopFlingInertia()
    {
        StopVSyncLoop();
        _flingVelocityPxPerSec = 0f;
        _pendingSmoothScrollPx = 0f;
        if (_isActivelyInteracting)
        {
            _isActivelyInteracting = false;
            InvalidateCanvasSurface();
        }
    }

    private void RecordPanSample(long timestampTicks, float totalYPx)
    {
        _panSamples[_panSampleWriteIndex] = (timestampTicks, totalYPx);
        _panSampleWriteIndex = (_panSampleWriteIndex + 1) % _panSamples.Length;
        if (_panSampleCount < _panSamples.Length)
        {
            _panSampleCount++;
        }
    }

    private float ComputeVerticalVelocityPxPerSecond(long nowTicks)
    {
        if (_panSampleCount < 2) return 0f;

        var latestIdx = (_panSampleWriteIndex - 1 + _panSamples.Length) % _panSamples.Length;
        var latest = _panSamples[latestIdx];

        // Si el dedo se detuvo más de 100 ms antes de levantarse, no hay inercia residual
        var ageMs = (nowTicks - latest.TimestampTicks) * 1000.0 / Stopwatch.Frequency;
        if (ageMs > 100.0) return 0f;

        var windowTicks = (long)(0.105 * Stopwatch.Frequency);
        var oldestInWindow = latest;

        for (var i = 1; i < _panSampleCount; i++)
        {
            var idx = (_panSampleWriteIndex - 1 - i + _panSamples.Length) % _panSamples.Length;
            var sample = _panSamples[idx];
            if (latest.TimestampTicks - sample.TimestampTicks > windowTicks)
            {
                break;
            }
            oldestInWindow = sample;
        }

        var dtSec = (latest.TimestampTicks - oldestInWindow.TimestampTicks) / (double)Stopwatch.Frequency;
        if (dtSec < 0.008) return 0f;

        return (float)((latest.TotalYPx - oldestInWindow.TotalYPx) / dtSec);
    }

    private void ClampPan()
    {
        var width = CanvasPixelWidth;
        var height = CanvasPixelHeight;
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
        if (_readerSurfaceView.Width <= 0 || CanvasPixelWidth <= 0)
        {
            return (float)DeviceDisplay.Current.MainDisplayInfo.Density;
        }

        return (float)(CanvasPixelWidth / _readerSurfaceView.Width);
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            StopFlingInertia();
            _isActivelyInteracting = true;
            return;
        }

        if (e.Status == GestureStatus.Completed || e.Status == GestureStatus.Canceled)
        {
            _isActivelyInteracting = false;
            InvalidateCanvasSurface();
            return;
        }

        if (e.Status != GestureStatus.Running) return;

        var canvasWidth = CanvasPixelWidth;
        var canvasHeight = CanvasPixelHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        _isActivelyInteracting = true;
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
                StopFlingInertia();
                _lastPanTotalX = 0;
                _lastPanTotalY = 0;
                _swipeAccumulatedX = 0;
                _panSampleCount = 0;
                _panSampleWriteIndex = 0;
                RecordPanSample(Stopwatch.GetTimestamp(), 0f);
                _isActivelyInteracting = _zoom > 1.02f || vm.IsContinuousVertical;
                break;

            case GestureStatus.Running:
                var dx = e.TotalX - _lastPanTotalX;
                var dy = e.TotalY - _lastPanTotalY;
                _lastPanTotalX = e.TotalX;
                _lastPanTotalY = e.TotalY;

                if (_zoom > 1.02f || vm.IsContinuousVertical)
                {
                    _isActivelyInteracting = true;
                    RecordPanSample(Stopwatch.GetTimestamp(), (float)(e.TotalY * scale));
                    ApplyPanDelta((float)(dx * scale), (float)(dy * scale));
                }
                else
                {
                    _swipeAccumulatedX += dx;
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (vm.IsContinuousVertical)
                {
                    var velocityPxPerSec = e.StatusType == GestureStatus.Completed
                        ? ComputeVerticalVelocityPxPerSecond(Stopwatch.GetTimestamp())
                        : 0f;

                    if (Math.Abs(velocityPxPerSec) >= 180f)
                    {
                        StartFlingInertia(velocityPxPerSec);
                    }
                    else
                    {
                        _isActivelyInteracting = false;
                        InvalidateCanvasSurface();
                    }
                }
                else if (_zoom > 1.02f)
                {
                    _isActivelyInteracting = false;
                    InvalidateCanvasSurface();
                }
                else
                {
                    _isActivelyInteracting = false;
                    if (Math.Abs(_swipeAccumulatedX) > 65)
                    {
                        ResetZoomAndVerticalPosition();
                        var swipeLeft = _swipeAccumulatedX < 0;
                        var command = swipeLeft ? vm.GoNextVisualCommand : vm.GoPreviousVisualCommand;
                        _ = command.ExecuteAsync(null);
                    }
                }
                _swipeAccumulatedX = 0;
                break;
        }
    }

    private bool TryScrollContinuousEdgeTap(double tapYDips, float canvasHeight)
    {
        if (_readerSurfaceView.Height <= 0 || canvasHeight <= 0) return false;

        var normalizedY = tapYDips / _readerSurfaceView.Height;
        var scrollStepPx = Math.Max(220f, canvasHeight * 0.70f);
        if (normalizedY < 0.22)
        {
            QueueSmoothVerticalScroll(-scrollStepPx);
            return true;
        }

        if (normalizedY > 0.78)
        {
            QueueSmoothVerticalScroll(scrollStepPx);
            return true;
        }

        return false;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        var vm = ViewModel;
        var canvasWidth = CanvasPixelWidth;
        var canvasHeight = CanvasPixelHeight;
        if (vm == null || canvasWidth <= 0 || canvasHeight <= 0 || _readerSurfaceView.Width <= 0) return;

        var pos = e.GetPosition(_readerSurfaceView);
        if (pos.HasValue)
        {
            var normalizedX = pos.Value.X / _readerSurfaceView.Width;
            // Los extremos izquierdo y derecho están reservados para navegación rápida, nunca para zoom
            if (normalizedX < 0.25 || normalizedX > 0.75)
            {
                if (!vm.IsContinuousVertical)
                {
                    ResetZoomAndVerticalPosition();
                    var cmd = normalizedX < 0.25 ? vm.GoPreviousVisualCommand : vm.GoNextVisualCommand;
                    _ = cmd.ExecuteAsync(null);
                }
                else
                {
                    TryScrollContinuousEdgeTap(pos.Value.Y, canvasHeight);
                }
                return;
            }
        }

        var scale = DipToPixelScale();
        var focalX = pos.HasValue ? (float)(pos.Value.X * scale) : canvasWidth / 2f;
        var focalY = pos.HasValue ? (float)(pos.Value.Y * scale) : canvasHeight / 2f;

        var targetZoom = _zoom > 1.15f ? MinZoom : DoubleTapZoom;
        ApplyZoomAtFocalPoint(targetZoom, focalX, focalY);
    }

    private void OnSingleTapped(object? sender, TappedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || _readerSurfaceView.Width <= 0) return;

        StopFlingInertia();

        var pos = e.GetPosition(_readerSurfaceView);
        if (!pos.HasValue)
        {
            vm.ToggleHudCommand.Execute(null);
            return;
        }

        if (vm.IsContinuousVertical)
        {
            if (!TryScrollContinuousEdgeTap(pos.Value.Y, CanvasPixelHeight))
            {
                vm.ToggleHudCommand.Execute(null);
            }
            return;
        }

        var normalizedX = pos.Value.X / _readerSurfaceView.Width;
        if (normalizedX < 0.25)
        {
            ResetZoomAndVerticalPosition();
            _ = vm.GoPreviousVisualCommand.ExecuteAsync(null);
        }
        else if (normalizedX > 0.75)
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
        StopFlingInertia();
        if (_sliderDragActive)
        {
            _sliderDragActive = false;
        }
        DetachPlatformInput();
        _platformInputAttached = false;
    }

    private partial void AttachPlatformInput(View surfaceView);
    private partial void DetachPlatformInput();
    private partial void StartVSyncLoop();
    private partial void StopVSyncLoop();
}
