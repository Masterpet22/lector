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
    private readonly ILibraryService _libraryService;
    private readonly IFilePickerService _filePickerService;
    private readonly ILibraryBackupService _backupService;
    private readonly IFullScreenService _fullScreenService;
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

    [ObservableProperty]
    public partial ObservableCollection<LibraryDisplayItem> DisplayItems { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContinueReading))]
    public partial ObservableCollection<LibraryDisplayItem> ContinueReadingItems { get; set; } = new();

    public bool HasContinueReading => ContinueReadingItems.Count > 0;
    public IReadOnlyList<string> FormatOptions { get; } = ["Todos los formatos", "CBZ", "CBR", "PDF"];
    public IReadOnlyList<string> StatusOptions { get; } = ["Todos los estados", "No leído", "Leyendo", "Completado"];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedFormatIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedStatusIndex { get; set; }

    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText) ||
        SelectedFormatIndex > 0 || SelectedStatusIndex > 0;
    public string EmptyTitle => HasActiveFilters ? "No hay resultados" : "Tu biblioteca está vacía";
    public string EmptyMessage => HasActiveFilters
        ? "Prueba otra búsqueda o limpia los filtros para ver tus documentos."
        : "Pulsa 'Abrir archivo' o 'Escanear carpeta' para importar cómics o PDF.";
    private int _matchingBookCount;
    private bool _clearingFilters;

    partial void OnSearchTextChanged(string value) => RefreshFilters();
    partial void OnSelectedFormatIndexChanged(int value) => RefreshFilters();
    partial void OnSelectedStatusIndexChanged(int value) => RefreshFilters();

    private void RefreshFilters()
    {
        if (_clearingFilters) return;
        OnPropertyChanged(nameof(HasActiveFilters));
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
    public partial string StatusMessage { get; set; } = "Biblioteca lista. Abre un archivo o escanea una carpeta.";

    public LibraryViewModel(
        ILibraryService libraryService,
        IFilePickerService filePickerService,
        ILibraryBackupService backupService,
        IFullScreenService fullScreenService)
    {
        _libraryService = libraryService;
        _filePickerService = filePickerService;
        _backupService = backupService;
        _fullScreenService = fullScreenService;
        _libraryService.BookUpdated += OnBookUpdated;
        _libraryService.PendingCoverCountChanged += OnPendingCoverCountChanged;
        PendingCoverCount = _libraryService.PendingCoverCount;
        _fullScreenService.FullScreenChanged += OnFullScreenChanged;
        IsFullScreen = _fullScreenService.IsFullScreen;
        Title = "Biblioteca - Nebula Reader";
    }

    private void OnFullScreenChanged(object? sender, bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() => IsFullScreen = enabled);
    }

    private void OnPendingCoverCountChanged(object? sender, int count) =>
        MainThread.BeginInvokeOnMainThread(() => PendingCoverCount = count);

    [RelayCommand]
    private void ToggleFullScreen()
    {
        _fullScreenService.ToggleFullScreen();
    }

    private void OnBookUpdated(object? sender, BookItem updated)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
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
                }

                RebuildDisplayItems();
                UpdateStatusSummary();
            }
        });
    }

    [RelayCommand]
    private async Task LoadLibraryAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var items = await _libraryService.GetBooksAsync();
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

            await RunImportAsync((progress, ct) => _libraryService.ImportFolderAsync(folderPathOrUri, progress, ct));
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

    private async Task RunImportAsync(Func<IProgress<FolderImportProgress>, CancellationToken, Task<FolderImportResult>> run)
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
            StatusMessage = $"Respaldo importado: {result.Added} nuevos, {result.Merged} combinados. Asocia los archivos pendientes desde Acciones.";
        }
        catch (Exception ex) { StatusMessage = $"No se importó el respaldo: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task RefreshBooksAsync()
    {
        var items = await _libraryService.GetBooksAsync();
        Books.Clear();
        foreach (var item in items) Books.Add(item);
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

    [RelayCommand]
    private async Task OpenItemAsync(LibraryDisplayItem? item)
    {
        if (item == null) return;

        if (item.IsGroup)
        {
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

    [RelayCommand]
    private void ExitGroup()
    {
        if (!IsInsideGroup) return;

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
        UpsertBookInCollection(await _libraryService.EditDetailsAsync(book.Id, title, series));
        RebuildDisplayItems();
        UpdateStatusSummary();
    }

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
        ContinueReadingItems = new ObservableCollection<LibraryDisplayItem>(
            LibraryBrowseService.GetContinueReading(Books)
                .Select(book => LibraryDisplayItem.FromSingleBook(book)));

        var buckets = LibraryBrowseService.FilterGroups(Books, SearchText,
            SelectedFormatIndex is >= 1 and <= 3 ? (BookFormat)SelectedFormatIndex : null,
            SelectedStatusIndex is >= 1 and <= 3 ? (ReadingStatus)(SelectedStatusIndex - 1) : null);
        _matchingBookCount = buckets.Sum(bucket => bucket.Items.Count);

        var nextItems = new List<LibraryDisplayItem>();

        if (IsInsideGroup)
        {
            var matchedGroup = buckets.FirstOrDefault(bucket =>
                bucket.IsGroup &&
                string.Equals(bucket.DisplayName, ActiveGroupTitle, StringComparison.OrdinalIgnoreCase));

            if (matchedGroup != null)
            {
                foreach (var book in matchedGroup.Items)
                {
                    var bookTags = string.IsNullOrWhiteSpace(book.Tags)
                        ? Array.Empty<string>()
                        : book.Tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    nextItems.Add(LibraryDisplayItem.FromSingleBook(book, bookTags, _cardCoverHeight));
                }

                DisplayItems = new ObservableCollection<LibraryDisplayItem>(nextItems);
                return;
            }

            // Mantener la navegación si una búsqueda deja esta serie sin coincidencias.
            DisplayItems = new ObservableCollection<LibraryDisplayItem>();
            return;
        }

        foreach (var bucket in buckets)
        {
            nextItems.Add(bucket.IsGroup
                ? LibraryDisplayItem.FromSeriesGroup(bucket.DisplayName, bucket.Items, bucket.CommonTags, _cardCoverHeight)
                : LibraryDisplayItem.FromSingleBook(bucket.Items[0], bucket.CommonTags, _cardCoverHeight));
        }

        DisplayItems = new ObservableCollection<LibraryDisplayItem>(nextItems);
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
            ? $"{Books.Count} documento(s) en biblioteca ({groupCount} serie(s) agrupadas)."
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
