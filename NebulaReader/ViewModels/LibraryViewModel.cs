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
    public partial string StatusMessage { get; set; } = "Biblioteca lista. Abre un archivo CBZ, CBR o PDF.";

    public LibraryViewModel(ILibraryService libraryService, IFilePickerService filePickerService)
    {
        _libraryService = libraryService;
        _filePickerService = filePickerService;
        Title = "Biblioteca - Nebula Reader";
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
            if (!Books.Any(b => b.Id == book.Id))
            {
                Books.Add(book);
            }

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
}

