using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla de ajustes del lector y preferencias visuales.
/// </summary>
public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

