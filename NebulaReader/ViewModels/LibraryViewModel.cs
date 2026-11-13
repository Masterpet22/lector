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
            if (existing != null && !ReferenceEquals(existing, updated))
            {
                existing.CoverThumbnailPath = updated.CoverThumbnailPath;
                existing.TotalPages = updated.TotalPages;
                existing.CurrentPage = updated.CurrentPage;
                existing.Status = updated.Status;
                existing.LastReadAtUtc = updated.LastReadAtUtc;
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
            StatusMessage = $"{Books.Count} documento(s) en biblioteca.";
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
