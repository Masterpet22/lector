using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla de ajustes del lector y preferencias visuales adaptada a cualquier resolución.
/// </summary>
public partial class SettingsPage : ContentPage
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel.ReloadImportedFolders();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || SettingsContainer == null)
        {
            return;
        }

        var compact = width < 480 || height < 500;
        SettingsContainer.WidthRequest = Math.Min(680.0, Math.Max(0.0, width - 16.0));
        SettingsContainer.Padding = compact ? new Thickness(10, 10) : new Thickness(20, 18);
        var panelPadding = compact ? new Thickness(14) : new Thickness(20);
        AppearancePanel.Padding = panelPadding;
        LayoutPanel.Padding = panelPadding;
        WindowPanel.Padding = panelPadding;
        ImportedFoldersPanel.Padding = panelPadding;
    }
}
