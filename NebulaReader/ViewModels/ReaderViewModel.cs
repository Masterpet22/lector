using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NebulaReader.Models;
using NebulaReader.Services;
using SkiaSharp;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel del visor de lectura (CBR, CBZ y PDF) con caché deslizante amplia, precarga prioritaria
/// de páginas y reserva anticipada de espacios para lectura fluida.
/// </summary>
public partial class ReaderViewModel : BaseViewModel, IQueryAttributable
{
    private const int PhoneMaxDecodeDimensionPx = 2400;
    private const int DesktopMaxDecodeDimensionPx = 3600;
    private const int SpaceProbeLookaheadCount = 12;
    private static readonly TimeSpan ProgressPersistDebounce = TimeSpan.FromMilliseconds(750);

    private readonly ILibraryService _libraryService;
    private readonly IPageCacheService _pageCacheService;
    private readonly IBookSourceFactory _bookSourceFactory;
    private readonly IReaderSettingsService _settingsService;
    private readonly IOrientationService _orientationService;

    private IBookSource? _bookSource;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _preloadLoopCts;
    private CancellationTokenSource? _persistDebounceCts;
    private readonly SemaphoreSlim _preloadSignal = new(0);
    private readonly ConcurrentDictionary<int, float> _pageAspectRatios = new();
    private readonly object _pageLoadSync = new();
    private readonly HashSet<int> _inFlightPages = new();
    private readonly HashSet<(int PageIndex, int WidthPx)> _inFlightFitBuilds = new();
    private float _defaultPageAspectRatio = 1.414f;
    private int _lastNavigationDirection = 1;
    private double _viewportWidth;
    private double _viewportHeight;
    private bool _suppressSliderSync;

    public event EventHandler? PageContentReady;

    [ObservableProperty]
    public partial BookItem? CurrentBook { get; set; }

    [ObservableProperty]
    public partial int CurrentPageIndex { get; set; }

    [ObservableProperty]
    public partial int TotalPages { get; set; }

    [ObservableProperty]
    public partial int CurrentPageNumber { get; set; } = 1;

    [ObservableProperty]
    public partial int SliderMaximum { get; set; } = 2;

    [ObservableProperty]
    public partial string PageIndicator { get; set; } = "Página 0 / 0";

    [ObservableProperty]
    public partial bool IsHudVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsRotationLocked { get; set; }

    [ObservableProperty]
    public partial bool IsRightToLeftManga { get; set; }

    [ObservableProperty]
    public partial bool IsContinuousVertical { get; set; }

    [ObservableProperty]
    public partial bool IsDoublePageEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDoublePageActive { get; set; }

    [ObservableProperty]
    public partial bool IsNightMode { get; set; }

    [ObservableProperty]
    public partial ReaderVisualFilter VisualFilter { get; set; }

    public bool CanScrubPages => TotalPages > 1;
    public string RotationLockLabel => IsRotationLocked ? "Rotación: Fija" : "Rotación: Libre";
    public string DirectionModeLabel => IsRightToLeftManga ? "Manga (D→I)" : "Cómic (I→D)";
    public string LayoutModeLabel => IsContinuousVertical
        ? "Continua (↓)"
        : (IsDoublePageActive ? "Doble Pág." : "Paginado");
    public string FilterModeLabel => VisualFilter switch
    {
        ReaderVisualFilter.NightInvert => "Filtro: Nocturno",
        ReaderVisualFilter.Sepia => "Filtro: Sepia",
        _ => "Filtro: Normal"
    };

    public ReaderViewModel(
        ILibraryService libraryService,
        IPageCacheService pageCacheService,
        IBookSourceFactory bookSourceFactory,
        IReaderSettingsService settingsService,
        IOrientationService orientationService)
    {
        _libraryService = libraryService;
        _pageCacheService = pageCacheService;
        _bookSourceFactory = bookSourceFactory;
        _settingsService = settingsService;
        _orientationService = orientationService;

        Title = "Lector";
        SyncFromSettings();
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Book", out var value) && value is BookItem book)
        {
            CurrentBook = book;
            Title = book.Title;
            TotalPages = book.TotalPages;
            SliderMaximum = Math.Max(2, TotalPages);
            OnPropertyChanged(nameof(CanScrubPages));

            SetPageIndexCore(Math.Max(0, book.CurrentPage - 1));
            _ = LoadBookSourceAsync(book);
        }
    }

