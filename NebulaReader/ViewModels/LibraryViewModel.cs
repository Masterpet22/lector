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
    private readonly IFullScreenService _fullScreenService;

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
        IFullScreenService fullScreenService)
    {
        _libraryService = libraryService;
        _filePickerService = filePickerService;
        _fullScreenService = fullScreenService;
        _libraryService.BookUpdated += OnBookUpdated;
        _fullScreenService.FullScreenChanged += OnFullScreenChanged;
        IsFullScreen = _fullScreenService.IsFullScreen;
        Title = "Biblioteca - Nebula Reader";
    }

    private void OnFullScreenChanged(object? sender, bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() => IsFullScreen = enabled);
    }

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

            StatusMessage = "Escaneando carpeta...";
            var imported = await _libraryService.ImportFolderAsync(folderPathOrUri);
            foreach (var book in imported)
            {
                UpsertBookInCollection(book);
            }

            RebuildDisplayItems();
            StatusMessage = imported.Count > 0
                ? $"Se indexaron {imported.Count} documento(s). Total en biblioteca: {Books.Count}."
                : "No se encontraron archivos CBZ, CBR o PDF en la carpeta seleccionada.";
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
        await Shell.Current.GoToAsync("ReaderPage", new Dictionary<string, object>
        {
            { "Book", book }
        });
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
