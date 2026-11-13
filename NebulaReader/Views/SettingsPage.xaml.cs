using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla de ajustes del lector y preferencias visuales adaptada a cualquier resolución.
/// </summary>
public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || SettingsContainer == null)
        {
            return;
        }

        SettingsContainer.WidthRequest = Math.Min(760.0, Math.Max(260.0, width - 8.0));
        SettingsContainer.Padding = height < 500
            ? new Thickness(14, 10)
            : new Thickness(20, 18);
    }
}