    public void UpdateViewportSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _viewportWidth = width;
        _viewportHeight = height;
        RecalculateDoublePageState();
    }

    public float GetExpectedAspectRatio(int pageIndex)
    {
        return _pageAspectRatios.TryGetValue(pageIndex, out var ratio)
            ? ratio
            : _defaultPageAspectRatio;
    }

    private int _preferredFitWidthPx;

    public void SetPreferredFitWidthPx(int fitWidthPx)
    {
        if (fitWidthPx > 64)
        {
            _preferredFitWidthPx = fitWidthPx;
        }
    }

    public bool TryDrawCachedPage(int pageIndex, Action<SKBitmap> drawAction)
    {
        if (CurrentBook == null || (uint)pageIndex >= (uint)TotalPages)
        {
            return false;
        }

        return _pageCacheService.TryDrawBitmap(CurrentBook.Id, pageIndex, drawAction);
    }

    public bool TryDrawCachedPage(int pageIndex, int? preferredFitWidthPx, Action<SKImage?, SKBitmap> drawAction)
    {
        var book = CurrentBook;
        if (book == null || (uint)pageIndex >= (uint)TotalPages)
        {
            return false;
        }

        var drawn = _pageCacheService.TryDrawPage(book.Id, pageIndex, preferredFitWidthPx, (image, bitmap) =>
        {
            drawAction(image, bitmap);

            // Si en modo CPU la imagen origen es mucho mayor al ancho mostrado, generar copia 1:1 en segundo plano
            if (preferredFitWidthPx is > 64 && bitmap.Width > preferredFitWidthPx.Value * 1.15f)
            {
                ScheduleFittedBitmapCreation(book.Id, pageIndex, preferredFitWidthPx.Value);
            }
        });

        return drawn;
    }

    private void ScheduleFittedBitmapCreation(string bookId, int pageIndex, int targetWidthPx)
    {
        if (targetWidthPx <= 64) return;

        var key = (pageIndex, targetWidthPx);
        lock (_pageLoadSync)
        {
            if (_inFlightFitBuilds.Contains(key)) return;
            _inFlightFitBuilds.Add(key);
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var pin = _pageCacheService.TryPinBitmap(bookId, pageIndex, out var pinnedBitmap);
                if (pin == null || pinnedBitmap == null) return;

                var fitted = CreateFittedBitmapSafe(pinnedBitmap, targetWidthPx);
                if (fitted != null)
                {
                    _pageCacheService.SetFittedBitmap(bookId, pageIndex, fitted, targetWidthPx);
                }
            }
            catch
            {
                // Ignorar si la página fue expulsada antes de completar el reescalado
            }
            finally
            {
                lock (_pageLoadSync)
                {
                    _inFlightFitBuilds.Remove(key);
                }
            }
        });
    }

    private static SKBitmap? CreateFittedBitmapSafe(SKBitmap sourceBitmap, int targetWidthPx)
    {
        if (sourceBitmap.Handle == IntPtr.Zero || sourceBitmap.Width <= targetWidthPx * 1.15f)
        {
            return null;
        }

        var scale = targetWidthPx / (double)sourceBitmap.Width;
        var targetHeight = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));
        var info = new SKImageInfo(targetWidthPx, targetHeight, sourceBitmap.ColorType, sourceBitmap.AlphaType);
        return sourceBitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    public void RequestPageLoadIfNeeded(int pageIndex)
    {
        var book = CurrentBook;
        if (book == null || _bookSource == null || (uint)pageIndex >= (uint)TotalPages)
        {
            return;
        }

        var (minKeep, maxKeep) = GetKeepWindowRange(CurrentPageIndex);
        if (pageIndex < minKeep || pageIndex > maxKeep)
        {
            return;
        }

        if (_pageCacheService.ContainsPage(book.Id, pageIndex))
        {
            return;
        }

        SignalPreloadWorker();
    }

    private (int Behind, int Ahead) GetWindowBounds()
    {
        var isPhone = IsPhoneDevice();
        if (IsContinuousVertical)
        {
            return isPhone ? (2, 5) : (3, 7);
        }

        if (IsDoublePageActive)
        {
            return isPhone ? (2, 4) : (2, 5);
        }

        return isPhone ? (1, 3) : (2, 4);
    }

    private (int MinKeep, int MaxKeep) GetKeepWindowRange(int center)
    {
        var (behind, ahead) = GetWindowBounds();
        var maxIdx = Math.Max(0, TotalPages - 1);
        return (Math.Max(0, center - behind), Math.Min(maxIdx, center + ahead));
    }

    public Task GoToPageAsync(int pageIndex)
    {
        UpdateActivePage(pageIndex, raiseCanvasInvalidate: true);
        return Task.CompletedTask;
    }

    public void SyncContinuousPage(int pageIndex)
    {
        UpdateActivePage(pageIndex, raiseCanvasInvalidate: false);
    }

    private void UpdateActivePage(int pageIndex, bool raiseCanvasInvalidate)
    {
        if (CurrentBook == null || TotalPages <= 0) return;

        var clamped = Math.Clamp(pageIndex, 0, TotalPages - 1);
        if (clamped == CurrentPageIndex && _pageCacheService.ContainsPage(CurrentBook.Id, clamped))
        {
            return;
        }

        _lastNavigationDirection = clamped >= CurrentPageIndex ? 1 : -1;
        SetPageIndexCore(clamped);

        var (minKeep, maxKeep) = GetKeepWindowRange(CurrentPageIndex);
        _pageCacheService.EvictOutsideRange(CurrentBook.Id, minKeep, maxKeep);
        if (raiseCanvasInvalidate)
        {
            RaisePageContentReady();
        }
        SignalPreloadWorker();
        ScheduleDebouncedProgressPersist();
    }

    [RelayCommand]
    private Task NextPageAsync()
    {
        var step = IsDoublePageActive ? 2 : 1;
        return GoToPageAsync(CurrentPageIndex + step);
    }

    [RelayCommand]
    private Task PreviousPageAsync()
    {
        var step = IsDoublePageActive ? 2 : 1;
        return GoToPageAsync(CurrentPageIndex - step);
    }

    [RelayCommand]
    private Task GoNextVisualAsync()
    {
        // En modo Manga (D→I), avanzar físicamente por el borde izquierdo / botón visual sigue la lógica de lectura
        return IsRightToLeftManga && !IsContinuousVertical
            ? PreviousPageAsync()
            : NextPageAsync();
    }

    [RelayCommand]
    private Task GoPreviousVisualAsync()
    {
        return IsRightToLeftManga && !IsContinuousVertical
            ? NextPageAsync()
            : PreviousPageAsync();
    }

    [RelayCommand]
    private void ToggleHud()
    {
        IsHudVisible = !IsHudVisible;
    }

    [RelayCommand]
    private void ToggleRotationLock()
    {
        var next = !IsRotationLocked;
        _orientationService.SetRotationLocked(next);
        IsRotationLocked = next;
        OnPropertyChanged(nameof(RotationLockLabel));
    }

    [RelayCommand]
    private void ToggleReadingDirection()
    {
        _settingsService.RightToLeftMangaMode = !_settingsService.RightToLeftMangaMode;
    }

    [RelayCommand]
    private void ToggleVerticalScroll()
    {
        _settingsService.ContinuousVerticalScroll = !_settingsService.ContinuousVerticalScroll;
    }

    [RelayCommand]
    private void CycleVisualFilter()
    {
        _settingsService.VisualFilter = VisualFilter switch
        {
            ReaderVisualFilter.None => ReaderVisualFilter.NightInvert,
            ReaderVisualFilter.NightInvert => ReaderVisualFilter.Sepia,
            _ => ReaderVisualFilter.None
        };
    }

    [RelayCommand]
    private async Task CloseReaderAsync()
    {
        _persistDebounceCts?.Cancel();
        _persistDebounceCts?.Dispose();
        _persistDebounceCts = null;
        await PersistProgressAsync();

        _preloadLoopCts?.Cancel();
        _preloadLoopCts?.Dispose();
        _preloadLoopCts = null;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _pageAspectRatios.Clear();
        _defaultPageAspectRatio = 1.414f;

        _bookSource?.Dispose();
        _bookSource = null;

        if (IsRotationLocked)
        {
            _orientationService.SetRotationLocked(false);
            IsRotationLocked = false;
            OnPropertyChanged(nameof(RotationLockLabel));
        }

        if (CurrentBook != null)
        {
            _pageCacheService.ClearBook(CurrentBook.Id);
        }

        await Shell.Current.GoToAsync("..");
    }

    public async Task OnSliderPageSelectedAsync(int pageNumber1Based)
    {
        if (_suppressSliderSync || TotalPages <= 0) return;
        await GoToPageAsync(pageNumber1Based - 1);
    }

    private async Task LoadBookSourceAsync(BookItem book)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _preloadLoopCts?.Cancel();
        _preloadLoopCts?.Dispose();
        _preloadLoopCts = new CancellationTokenSource();
        var preloadCt = _preloadLoopCts.Token;

        _pageAspectRatios.Clear();
        _defaultPageAspectRatio = 1.414f;

        IsBusy = true;
        try
        {
            _bookSource?.Dispose();
            _bookSource = null;

            if (string.IsNullOrWhiteSpace(book.FilePath))
            {
                return;
            }

            var source = await _bookSourceFactory.CreateAsync(book.FilePath, ct);
            if (ct.IsCancellationRequested)
            {
                source.Dispose();
                return;
            }

            _bookSource = source;
            TotalPages = source.PageCount;
            book.TotalPages = source.PageCount;
            SliderMaximum = Math.Max(2, TotalPages);
            OnPropertyChanged(nameof(CanScrubPages));

            var targetIndex = TotalPages > 0
                ? Math.Clamp(CurrentPageIndex, 0, TotalPages - 1)
                : 0;
            SetPageIndexCore(targetIndex);

            StartPreloadWorker(book, source, preloadCt);
            if (source is ISkiaBookSource skiaSource)
            {
                _ = LoadPdfAspectRatiosAsync(skiaSource, preloadCt);
            }
            RaisePageContentReady();
        }
        catch (OperationCanceledException)
        {
            // Cancelación normal al cambiar de libro o salir del visor
        }
        catch (Exception ex)
        {
            PageIndicator = $"Error al cargar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SignalPreloadWorker()
    {
        try
        {
            if (_preloadSignal.CurrentCount == 0)
            {
                _preloadSignal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Señal ya pendiente para procesar el siguiente lote
        }
    }

    private void StartPreloadWorker(BookItem book, IBookSource source, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var nextBitmapPage = FindNextPageToPreload(book.Id);
                if (nextBitmapPage >= 0)
                {
                    await EnsurePageDecodedAsync(book.Id, source, nextBitmapPage, ct).ConfigureAwait(false);
                    continue;
                }

                if (source is not ISkiaBookSource)
                {
                    var probePage = FindNextSpaceToProbe();
                    if (probePage >= 0)
                    {
                        await ProbePageAspectRatioAsync(source, probePage, ct).ConfigureAwait(false);
                        continue;
                    }
                }

                try
                {
                    await _preloadSignal.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, ct);
    }

    private async Task LoadPdfAspectRatiosAsync(ISkiaBookSource skiaSource, CancellationToken ct)
    {
        try
        {
            var ratios = await skiaSource.GetPageAspectRatiosAsync(ct).ConfigureAwait(false);
            if (ratios == null || ratios.Length == 0 || ct.IsCancellationRequested)
            {
                return;
            }

            double sum = 0;
            var count = 0;
            for (var i = 0; i < ratios.Length; i++)
            {
                var clamped = Math.Clamp(ratios[i], 0.25f, 4.5f);
                _pageAspectRatios[i] = clamped;
                if (count < 24)
                {
                    sum += clamped;
                    count++;
                }
            }

            if (count > 0)
            {
                _defaultPageAspectRatio = (float)(sum / count);
            }

            RaisePageContentReady();
        }
        catch (OperationCanceledException)
        {
            // Cancelado al cerrar o cambiar libro
        }
        catch
        {
            // Si el PDF no entrega métricas rápidas, se usarán los bitmaps al decodificarse
        }
    }

    private int FindNextPageToPreload(string bookId)
    {
        var total = TotalPages;
        if (total <= 0) return -1;

        var center = Math.Clamp(CurrentPageIndex, 0, total - 1);
        var (minKeep, maxKeep) = GetKeepWindowRange(center);
        var dir = _lastNavigationDirection >= 0 ? 1 : -1;

        if (IsCandidateNeeded(bookId, center)) return center;

        if (IsDoublePageActive && center + 1 <= maxKeep && IsCandidateNeeded(bookId, center + 1))
        {
            return center + 1;
        }

        var maxStep = Math.Max(center - minKeep, maxKeep - center);
        for (var step = 1; step <= maxStep; step++)
        {
            var forward = center + (step * dir);
            if (forward >= minKeep && forward <= maxKeep && IsCandidateNeeded(bookId, forward))
            {
                return forward;
            }

            var backward = center - (step * dir);
            if (backward >= minKeep && backward <= maxKeep && IsCandidateNeeded(bookId, backward))
            {
                return backward;
            }
        }

        return -1;
    }

    private int FindNextSpaceToProbe()
    {
        var total = TotalPages;
        if (total <= 0) return -1;

        var center = Math.Clamp(CurrentPageIndex, 0, total - 1);
        var (_, maxKeep) = GetKeepWindowRange(center);
        var start = Math.Min(total - 1, maxKeep + 1);
        var end = Math.Min(total - 1, maxKeep + SpaceProbeLookaheadCount);

        for (var i = start; i <= end; i++)
        {
            if (!_pageAspectRatios.ContainsKey(i))
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsCandidateNeeded(string bookId, int pageIndex)
    {
        if (_pageCacheService.ContainsPage(bookId, pageIndex))
        {
            return false;
        }

        lock (_pageLoadSync)
        {
            return !_inFlightPages.Contains(pageIndex);
        }
    }

    private async Task EnsurePageDecodedAsync(
        string bookId,
        IBookSource source,
        int pageIndex,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested || (uint)pageIndex >= (uint)TotalPages)
        {
            return;
        }

        if (_pageCacheService.ContainsPage(bookId, pageIndex))
        {
            return;
        }

        lock (_pageLoadSync)
        {
            if (!_inFlightPages.Add(pageIndex))
            {
                return;
            }
        }

        try
        {
            var maxDim = GetPlatformMaxDecodeDimensionPx();
            SKBitmap? bitmap = null;

            if (source is ISkiaBookSource skiaSource)
            {
                bitmap = await skiaSource.GetPageBitmapAsync(pageIndex, maxDim, ct).ConfigureAwait(false);
            }
            else
            {
                await using var stream = await source.GetPageStreamAsync(pageIndex, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;
                bitmap = DecodeAndScaleSafe(stream, maxDim);
            }

            if (bitmap == null) return;

            RecordAspectRatio(pageIndex, bitmap.Width, bitmap.Height);
            var (minKeep, maxKeep) = GetKeepWindowRange(CurrentPageIndex);
            _pageCacheService.StoreBitmapInRange(bookId, pageIndex, bitmap, minKeep, maxKeep);

            var preferredWidth = _preferredFitWidthPx;
            if (preferredWidth > 64 && bitmap.Width > preferredWidth * 1.15f)
            {
                var fitted = CreateFittedBitmapSafe(bitmap, preferredWidth);
                if (fitted != null)
                {
                    _pageCacheService.SetFittedBitmap(bookId, pageIndex, fitted, preferredWidth);
                }
            }

            RaisePageContentReady();
        }
        catch (OperationCanceledException)
        {
            // Ignorar cancelación al cerrar o cambiar libro
        }
        catch
        {
            // Ignorar errores transitorios de precarga fuera de pantalla
        }
        finally
        {
            lock (_pageLoadSync)
            {
                _inFlightPages.Remove(pageIndex);
            }
        }
    }

    private async Task ProbePageAspectRatioAsync(IBookSource source, int pageIndex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || (uint)pageIndex >= (uint)TotalPages || _pageAspectRatios.ContainsKey(pageIndex))
        {
            return;
        }

        try
        {
            await using var stream = await source.GetPageStreamAsync(pageIndex, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
            if (stream.CanSeek) stream.Position = 0;

            using var codec = SKCodec.Create(stream);
            if (codec != null)
            {
                var info = codec.Info;
                if (info.Width > 0 && info.Height > 0)
                {
                    RecordAspectRatio(pageIndex, info.Width, info.Height);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignorar al cerrar o cambiar libro
        }
        catch
        {
            // Ignorar fallo puntual en lectura de cabecera
        }
    }

    private void RecordAspectRatio(int pageIndex, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        var ratio = Math.Clamp(height / (float)width, 0.25f, 4.5f);
        _pageAspectRatios[pageIndex] = ratio;

        if (_pageAspectRatios.Count <= 16)
        {
            double sum = 0;
            var count = 0;
            foreach (var entry in _pageAspectRatios)
            {
                sum += entry.Value;
                count++;
            }

            if (count > 0)
            {
                _defaultPageAspectRatio = (float)(sum / count);
            }
        }
    }

    private static bool IsPhoneDevice()
    {
        try
        {
            return DeviceInfo.Current.Idiom == DeviceIdiom.Phone;
        }
        catch
        {
            return true;
        }
    }

    private static int GetPlatformMaxDecodeDimensionPx()
    {
        return IsPhoneDevice()
            ? PhoneMaxDecodeDimensionPx
            : DesktopMaxDecodeDimensionPx;
    }

    private static SKBitmap? DecodeAndScaleSafe(Stream stream, int maxDecodeDimensionPx)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var codec = SKCodec.Create(stream);
        if (codec != null)
        {
            var info = codec.Info;
            var maxSide = Math.Max(info.Width, info.Height);
            if (maxSide > 0)
            {
                var scale = maxSide > maxDecodeDimensionPx
                    ? (float)maxDecodeDimensionPx / maxSide
                    : 1.0f;

                var desired = codec.GetScaledDimensions(scale);
                var decodeInfo = new SKImageInfo(desired.Width, desired.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                var decodedScaled = SKBitmap.Decode(codec, decodeInfo);
                if (decodedScaled != null)
                {
                    return EnsureMaxDimension(decodedScaled, maxDecodeDimensionPx);
                }
            }
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var fallback = SKBitmap.Decode(stream);
        return fallback != null ? EnsureMaxDimension(fallback, maxDecodeDimensionPx) : null;
    }

    private static SKBitmap EnsureMaxDimension(SKBitmap bitmap, int maxDecodeDimensionPx)
    {
        var maxSide = Math.Max(bitmap.Width, bitmap.Height);
        if (maxSide <= maxDecodeDimensionPx)
        {
            return bitmap;
        }

        var scale = maxDecodeDimensionPx / (double)maxSide;
        var targetWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var info = new SKImageInfo(targetWidth, targetHeight, bitmap.ColorType, bitmap.AlphaType);

        var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized != null)
        {
            bitmap.Dispose();
            return resized;
        }

        return bitmap;
    }

    private void ScheduleDebouncedProgressPersist()
    {
        _persistDebounceCts?.Cancel();
        _persistDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _persistDebounceCts = cts;
        var ct = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ProgressPersistDebounce, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    await PersistProgressAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Reemplazado por una navegación más reciente
            }
        }, ct);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SyncFromSettings();
            RaisePageContentReady();
        });
    }

    private void SyncFromSettings()
    {
        IsNightMode = _settingsService.NightReadingModeEnabled;
        VisualFilter = _settingsService.VisualFilter;
        IsRightToLeftManga = _settingsService.RightToLeftMangaMode;
        IsContinuousVertical = _settingsService.ContinuousVerticalScroll;
        IsDoublePageEnabled = _settingsService.DoublePageSpreadEnabled;

        RecalculateDoublePageState();
        OnPropertyChanged(nameof(DirectionModeLabel));
        OnPropertyChanged(nameof(FilterModeLabel));
    }

    private void RecalculateDoublePageState()
    {
        var active = !IsContinuousVertical
            && IsDoublePageEnabled
            && _viewportWidth > 800
            && _viewportWidth > _viewportHeight;

        if (IsDoublePageActive != active)
        {
            IsDoublePageActive = active;
            UpdateIndicator();
        }

        OnPropertyChanged(nameof(LayoutModeLabel));
        SignalPreloadWorker();
        RaisePageContentReady();
    }

    private void SetPageIndexCore(int pageIndex)
    {
        CurrentPageIndex = pageIndex;

        _suppressSliderSync = true;
        try
        {
            CurrentPageNumber = TotalPages > 0 ? pageIndex + 1 : 1;
        }
        finally
        {
            _suppressSliderSync = false;
        }

        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        if (TotalPages <= 0)
        {
            PageIndicator = "Sin páginas disponibles";
            return;
        }

        if (IsDoublePageActive && CurrentPageIndex + 1 < TotalPages)
        {
            PageIndicator = $"Páginas {CurrentPageIndex + 1}-{CurrentPageIndex + 2} / {TotalPages}";
            return;
        }

        PageIndicator = $"Página {CurrentPageIndex + 1} / {TotalPages}";
    }

    private void RaisePageContentReady()
    {
        MainThread.BeginInvokeOnMainThread(() => PageContentReady?.Invoke(this, EventArgs.Empty));
    }

    private Task PersistProgressAsync()
    {
        if (CurrentBook == null) return Task.CompletedTask;
        return _libraryService.UpdateProgressAsync(new ReadingProgress
        {
            BookId = CurrentBook.Id,
            CurrentPage = CurrentPageIndex + 1,
            TotalPages = TotalPages,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }
}
