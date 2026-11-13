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

    [ObservableProperty]
    public partial ObservableCollection<BookItem> Books { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<LibraryDisplayItem> DisplayItems { get; set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInsideGroup))]
    public partial string? ActiveGroupTitle { get; set; }

    public bool IsInsideGroup => !string.IsNullOrWhiteSpace(ActiveGroupTitle);

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Biblioteca lista. Abre un archivo o escanea una carpeta.";

    public LibraryViewModel(ILibraryService libraryService, IFilePickerService filePickerService)
    {
        _libraryService = libraryService;
        _filePickerService = filePickerService;
        _libraryService.BookUpdated += OnBookUpdated;
        Title = "Biblioteca - Nebula Reader";
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
        var buckets = BookMetadataExtractor.BuildLibraryGroups(
            Books,
            b => b.Title,
            b => b.SeriesName,
            b => b.Tags);

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
                    nextItems.Add(LibraryDisplayItem.FromSingleBook(book, bookTags));
                }

                DisplayItems = new ObservableCollection<LibraryDisplayItem>(nextItems);
                return;
            }

            ActiveGroupTitle = null;
        }

        foreach (var bucket in buckets)
        {
            nextItems.Add(bucket.IsGroup
                ? LibraryDisplayItem.FromSeriesGroup(bucket.DisplayName, bucket.Items, bucket.CommonTags)
                : LibraryDisplayItem.FromSingleBook(bucket.Items[0], bucket.CommonTags));
        }

        DisplayItems = new ObservableCollection<LibraryDisplayItem>(nextItems);
    }

    private void UpdateStatusSummary()
    {
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
