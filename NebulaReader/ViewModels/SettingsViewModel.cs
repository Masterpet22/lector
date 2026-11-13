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

    [ObservableProperty]
    public partial bool NightReadingModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool SepiaFilterEnabled { get; set; }

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

        NightReadingModeEnabled = _settingsService.VisualFilter == ReaderVisualFilter.NightInvert;
        SepiaFilterEnabled = _settingsService.VisualFilter == ReaderVisualFilter.Sepia;
        RightToLeftMangaMode = _settingsService.RightToLeftMangaMode;
        ContinuousVerticalScroll = _settingsService.ContinuousVerticalScroll;
        DoublePageSpreadEnabled = _settingsService.DoublePageSpreadEnabled;
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
    partial void OnContinuousVerticalScrollChanged(bool value) => _settingsService.ContinuousVerticalScroll = value;
    partial void OnDoublePageSpreadEnabledChanged(bool value) => _settingsService.DoublePageSpreadEnabled = value;

    [RelayCommand]
    private Task GoBackAsync()
    {
        return Shell.Current.GoToAsync("..");
    }
}
