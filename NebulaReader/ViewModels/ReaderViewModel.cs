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
    private const int MaxDecodeDimensionPx = 3200;

    private readonly ILibraryService _libraryService;
    private readonly IPageCacheService _pageCacheService;
    private readonly IBookSourceFactory _bookSourceFactory;
    private readonly IReaderSettingsService _settingsService;
    private readonly IOrientationService _orientationService;

    private IBookSource? _bookSource;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _windowPreloadCts;
    private readonly object _pageLoadSync = new();
    private readonly HashSet<int> _inFlightPages = new();
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
        ? "Modo: Vertical"
        : (IsDoublePageActive ? "Modo: Doble Pág." : "Modo: Paginado");
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

        if (Math.Abs(pageIndex - CurrentPageIndex) > 1)
        {
            return;
        }

        if (_pageCacheService.ContainsPage(book.Id, pageIndex))
        {
            return;
        }

        _ = EnsurePageDecodedAsync(book.Id, source, pageIndex, CancellationToken.None);
    }

    public async Task GoToPageAsync(int pageIndex)
    {
        if (CurrentBook == null || TotalPages <= 0) return;

        var clamped = Math.Clamp(pageIndex, 0, TotalPages - 1);
        if (clamped == CurrentPageIndex && _pageCacheService.ContainsPage(CurrentBook.Id, clamped))
        {
            return;
        }

        SetPageIndexCore(clamped);
        _pageCacheService.EvictOutsideWindow(CurrentBook.Id, CurrentPageIndex, radius: 1);
        RaisePageContentReady();
        PreloadSlidingWindow();
        await PersistProgressAsync();
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
        var center = CurrentPageIndex;

        // Ventana deslizante de 3 páginas: [actual, actual + 1, actual - 1]
        _ = Task.Run(async () =>
        {
            await EnsurePageDecodedAsync(book.Id, source, center, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            if (center + 1 < TotalPages)
            {
                await EnsurePageDecodedAsync(book.Id, source, center + 1, ct).ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested) return;

            if (center - 1 >= 0)
            {
                await EnsurePageDecodedAsync(book.Id, source, center - 1, ct).ConfigureAwait(false);
            }
        }, ct);
    }

    private async Task EnsurePageDecodedAsync(string bookId, IBookSource source, int pageIndex, CancellationToken ct)
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
            await using var stream = await source.GetPageStreamAsync(pageIndex, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            var bitmap = DecodeAndScaleSafe(stream);
            if (bitmap == null) return;

            _pageCacheService.StoreBitmap(bookId, pageIndex, bitmap, CurrentPageIndex, radius: 1);
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

    private static SKBitmap? DecodeAndScaleSafe(Stream stream)
    {
        var decoded = SKBitmap.Decode(stream);
        if (decoded == null) return null;

        var maxSide = Math.Max(decoded.Width, decoded.Height);
        if (maxSide <= MaxDecodeDimensionPx)
        {
            return decoded;
        }

        var scale = MaxDecodeDimensionPx / (double)maxSide;
        var targetWidth = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        var info = new SKImageInfo(targetWidth, targetHeight, decoded.ColorType, decoded.AlphaType);

        var resized = decoded.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized != null)
        {
            decoded.Dispose();
            return resized;
        }

        return decoded;
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
