using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NebulaReader.Models;
using NebulaReader.Services;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel de la pantalla principal de biblioteca.
/// </summary>
public partial class LibraryViewModel : BaseViewModel
{
    private readonly LibrarySelectionState _selection = new();

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    [ObservableProperty]
    public partial int SelectedDocumentCount { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingTags { get; set; }

    [ObservableProperty]
    public partial string TagDownloadProgress { get; set; } = string.Empty;

    public IReadOnlyList<BookItem> GetSelectedBooks() => _selection.ResolveBooks(Books);

    private readonly ILibraryService _libraryService;
    private readonly IFilePickerService _filePickerService;
    private readonly ILibraryBackupService _backupService;
    private readonly IImportedFolderStore _importedFolderStore;
    private readonly LibrarySyncService _librarySyncService;
    private readonly IFullScreenService _fullScreenService;
    private readonly IOnlineTagService _onlineTagService;
    private readonly ITagLocalizationService _tagLocalizationService;
    private CancellationTokenSource? _importCts;
    private int _importRunId;

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportFraction))]
    public partial int ImportProcessed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportFraction))]
    public partial int ImportTotal { get; set; }

    [ObservableProperty]
    public partial string ImportProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingCoverText))]
    public partial int PendingCoverCount { get; set; }

    [ObservableProperty]
    public partial bool HasImportReport { get; set; }

    [ObservableProperty]
    public partial string ImportSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportFailures))]
    public partial ObservableCollection<ImportFailure> ImportFailures { get; set; } = new();

    public double ImportFraction => ImportTotal > 0 ? (double)ImportProcessed / ImportTotal : 0;
    public string PendingCoverText => $"Portadas pendientes: {PendingCoverCount}";
    public bool HasImportFailures => ImportFailures.Count > 0;

    [ObservableProperty]
    public partial ObservableCollection<BookItem> Books { get; set; } = new();

    public ObservableCollection<LibraryDisplayItem> DisplayItems { get; } = new();

    /// <summary>
    /// Indica que no hay elementos visibles. El estado vacío se dibuja dentro de la cabecera de la
    /// colección porque, en Windows, el EmptyView nativo del CollectionView se superpone a la cabecera
    /// y bloquea los clics en sus botones (por ejemplo, «Añadir»).
    /// </summary>
    public bool IsLibraryEmpty => DisplayItems.Count == 0;

    public ObservableCollection<LibraryDisplayRow> DisplayRows { get; } = new();

    [ObservableProperty]
    public partial double CardWidth { get; set; } = 200;

    private int _cardColumns = 1;

    public void UpdateCardColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (_cardColumns == columns) return;
        _cardColumns = columns;
        RebuildDisplayRows();
    }

    private void RebuildDisplayRows() =>
        SyncDisplayRows(DisplayRows, ResponsiveLayout.GroupIntoRows(DisplayItems, _cardColumns));

    public ObservableCollection<LibraryDisplayItem> ContinueReadingItems { get; } = new();

    public ObservableCollection<LibraryDisplayRow> ContinueReadingRows { get; } = new();

    private int _recentCardColumns = 1;

    public void UpdateRecentCardLayout(ResponsiveLayout.CardGridMetrics metrics)
    {
        RecentCardWidth = metrics.CardWidth;
        if (_recentCardColumns == metrics.Columns) return;
        _recentCardColumns = metrics.Columns;
        RebuildContinueReadingRows();
    }

    private void RebuildContinueReadingRows() =>
        SyncDisplayRows(ContinueReadingRows, ResponsiveLayout.GroupIntoRows(ContinueReadingItems, _recentCardColumns));

    private static void SyncDisplayItems(
        ObservableCollection<LibraryDisplayItem> target,
        IReadOnlyList<LibraryDisplayItem> nextItems)
    {
        // Reutilizar las instancias existentes por clave evita recrear contenedores en WinUI
        // cuando las portadas o metadatos terminan de generarse en segundo plano.
        var existingByKey = new Dictionary<string, LibraryDisplayItem>(target.Count, StringComparer.Ordinal);
        foreach (var item in target)
        {
            existingByKey.TryAdd(item.ItemKey, item);
        }

        var resolved = new List<LibraryDisplayItem>(nextItems.Count);
        foreach (var next in nextItems)
        {
            if (existingByKey.TryGetValue(next.ItemKey, out var existing))
            {
                existing.UpdateFrom(next);
                resolved.Add(existing);
            }
            else
            {
                resolved.Add(next);
            }
        }

        for (var i = 0; i < resolved.Count; i++)
        {
            if (i < target.Count)
            {
                if (!ReferenceEquals(target[i], resolved[i]))
                {
                    target[i] = resolved[i];
                }
            }
            else
            {
                target.Add(resolved[i]);
            }
        }

        while (target.Count > resolved.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static void SyncDisplayRows(
        ObservableCollection<LibraryDisplayRow> target,
        IReadOnlyList<IReadOnlyList<LibraryDisplayItem>> nextRows)
    {
        for (var i = 0; i < nextRows.Count; i++)
        {
            if (i < target.Count)
            {
                target[i].SyncItems(nextRows[i]);
            }
            else
            {
                target.Add(new LibraryDisplayRow(nextRows[i]));
            }
        }

        while (target.Count > nextRows.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    public bool HasContinueReading => ContinueReadingItems.Count > 0;
    public bool HasNoContinueReading => !HasContinueReading;

    [ObservableProperty]
    public partial double RecentCardWidth { get; set; } = 282;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseHorizontalBookActions))]
    public partial bool UseStackedBookActions { get; set; }

    public bool UseHorizontalBookActions => !UseStackedBookActions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrarySelected))]
    public partial bool IsHomeSelected { get; set; } = true;

    public bool IsLibrarySelected => !IsHomeSelected;

    partial void OnIsHomeSelectedChanged(bool value) =>
        Title = value ? "Inicio - Nebula Reader" : "Biblioteca - Nebula Reader";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompactFilterPanel))]
    public partial bool IsFilterPanelExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCompactFilterPanel))]
    public partial bool UseCompactFilters { get; set; } = true;

    public bool ShowCompactFilterPanel => UseCompactFilters && IsFilterPanelExpanded;

    public string FilterButtonLabel => ActiveFilterCount > 0
        ? $"Filtrar ({ActiveFilterCount})" : "Filtrar";
    public int ActiveFilterCount => (string.IsNullOrWhiteSpace(SearchText) ? 0 : 1) +
        (SelectedFormatIndex > 0 ? 1 : 0) + (SelectedStatusIndex > 0 ? 1 : 0) + (HasTagFilter ? 1 : 0);
    public string ActiveFiltersSummary
    {
        get
        {
            var criteria = new List<string>();
            if (!string.IsNullOrWhiteSpace(SearchText)) criteria.Add($"Búsqueda: {SearchText.Trim()}");
            if (SelectedFormatIndex > 0 && SelectedFormatIndex < FormatOptions.Count)
                criteria.Add($"Formato: {FormatOptions[SelectedFormatIndex]}");
            if (SelectedStatusIndex > 0 && SelectedStatusIndex < StatusOptions.Count)
                criteria.Add($"Estado: {StatusOptions[SelectedStatusIndex]}");
            if (HasTagFilter) criteria.Add(SelectedTagFilter!.Label);
            return criteria.Count == 0 ? "Sin filtros activos" : $"Filtros activos · {string.Join(" · ", criteria)}";
        }
    }

    [RelayCommand]
    private void ShowHome() => IsHomeSelected = true;

    [RelayCommand]
    private void ShowLibrary() => IsHomeSelected = false;

    [RelayCommand]
    private void ToggleFilterPanel() => IsFilterPanelExpanded = !IsFilterPanelExpanded;

    public IReadOnlyList<string> FormatOptions { get; } = ["Todos los formatos", "CBZ", "CBR", "PDF"];
    public IReadOnlyList<string> StatusOptions { get; } = ["Todos los estados", "No leído", "Leyendo", "Completado"];
    public IReadOnlyList<string> GroupingOptions { get; } = ["Series", "Etiquetas", "Documentos"];
    public IReadOnlyList<TagFilterOption> TagOptions { get; private set; } =
        [LibraryBrowseService.AllTags, LibraryBrowseService.Untagged];

    [ObservableProperty]
    public partial TagFilterOption? SelectedTagFilter { get; set; } = LibraryBrowseService.AllTags;

    [ObservableProperty]
    public partial int SelectedGroupingIndex { get; set; }

    private bool HasTagFilter => SelectedTagFilter?.Tag != null || SelectedTagFilter?.WithoutTags == true;
    private bool _updatingTagOptions;
    private string? _activeGroupKey;
    private const string GroupingPreferenceKey = "library_grouping_mode";

    partial void OnSelectedTagFilterChanged(TagFilterOption? value) => RefreshFilters();

    partial void OnSelectedGroupingIndexChanged(int value)
    {
        AppPreferences.Set(GroupingPreferenceKey, Math.Clamp(value, 0, 2));
        _activeGroupKey = null;
        ActiveGroupTitle = null;
        RefreshFilters();
    }

    private void RefreshTagOptions()
    {
        var selected = SelectedTagFilter ?? LibraryBrowseService.AllTags;
        var options = LibraryBrowseService.GetTagOptions(Books, selected);
        if (TagOptions.SequenceEqual(options)) return;
        _updatingTagOptions = true;
        try
        {
            TagOptions = options;
            OnPropertyChanged(nameof(TagOptions));
            SelectedTagFilter = selected;
        }
        finally { _updatingTagOptions = false; }
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedFormatIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedStatusIndex { get; set; }

    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText) ||
        SelectedFormatIndex > 0 || SelectedStatusIndex > 0 || HasTagFilter;
    public string EmptyTitle => HasActiveFilters ? "No hay resultados" : "Tu biblioteca está vacía";
    public string EmptyMessage => HasActiveFilters
        ? "Prueba otra búsqueda o limpia los filtros para ver tus documentos."
        : "Pulsa Añadir para importar un cómic o PDF, o escanear una carpeta.";
    private int _matchingBookCount;
    private bool _clearingFilters;

    partial void OnSearchTextChanged(string value) => RefreshFilters();
    partial void OnSelectedFormatIndexChanged(int value) => RefreshFilters();
    partial void OnSelectedStatusIndexChanged(int value) => RefreshFilters();

    private void RefreshFilters()
    {
        if (_clearingFilters || _updatingTagOptions) return;
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterButtonLabel));
        OnPropertyChanged(nameof(ActiveFiltersSummary));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _clearingFilters = true;
        SearchText = string.Empty;
        SelectedFormatIndex = 0;
        SelectedStatusIndex = 0;
        SelectedTagFilter = LibraryBrowseService.AllTags;
        _clearingFilters = false;
        RefreshFilters();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInsideGroup))]
    public partial string? ActiveGroupTitle { get; set; }

    public bool IsInsideGroup => !string.IsNullOrWhiteSpace(ActiveGroupTitle);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFullScreen))]
    [NotifyPropertyChangedFor(nameof(FullScreenButtonLabel))]
    public partial bool IsFullScreen { get; set; }

    private double _cardCoverHeight = 200;

    public bool IsNotFullScreen => !IsFullScreen;
    public string FullScreenButtonLabel => IsFullScreen ? "Modo ventana" : "Pantalla completa";

    public void UpdateCardCoverHeight(double height)
    {
        if (Math.Abs(_cardCoverHeight - height) < 1.0)
        {
            return;
        }

        _cardCoverHeight = height;
        foreach (var item in DisplayItems)
        {
            item.CoverHeight = height;
        }
    }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Biblioteca lista. Pulsa Añadir para incorporar documentos.";

    public LibraryViewModel(
        ILibraryService libraryService,
        IFilePickerService filePickerService,
        ILibraryBackupService backupService,
        IImportedFolderStore importedFolderStore,
        LibrarySyncService librarySyncService,
        IFullScreenService fullScreenService,
        IOnlineTagService onlineTagService, ITagLocalizationService tagLocalizationService)
    {
        _onlineTagService = onlineTagService;
        _tagLocalizationService = tagLocalizationService;
        _libraryService = libraryService;
        _filePickerService = filePickerService;
        _backupService = backupService;
        _importedFolderStore = importedFolderStore;
        _librarySyncService = librarySyncService;
        _fullScreenService = fullScreenService;
        _libraryService.BookUpdated += OnBookUpdated;
        _libraryService.PendingCoverCountChanged += OnPendingCoverCountChanged;
        PendingCoverCount = _libraryService.PendingCoverCount;
        _fullScreenService.FullScreenChanged += OnFullScreenChanged;
        IsFullScreen = _fullScreenService.IsFullScreen;
        Title = "Inicio - Nebula Reader";
        SelectedGroupingIndex = Math.Clamp(AppPreferences.Get(GroupingPreferenceKey, 0), 0, 2);
    }

    private void OnFullScreenChanged(object? sender, bool enabled)
    {
        if (AppLifetime.IsShuttingDown) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!AppLifetime.IsShuttingDown) IsFullScreen = enabled;
        });
    }

    private void OnPendingCoverCountChanged(object? sender, int count)
    {
        if (AppLifetime.IsShuttingDown) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!AppLifetime.IsShuttingDown) PendingCoverCount = count;
        });
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        _fullScreenService.ToggleFullScreen();
    }

    private void OnBookUpdated(object? sender, BookItem updated)
    {
        if (AppLifetime.IsShuttingDown) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // La ventana pudo cerrarse entre el encolado y la ejecución de este callback.
            if (AppLifetime.IsShuttingDown) return;
            var existing = Books.FirstOrDefault(b => b.Id == updated.Id);
            if (existing != null)
            {
                if (!ReferenceEquals(existing, updated))
                {
                    existing.CoverThumbnailPath = updated.CoverThumbnailPath;
                    existing.TotalPages = updated.TotalPages;
                    existing.CurrentPage = updated.CurrentPage;
                    existing.Status = updated.Status;
                    existing.LastReadAtUtc = updated.LastReadAtUtc;
                    existing.SeriesName = updated.SeriesName;
                    existing.Tags = updated.Tags;
                    existing.MetadataExtracted = updated.MetadataExtracted;
                    existing.Title = updated.Title;
                    existing.FilePath = updated.FilePath;
                    existing.OriginalFilePath = updated.OriginalFilePath;
                    existing.CoverError = updated.CoverError;
                    existing.TitleEditedManually = updated.TitleEditedManually;
                    existing.SeriesEditedManually = updated.SeriesEditedManually;
                    existing.TagsEditedManually = updated.TagsEditedManually;
                }

                RebuildDisplayItems();
                UpdateStatusSummary();
            }
        });
    }

    public void PrepareForShutdown()
    {
        _importCts?.Cancel();
        _libraryService.BookUpdated -= OnBookUpdated;
        _libraryService.PendingCoverCountChanged -= OnPendingCoverCountChanged;
        _fullScreenService.FullScreenChanged -= OnFullScreenChanged;
    }

    [RelayCommand]
    private async Task LoadLibraryAsync()
    {
        if (IsBusy || AppLifetime.IsShuttingDown) return;
        IsBusy = true;
        try
        {
            var items = await _libraryService.GetBooksAsync();
            if (AppLifetime.IsShuttingDown) return;
            Books.Clear();
            foreach (var item in items)
            {
                Books.Add(item);
            }

            RebuildDisplayItems();
            UpdateStatusSummary();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar biblioteca: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SynchronizeLibraryAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsImporting = true;
        HasImportReport = true;
        ImportSummary = string.Empty;
        ImportProcessed = 0;
        ImportTotal = 0;
        ImportProgressText = "Revisando carpetas guardadas...";
        _importCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<FolderImportProgress>(p =>
            {
                ImportProcessed = p.Processed;
                ImportTotal = p.Total;
                ImportProgressText = $"Importando nuevos: {p.Processed}/{p.Total} · fallidos {p.Failed}";
            });
            var result = await _librarySyncService.RunAsync(progress, _importCts.Token);
            await RefreshBooksAsync();
            ImportSummary = result.Cancelled
                ? "Revisión cancelada."
                : $"Biblioteca revisada: {result.Added} nuevos, {result.Removed} quitados, {result.Unchanged} sin cambios.";
            if (result.UnavailableFolders > 0)
                ImportSummary += $" {result.UnavailableFolders} carpeta(s) no disponibles; sus documentos se conservaron.";
            if (result.Failed > 0)
                ImportSummary += $" {result.Failed} archivo(s) no se pudieron importar.";
            UpdateStatusSummary();
        }
        catch (Exception ex)
        {
            ImportSummary = $"No se pudo revisar la biblioteca: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
            _importCts.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var filePath = await _filePickerService.PickBookFileAsync();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                StatusMessage = "Selección cancelada.";
                return;
            }

            var book = await _libraryService.ImportFileAsync(filePath);
            UpsertBookInCollection(book);
            RebuildDisplayItems();

            StatusMessage = $"Importado: {book.Title}";
            await Shell.Current.GoToAsync("ReaderPage", new Dictionary<string, object>
            {
                { "Book", book }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al abrir archivo: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var folderPathOrUri = await _filePickerService.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(folderPathOrUri))
            {
                StatusMessage = "Selección de carpeta cancelada.";
                return;
            }

            var result = await RunImportAsync((progress, ct) => _libraryService.ImportFolderAsync(folderPathOrUri, progress, ct));
            if (!result.Cancelled) _importedFolderStore.Add(folderPathOrUri);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al escanear carpeta: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _importCts?.Cancel();
        ImportProgressText = "Cancelando el escaneo...";
    }

    [RelayCommand]
    private async Task RetryFailedImportsAsync()
    {
        if (IsBusy || ImportFailures.Count == 0) return;
        IsBusy = true;
        try
        {
            var paths = ImportFailures.Select(f => f.FilePath).ToArray();
            await RunImportAsync((progress, ct) => _libraryService.RetryImportFailuresAsync(paths, progress, ct));
        }
        catch (Exception ex) { StatusMessage = $"Error al reintentar: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task<FolderImportResult> RunImportAsync(Func<IProgress<FolderImportProgress>, CancellationToken, Task<FolderImportResult>> run)
    {
        _importCts = new CancellationTokenSource();
        var runId = ++_importRunId;
        IsImporting = true;
        HasImportReport = true;
        ImportProcessed = 0;
        ImportTotal = 0;
        ImportFailures = new ObservableCollection<ImportFailure>();
        ImportSummary = string.Empty;
        ImportProgressText = "Buscando archivos compatibles...";
        try
        {
            var progress = new Progress<FolderImportProgress>(p =>
            {
                if (runId != _importRunId) return;
                ImportProcessed = p.Processed;
                ImportTotal = p.Total;
                PendingCoverCount = p.PendingCovers;
                ImportProgressText = $"Indexación: {p.Processed}/{p.Total} · añadidos {p.Added}, existentes {p.Existing}, fallidos {p.Failed}";
            });
            var result = await run(progress, _importCts.Token);
            foreach (var failure in result.Failures) ImportFailures.Add(failure);
            OnPropertyChanged(nameof(HasImportFailures));
            await RefreshBooksAsync();
            ImportProcessed = result.Processed;
            ImportTotal = result.Total;
            ImportSummary = $"{(result.Cancelled ? "Cancelado" : "Terminado")}: {result.Added} añadidos, {result.Existing} existentes, {result.Failed} fallidos.";
            StatusMessage = ImportSummary;
            return result;
        }
        finally
        {
            IsImporting = false;
            _importCts.Dispose();
            _importCts = null;
            _importRunId++;
            PendingCoverCount = _libraryService.PendingCoverCount;
        }
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var output = new MemoryStream();
            var count = await _backupService.ExportAsync(output);
            var name = $"NebulaReader-respaldo-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            StatusMessage = await _filePickerService.SaveBackupAsync(output, name)
                ? $"Respaldo guardado: {count} documento(s). No incluye los archivos originales."
                : "Exportación cancelada.";
        }
        catch (Exception ex) { StatusMessage = $"Error al exportar respaldo: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await using var input = await _filePickerService.PickBackupAsync();
            if (input == null) { StatusMessage = "Importación de respaldo cancelada."; return; }
            var result = await _backupService.ImportAsync(input);
            await RefreshBooksAsync();
            StatusMessage = $"Respaldo importado: {result.Added} nuevos, {result.Merged} combinados. Los nombres coincidentes se asociaron; revisa los pendientes desde Acciones.";
        }
        catch (Exception ex) { StatusMessage = $"No se importó el respaldo: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task RefreshBooksAsync()
    {
        if (AppLifetime.IsShuttingDown) return;
        var items = await _libraryService.GetBooksAsync();
        if (AppLifetime.IsShuttingDown) return;
        Books.Clear();
        foreach (var item in items) Books.Add(item);
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    [RelayCommand]
    private async Task OpenItemAsync(LibraryDisplayItem? item)
    {
        if (item == null) return;

        if (IsSelectionMode)
        {
            ToggleSelection(item);
            return;
        }

        if (item.IsGroup)
        {
            _activeGroupKey = item.GroupKey ?? item.Title;
            ActiveGroupTitle = item.Title;
            RebuildDisplayItems();
            UpdateStatusSummary();
            return;
        }

        if (item.Book != null)
        {
            await OpenBookAsync(item.Book);
        }
    }

    public void ToggleSelection(LibraryDisplayItem item)
    {
        _selection.Toggle(item);
        RefreshSelectionState();
    }

    public void AddToSelection(LibraryDisplayItem item)
    {
        _selection.Add(item);
        RefreshSelectionState();
    }

    public void SelectRange(int firstIndex, int lastIndex)
    {
        _selection.SelectRange(DisplayItems, firstIndex, lastIndex);
        RefreshSelectionState();
    }

    public void SelectAllVisible()
    {
        _selection.SelectAll(DisplayItems);
        RefreshSelectionState();
    }

    public void ClearSelection()
    {
        _selection.Clear();
        RefreshSelectionState();
    }

    public async Task SetSelectedStatusAsync(ReadingStatus status)
    {
        var ids = _selection.SelectedIds;
        if (status == ReadingStatus.Completed && Books.Any(book => ids.Contains(book.Id) && book.TotalPages <= 0))
            throw new InvalidOperationException("Hay documentos sin un total de páginas conocido.");
        foreach (var id in ids)
            UpsertBookInCollection(await _libraryService.SetReadingStatusAsync(id, status));
        ClearSelection();
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    public async Task RemoveSelectedAsync()
    {
        var ids = _selection.SelectedIds;
        foreach (var id in ids)
            await _libraryService.RemoveBookAsync(id);
        foreach (var book in Books.Where(book => ids.Contains(book.Id)).ToArray()) Books.Remove(book);
        ClearSelection();
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    private void RefreshSelectionState()
    {
        _selection.RetainBooks(Books);
        SelectedDocumentCount = _selection.Count;
        IsSelectionMode = SelectedDocumentCount > 0;
        foreach (var item in DisplayItems)
        {
            item.IsSelectionMode = IsSelectionMode;
            item.IsSelected = _selection.ContainsAll(item);
            item.IsPartiallySelected = !item.IsSelected && _selection.ContainsAny(item);
        }
    }

    [RelayCommand]
    private void ExitGroup()
    {
        if (!IsInsideGroup) return;

        _activeGroupKey = null;
        ActiveGroupTitle = null;
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    [RelayCommand]
    private async Task OpenBookAsync(BookItem? book)
    {
        if (book == null) return;
        var access = await _libraryService.CheckAccessAsync(book);
        if (access != BookAccessState.Available)
        {
            StatusMessage = access switch
            {
                BookAccessState.Missing => "Archivo ausente. Usa Volver a localizar.",
                BookAccessState.PermissionDenied => "Permiso perdido. Usa Conceder acceso.",
                _ => "Documento dañado o ilegible. Comprueba el archivo y vuelve a localizarlo."
            };
            try { await RecoverBookAsync(book); }
            catch (Exception ex) { StatusMessage = $"No se pudo recuperar el documento: {ex.Message}"; }
            return;
        }
        await Shell.Current.GoToAsync("ReaderPage", new Dictionary<string, object>
        {
            { "Book", book }
        });
    }

    public async Task RemoveBookAsync(BookItem book)
    {
        await _libraryService.RemoveBookAsync(book.Id);
        Books.Remove(book);
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    public async Task SetStatusAsync(BookItem book, ReadingStatus status)
    {
        UpsertBookInCollection(await _libraryService.SetReadingStatusAsync(book.Id, status));
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    public async Task EditDetailsAsync(BookItem book, string title, string? series)
    {
        var updated = await _libraryService.EditDetailsAsync(book.Id, title, series);
        UpsertBookInCollection(updated);
        UpdateActiveGroupAfterRename(updated);
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    private void UpdateActiveGroupAfterRename(BookItem updatedBook)
    {
        if (!IsInsideGroup || SelectedGroupingIndex != (int)LibraryGrouping.Series) return;
        var currentGroup = LibraryBrowseService.FilterGroups(Books, grouping: LibraryGrouping.Series)
            .FirstOrDefault(bucket => bucket.IsGroup && bucket.Items.Any(item => item.Id == updatedBook.Id));
        if (currentGroup != null)
        {
            _activeGroupKey = currentGroup.Key;
            ActiveGroupTitle = currentGroup.DisplayName;
        }
    }

    public async Task EditGroupSeriesAsync(LibraryDisplayItem item, string? series)
    {
        if (!item.CanEditSeriesGroup || SelectedGroupingIndex != (int)LibraryGrouping.Series)
            throw new InvalidOperationException("Selecciona un grupo de serie.");
        var group = LibraryBrowseService.FilterGroups(Books, grouping: LibraryGrouping.Series)
            .FirstOrDefault(bucket => bucket.IsGroup &&
                string.Equals(bucket.Key, item.GroupKey, StringComparison.OrdinalIgnoreCase));
        if (group == null) throw new InvalidOperationException("El grupo ya no está en la biblioteca.");
        await _libraryService.UpdateSeriesAsync(group.Items.Select(book => book.Id).ToArray(), series);
        await RefreshBooksAsync();
        StatusMessage = $"Serie actualizada en {group.Items.Count} documento(s).";
    }

    public async Task EditTagsAsync(BookItem book, string? tags)
    {
        UpsertBookInCollection(await _libraryService.EditTagsAsync(book.Id, tags));
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    public Task<IReadOnlyList<OnlineTagSource>> GetAvailableTagSourcesAsync(CancellationToken ct = default) =>
        _onlineTagService.GetAvailableSourcesAsync(ct);

    public Task<IReadOnlyList<OnlineTagSuggestion>> SearchTagsAsync(BookItem book, OnlineTagSource source, CancellationToken ct = default) =>
        _onlineTagService.SearchAsync(book, source, ct);

    public Task<IReadOnlyList<string>> LocalizeTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) =>
        _tagLocalizationService.LocalizeAsync(tags, ct);

    public async Task RecoverBookAsync(BookItem book)
    {
        var path = await _filePickerService.PickBookFileAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        UpsertBookInCollection(await _libraryService.RelocateBookAsync(book.Id, path));
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    public async Task RetryCoverAsync(BookItem book)
    {
        await _libraryService.RetryCoverAsync(book.Id);
        StatusMessage = $"Reintentando portada de {book.Title}.";
    }

    [RelayCommand]
    private Task OpenSettingsAsync()
    {
        return Shell.Current.GoToAsync("SettingsPage");
    }

    private void RebuildDisplayItems()
    {
        // Reemplazar ItemsSource provoca que ItemsViewHandler.CleanUpCollectionViewSource
        // desconecte LayoutPanel nativos activos en WinUI (0xC0000005 en LayoutHandler.DisconnectHandler).
        if (AppLifetime.IsShuttingDown) return;
        RefreshTagOptions();
        var nextContinueReading = LibraryBrowseService.GetContinueReading(Books)
            .Select(book => LibraryDisplayItem.FromSingleBook(book))
            .ToList();
        SyncDisplayItems(ContinueReadingItems, nextContinueReading);
        RebuildContinueReadingRows();
        OnPropertyChanged(nameof(HasContinueReading));
        OnPropertyChanged(nameof(HasNoContinueReading));

        var result = LibraryBrowseService.Browse(Books, _activeGroupKey, SearchText,
            SelectedFormatIndex is >= 1 and <= 3 ? (BookFormat)SelectedFormatIndex : null,
            SelectedStatusIndex is >= 1 and <= 3 ? (ReadingStatus)(SelectedStatusIndex - 1) : null,
            SelectedTagFilter?.Tag, SelectedTagFilter?.WithoutTags == true,
            (LibraryGrouping)Math.Clamp(SelectedGroupingIndex, 0, 2));
        var buckets = result.Groups;
        if (_activeGroupKey != null && result.ActiveGroupKey == null)
        {
            _activeGroupKey = null;
            ActiveGroupTitle = null;
        }
        _matchingBookCount = buckets.SelectMany(bucket => bucket.Items).DistinctBy(book => book.Id).Count();

        var nextItems = new List<LibraryDisplayItem>();

        if (IsInsideGroup)
        {
            var matchedGroup = buckets.FirstOrDefault(bucket =>
                bucket.IsGroup &&
                string.Equals(bucket.Key, _activeGroupKey, StringComparison.OrdinalIgnoreCase));

            if (matchedGroup != null)
            {
                foreach (var book in matchedGroup.Items)
                {
                    var bookTags = string.IsNullOrWhiteSpace(book.Tags)
                        ? Array.Empty<string>()
                        : book.Tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    nextItems.Add(LibraryDisplayItem.FromSingleBook(book, bookTags, _cardCoverHeight));
                }

                ApplyDisplayItems(nextItems);
                return;
            }

            // Mantener la navegación si una búsqueda deja esta serie sin coincidencias.
            ApplyDisplayItems(nextItems);
            return;
        }

        foreach (var bucket in buckets)
        {
            nextItems.Add(bucket.IsGroup
                ? LibraryDisplayItem.FromSeriesGroup(bucket.DisplayName, bucket.Items, bucket.CommonTags, _cardCoverHeight,
                    bucket.Key, SelectedGroupingIndex == 1 ? (bucket.Key == "untagged:" ? "Sin clasificar" : "Etiqueta") : "Saga")
                : LibraryDisplayItem.FromSingleBook(bucket.Items[0], bucket.CommonTags, _cardCoverHeight));
        }

        ApplyDisplayItems(nextItems);
    }

    private void ApplyDisplayItems(IReadOnlyList<LibraryDisplayItem> nextItems)
    {
        SyncDisplayItems(DisplayItems, nextItems);
        RebuildDisplayRows();
        OnPropertyChanged(nameof(IsLibraryEmpty));
        RefreshSelectionState();
    }

    private void UpdateStatusSummary()
    {
        if (HasActiveFilters)
        {
            StatusMessage = IsInsideGroup
                ? $"{ActiveGroupTitle}: {DisplayItems.Count} documento(s) coinciden."
                : $"{_matchingBookCount} de {Books.Count} documento(s) coinciden.";
            return;
        }
        if (IsInsideGroup)
        {
            StatusMessage = $"{ActiveGroupTitle}: {DisplayItems.Count} documento(s).";
            return;
        }

        var groupCount = DisplayItems.Count(d => d.IsGroup);
        StatusMessage = groupCount > 0
            ? $"{Books.Count} documento(s) en biblioteca ({groupCount} {(SelectedGroupingIndex == 1 ? "grupo(s) por etiquetas" : "serie(s) agrupadas")})."
            : $"{Books.Count} documento(s) en biblioteca.";
    }

    private void UpsertBookInCollection(BookItem book)
    {
        var existingIndex = -1;
        for (var i = 0; i < Books.Count; i++)
        {
            if (Books[i].Id == book.Id)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            Books.RemoveAt(existingIndex);
        }

        var insertIndex = 0;
        while (insertIndex < Books.Count && CompareBooksAlphabetically(Books[insertIndex], book) <= 0)
        {
            insertIndex++;
        }

        Books.Insert(insertIndex, book);
    }

    private static int CompareBooksAlphabetically(BookItem left, BookItem right)
    {
        var titleCmp = NaturalSortComparer.Instance.Compare(left.Title, right.Title);
        return titleCmp != 0
            ? titleCmp
            : NaturalSortComparer.Instance.Compare(left.FilePath, right.FilePath);
    }
}
