using NebulaReader.ViewModels;

namespace NebulaReader.Views;

public partial class ManagementPage : ContentPage
{
    public ManagementPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
#if ANDROID
        SafeAreaEdges = new Microsoft.Maui.SafeAreaEdges(Microsoft.Maui.SafeAreaRegions.Container);
#endif
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || ManagementContainer == null) return;
        ManagementContainer.WidthRequest = Math.Min(680, Math.Max(0, width - 16));
        ManagementContainer.Padding = width < 480 || height < 500
            ? new Thickness(10)
            : new Thickness(18, 16);
    }
}
