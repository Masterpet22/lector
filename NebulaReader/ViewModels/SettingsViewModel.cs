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
    private readonly IReaderSettingsService _settingsService;
    private readonly IFullScreenService _fullScreenService;

    public IReadOnlyList<string> LayoutModeOptions { get; } =
    [
        "Una sola página",
        "Doble página (pantallas amplias)",
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
    public partial bool RightToLeftMangaMode { get; set; }

    [ObservableProperty]
    public partial int SelectedLayoutModeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedPageFitModeIndex { get; set; }

    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    public SettingsViewModel(IReaderSettingsService settingsService, IFullScreenService fullScreenService)
    {
        _settingsService = settingsService;
        _fullScreenService = fullScreenService;
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
    }

    private void OnFullScreenChanged(object? sender, bool enabled)
    {
        MainThread.BeginInvokeOnMainThread(() => IsFullScreen = enabled);
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
