using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NebulaReader.Models;
using NebulaReader.Services;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel del visor de lectura (CBR, CBZ y PDF).
/// </summary>
public partial class ReaderViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ILibraryService _libraryService;
    private readonly IPageCacheService _pageCacheService;

    [ObservableProperty]
    public partial BookItem? CurrentBook { get; set; }

    [ObservableProperty]
    public partial int CurrentPageIndex { get; set; }

    [ObservableProperty]
    public partial int TotalPages { get; set; }

    [ObservableProperty]
    public partial string PageIndicator { get; set; } = "Página 0 / 0";

    public ReaderViewModel(ILibraryService libraryService, IPageCacheService pageCacheService)
    {
        _libraryService = libraryService;
        _pageCacheService = pageCacheService;
        Title = "Lector";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Book", out var value) && value is BookItem book)
        {
            CurrentBook = book;
            Title = book.Title;
            TotalPages = book.TotalPages;
            CurrentPageIndex = Math.Max(0, book.CurrentPage - 1);
            UpdateIndicator();
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentBook == null || CurrentPageIndex >= TotalPages - 1) return;
        CurrentPageIndex++;
        _pageCacheService.EvictOutsideWindow(CurrentBook.Id, CurrentPageIndex);
        UpdateIndicator();
        await PersistProgressAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentBook == null || CurrentPageIndex <= 0) return;
        CurrentPageIndex--;
        _pageCacheService.EvictOutsideWindow(CurrentBook.Id, CurrentPageIndex);
        UpdateIndicator();
        await PersistProgressAsync();
    }

    [RelayCommand]
    private async Task CloseReaderAsync()
    {
        if (CurrentBook != null)
        {
            _pageCacheService.ClearBook(CurrentBook.Id);
        }
        await Shell.Current.GoToAsync("..");
    }

    private void UpdateIndicator()
    {
        PageIndicator = TotalPages > 0
            ? $"Página {CurrentPageIndex + 1} / {TotalPages}"
            : "Sin páginas cargadas (Fase 2)";
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

