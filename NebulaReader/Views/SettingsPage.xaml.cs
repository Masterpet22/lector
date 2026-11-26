using NebulaReader.ViewModels;
using NebulaReader.Services;

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
#if ANDROID
        SafeAreaEdges = new Microsoft.Maui.SafeAreaEdges(Microsoft.Maui.SafeAreaRegions.Container);
#endif
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ViewModel.ReloadImportedFolders();
        await ViewModel.LoadOnlineKeysAsync();
    }

    private async void OnRegisterFileAssociationsClicked(object sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
#if WINDOWS
            WindowsFileAssociations.Register();
#endif
            await DisplayAlertAsync("Formatos registrados",
                "Selecciona Nebula Reader en Aplicaciones predeterminadas y asígnale .cbz, .cbr y .pdf. Tus elecciones actuales se conservan hasta que las cambies.", "Continuar");
            await Launcher.Default.OpenAsync("ms-settings:defaultapps");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudieron registrar los formatos", ex.Message, "Aceptar");
        }
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
        OnlineTagsPanel.Padding = panelPadding;
        CachePanel.Padding = panelPadding;
        ImportedFoldersPanel.Padding = panelPadding;
    }
}
