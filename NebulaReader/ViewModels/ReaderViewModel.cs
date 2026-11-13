using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NebulaReader.Models;
using NebulaReader.Services;
using SkiaSharp;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel del visor de lectura (CBR, CBZ y PDF) con caché LRU de 3 páginas y soporte de modos de lectura.
/// </summary>
public partial class ReaderViewModel : BaseViewModel, IQueryAttributable
{
    private const int PhoneMaxDecodeDimensionPx = 2400;
    private const int DesktopMaxDecodeDimensionPx = 3600;
    private static readonly TimeSpan ProgressPersistDebounce = TimeSpan.FromMilliseconds(750);

    private readonly ILibraryService _libraryService;
    private readonly IPageCacheService _pageCacheService;
    private readonly IBookSourceFactory _bookSourceFactory;
    private readonly IReaderSettingsService _settingsService;
    private readonly IOrientationService _orientationService;

    private IBookSource? _bookSource;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _windowPreloadCts;
    private CancellationTokenSource? _persistDebounceCts;
    private readonly object _pageLoadSync = new();
    private readonly HashSet<int> _inFlightPages = new();
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

    public bool TryDrawCachedPage(int pageIndex, Action<SKBitmap> drawAction)
    {
        if (CurrentBook == null || (uint)pageIndex >= (uint)TotalPages)
        {
            return false;
        }

        return _pageCacheService.TryDrawBitmap(CurrentBook.Id, pageIndex, drawAction);
    }

    public void RequestPageLoadIfNeeded(int pageIndex)
    {
        var book = CurrentBook;
        var source = _bookSource;
        if (book == null || source == null || (uint)pageIndex >= (uint)TotalPages)
        {
            return;
        }

        var radius = GetCacheWindowRadius();
        if (Math.Abs(pageIndex - CurrentPageIndex) > radius)
        {
            return;
        }

        if (_pageCacheService.ContainsPage(book.Id, pageIndex))
        {
            return;
        }

        _ = EnsurePageDecodedAsync(book.Id, source, pageIndex, radius, CancellationToken.None);
    }

    private int GetCacheWindowRadius() => IsDoublePageActive ? 2 : 1;

    public async Task GoToPageAsync(int pageIndex)
    {
        if (CurrentBook == null || TotalPages <= 0) return;

        var clamped = Math.Clamp(pageIndex, 0, TotalPages - 1);
        if (clamped == CurrentPageIndex && _pageCacheService.ContainsPage(CurrentBook.Id, clamped))
        {
            return;
        }

        _lastNavigationDirection = clamped >= CurrentPageIndex ? 1 : -1;
        SetPageIndexCore(clamped);
        _pageCacheService.EvictOutsideWindow(CurrentBook.Id, CurrentPageIndex, GetCacheWindowRadius());
        RaisePageContentReady();
        PreloadSlidingWindow();
        ScheduleDebouncedProgressPersist();
        await Task.CompletedTask;
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

        _windowPreloadCts?.Cancel();
        _windowPreloadCts?.Dispose();
        _windowPreloadCts = null;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

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

            PreloadSlidingWindow();
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

    private void PreloadSlidingWindow()
    {
        var book = CurrentBook;
        var source = _bookSource;
        if (book == null || source == null || TotalPages <= 0)
        {
            return;
        }

        _windowPreloadCts?.Cancel();
        _windowPreloadCts?.Dispose();
        _windowPreloadCts = new CancellationTokenSource();
        var ct = _windowPreloadCts.Token;

        // Ventana deslizante direccional: prioriza la página visible y luego el sentido de avance
        var center = CurrentPageIndex;
        var radius = GetCacheWindowRadius();
        var targets = new List<int>(4) { center };

        if (IsDoublePageActive)
        {
            if (center + 1 < TotalPages) targets.Add(center + 1);
            if (_lastNavigationDirection >= 0)
            {
                if (center + 2 < TotalPages) targets.Add(center + 2);
                if (center - 1 >= 0) targets.Add(center - 1);
            }
            else
            {
                if (center - 2 >= 0) targets.Add(center - 2);
                if (center - 1 >= 0) targets.Add(center - 1);
            }
        }
        else if (_lastNavigationDirection >= 0)
        {
            if (center + 1 < TotalPages) targets.Add(center + 1);
            if (center - 1 >= 0) targets.Add(center - 1);
        }
        else
        {
            if (center - 1 >= 0) targets.Add(center - 1);
            if (center + 1 < TotalPages) targets.Add(center + 1);
        }

        _ = Task.Run(async () =>
        {
            foreach (var pageIdx in targets)
            {
                if (ct.IsCancellationRequested) return;
                await EnsurePageDecodedAsync(book.Id, source, pageIdx, radius, ct).ConfigureAwait(false);
            }
        }, ct);
    }

    private async Task EnsurePageDecodedAsync(
        string bookId,
        IBookSource source,
        int pageIndex,
        int cacheRadius,
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

            _pageCacheService.StoreBitmap(bookId, pageIndex, bitmap, CurrentPageIndex, radius: cacheRadius);
            RaisePageContentReady();
        }
        catch (OperationCanceledException)
        {
            // Ignorar cancelación de precarga cuando el usuario avanza rápido
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

    private static int GetPlatformMaxDecodeDimensionPx()
    {
        try
        {
            return DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                ? PhoneMaxDecodeDimensionPx
                : DesktopMaxDecodeDimensionPx;
        }
        catch
        {
            return PhoneMaxDecodeDimensionPx;
        }
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
