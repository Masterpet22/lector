using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NebulaReader.Models;
using NebulaReader.Services;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel de ajustes de lectura y caché sincronizado con IReaderSettingsService.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    public bool CanRegisterFileAssociations => OperatingSystem.IsWindows();
    private readonly IReaderSettingsService _settingsService;
    private readonly IFullScreenService _fullScreenService;
    private readonly IImportedFolderStore _importedFolderStore;
    private readonly IPageCacheService _pageCacheService;
    private readonly ILibraryService _libraryService;
    private readonly IOnlineTagSettings _onlineTagSettings;
    private readonly LibrarySyncService _librarySyncService;

    public IReadOnlyList<TagLanguageOption> TagLanguageOptions => TagLanguages.Options;

    [ObservableProperty]
    public partial TagLanguageOption? SelectedTagLanguage { get; set; }

    partial void OnSelectedTagLanguageChanged(TagLanguageOption? value)
    {
        if (value != null) _onlineTagSettings.Language = value.Code;
    }

    [ObservableProperty]
    public partial string CacheStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FolderStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MetronToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ComicVineApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GoogleBooksApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OnlineKeysStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoImportedFolders))]
    public partial ObservableCollection<ImportedFolderEntry> ImportedFolders { get; set; } = new();

    public bool HasNoImportedFolders => ImportedFolders.Count == 0;

    public IReadOnlyList<string> LayoutModeOptions { get; } =
    [
        "Una sola página",
        "Doble página (2 páginas)",
        "Lectura continua (de arriba a abajo)"
    ];

    public IReadOnlyList<string> PageFitModeOptions { get; } =
    [
        "Completo (toda la página)",
        "Ajustar al ancho",
        "Ajustar al alto"
    ];

    [ObservableProperty]
    public partial bool NightReadingModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool SepiaFilterEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingDirectionIcon))]
    public partial bool RightToLeftMangaMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutModeIcon))]
    public partial int SelectedLayoutModeIndex { get; set; }

    public string ReadingDirectionIcon => RightToLeftMangaMode ? "reading_manga.png" : "reading_comic.png";
    public string LayoutModeIcon => SelectedLayoutModeIndex switch
    {
        0 => "reading_single.png",
        2 => "reading_continuous.png",
        _ => "reading_spread.png"
    };

    [ObservableProperty]
    public partial int SelectedPageFitModeIndex { get; set; }

    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    public SettingsViewModel(IReaderSettingsService settingsService, IFullScreenService fullScreenService,
        IImportedFolderStore importedFolderStore, IPageCacheService pageCacheService,
        ILibraryService libraryService, IOnlineTagSettings onlineTagSettings,
        LibrarySyncService librarySyncService)
    {
        _librarySyncService = librarySyncService;
        _onlineTagSettings = onlineTagSettings;
        SelectedTagLanguage = TagLanguages.Options.First(option => option.Code == _onlineTagSettings.Language);
        _settingsService = settingsService;
        _fullScreenService = fullScreenService;
        _importedFolderStore = importedFolderStore;
        _pageCacheService = pageCacheService;
        _libraryService = libraryService;
        Title = "Configuración";

        NightReadingModeEnabled = _settingsService.VisualFilter == ReaderVisualFilter.NightInvert;
        SepiaFilterEnabled = _settingsService.VisualFilter == ReaderVisualFilter.Sepia;
        RightToLeftMangaMode = _settingsService.RightToLeftMangaMode;
        SelectedLayoutModeIndex = _settingsService.LayoutMode switch
        {
            ReaderLayoutMode.SinglePage => 0,
            ReaderLayoutMode.DoublePage => 1,
            ReaderLayoutMode.ContinuousVertical => 2,
            _ => 1
        };
        SelectedPageFitModeIndex = _settingsService.PageFitMode switch
        {
            PageFitMode.FitPage => 0,
            PageFitMode.FitWidth => 1,
            PageFitMode.FitHeight => 2,
            _ => 0
        };
        IsFullScreen = _fullScreenService.IsFullScreen;
        _fullScreenService.FullScreenChanged += OnFullScreenChanged;
        ReloadImportedFolders();
    }

    public void ReloadImportedFolders() => ImportedFolders = new ObservableCollection<ImportedFolderEntry>(
        _importedFolderStore.GetFolders().Select(path => new ImportedFolderEntry { PathOrUri = path }));

    public async Task LoadOnlineKeysAsync()
    {
        try
        {
            MetronToken = await AppSecureStorage.GetAsync(OnlineTagService.MetronTokenKey) ?? string.Empty;
            ComicVineApiKey = await AppSecureStorage.GetAsync(OnlineTagService.ComicVineKey) ?? string.Empty;
            GoogleBooksApiKey = await AppSecureStorage.GetAsync(OnlineTagService.GoogleBooksKey) ?? string.Empty;
        }
        catch (Exception ex)
        {
            OnlineKeysStatus = $"No se pudieron cargar las claves: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveOnlineKeysAsync()
    {
        try
        {
            await StoreKeyAsync(OnlineTagService.MetronTokenKey, MetronToken);
            await StoreKeyAsync(OnlineTagService.ComicVineKey, ComicVineApiKey);
            await StoreKeyAsync(OnlineTagService.GoogleBooksKey, GoogleBooksApiKey);
            OnlineKeysStatus = "Claves guardadas en este dispositivo.";
        }
        catch (Exception ex)
        {
            OnlineKeysStatus = $"No se pudieron guardar las claves: {ex.Message}";
        }
    }

    private static async Task StoreKeyAsync(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) AppSecureStorage.Remove(name);
        else await AppSecureStorage.SetAsync(name, value.Trim());
    }

    [RelayCommand]
    private async Task RemoveImportedFolderAsync(string pathOrUri)
    {
        try
        {
            var removed = await _librarySyncService.RemoveFolderAsync(pathOrUri);
            ReloadImportedFolders();
            FolderStatusMessage = $"Carpeta quitada. {removed} documento(s) retirado(s) de la biblioteca.";
        }
        catch (Exception ex)
        {
            FolderStatusMessage = $"No se pudo quitar la carpeta: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        CacheStatusMessage = string.Empty;
        try
        {
            _pageCacheService.ClearAll();
            await _libraryService.ClearCoverCacheAsync();
            CacheStatusMessage = "Caché borrada. Las portadas se regenerarán al abrir la biblioteca.";
        }
        catch (Exception ex)
        {
            CacheStatusMessage = $"No se pudo borrar toda la caché: {ex.Message}";
        }
    }

    private void OnFullScreenChanged(object? sender, bool enabled)
    {
        if (AppLifetime.IsShuttingDown) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!AppLifetime.IsShuttingDown) IsFullScreen = enabled;
        });
    }

    partial void OnNightReadingModeEnabledChanged(bool value)
    {
        if (value)
        {
            SepiaFilterEnabled = false;
            _settingsService.VisualFilter = ReaderVisualFilter.NightInvert;
        }
        else if (!SepiaFilterEnabled)
        {
            _settingsService.VisualFilter = ReaderVisualFilter.None;
        }
    }

    partial void OnSepiaFilterEnabledChanged(bool value)
    {
        if (value)
        {
            NightReadingModeEnabled = false;
            _settingsService.VisualFilter = ReaderVisualFilter.Sepia;
        }
        else if (!NightReadingModeEnabled)
        {
            _settingsService.VisualFilter = ReaderVisualFilter.None;
        }
    }

    partial void OnRightToLeftMangaModeChanged(bool value) => _settingsService.RightToLeftMangaMode = value;

    partial void OnSelectedLayoutModeIndexChanged(int value)
    {
        _settingsService.LayoutMode = value switch
        {
            0 => ReaderLayoutMode.SinglePage,
            1 => ReaderLayoutMode.DoublePage,
            2 => ReaderLayoutMode.ContinuousVertical,
            _ => ReaderLayoutMode.DoublePage
        };
    }

    partial void OnSelectedPageFitModeIndexChanged(int value)
    {
        _settingsService.PageFitMode = value switch
        {
            0 => PageFitMode.FitPage,
            1 => PageFitMode.FitWidth,
            2 => PageFitMode.FitHeight,
            _ => PageFitMode.FitPage
        };
    }

    partial void OnIsFullScreenChanged(bool value)
    {
        _fullScreenService.SetFullScreen(value);
    }

    [RelayCommand]
    private Task GoBackAsync()
    {
        return Shell.Current.GoToAsync("..");
    }
}
