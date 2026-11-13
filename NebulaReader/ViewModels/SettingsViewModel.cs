using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NebulaReader.ViewModels;

/// <summary>
/// ViewModel de ajustes de lectura y caché.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    [ObservableProperty]
    public partial bool NightReadingModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool RightToLeftMangaMode { get; set; }

    [ObservableProperty]
    public partial bool ContinuousVerticalScroll { get; set; }

    public SettingsViewModel()
    {
        Title = "Configuración";
    }

    [RelayCommand]
    private Task GoBackAsync()
    {
        return Shell.Current.GoToAsync("..");
    }
}
