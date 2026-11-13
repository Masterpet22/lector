using CommunityToolkit.Mvvm.ComponentModel;

namespace NebulaReader.ViewModels;

/// <summary>
/// Clase base MVVM con soporte para propiedades observables y estado de carga.
/// </summary>
public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
    }
}
