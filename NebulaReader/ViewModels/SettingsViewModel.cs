using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NebulaReader.Services;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel de ajustes de lectura y caché sincronizado con IReaderSettingsService.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    private readonly IReaderSettingsService _settingsService;

    [ObservableProperty]
    public partial bool NightReadingModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool RightToLeftMangaMode { get; set; }

    [ObservableProperty]
    public partial bool ContinuousVerticalScroll { get; set; }

    [ObservableProperty]
    public partial bool DoublePageSpreadEnabled { get; set; }

    public SettingsViewModel(IReaderSettingsService settingsService)
    {
        _settingsService = settingsService;
        Title = "Configuración";

        NightReadingModeEnabled = _settingsService.NightReadingModeEnabled;
        RightToLeftMangaMode = _settingsService.RightToLeftMangaMode;
        ContinuousVerticalScroll = _settingsService.ContinuousVerticalScroll;
        DoublePageSpreadEnabled = _settingsService.DoublePageSpreadEnabled;
    }

    partial void OnNightReadingModeEnabledChanged(bool value) => _settingsService.NightReadingModeEnabled = value;
    partial void OnRightToLeftMangaModeChanged(bool value) => _settingsService.RightToLeftMangaMode = value;
    partial void OnContinuousVerticalScrollChanged(bool value) => _settingsService.ContinuousVerticalScroll = value;
    partial void OnDoublePageSpreadEnabledChanged(bool value) => _settingsService.DoublePageSpreadEnabled = value;

    [RelayCommand]
    private Task GoBackAsync()
    {
        return Shell.Current.GoToAsync("..");
    }
}
