using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla principal de la biblioteca con cuadrícula adaptable por tamaño de pantalla.
/// </summary>
public partial class LibraryPage : ContentPage
{
    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateAdaptiveGridSpan(Width);

        if (BindingContext is LibraryViewModel vm)
        {
            await vm.LoadLibraryCommand.ExecuteAsync(null);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateAdaptiveGridSpan(width);
    }

    private void UpdateAdaptiveGridSpan(double width)
    {
        if (width <= 0 || LibraryGridLayout == null)
        {
            return;
        }

        int targetSpan;
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Phone)
        {
            // 2 columnas en vertical y hasta 3 columnas en horizontal/pantallas amplias de teléfono
            targetSpan = width >= 540 ? 3 : 2;
        }
        else if (DeviceInfo.Current.Idiom == DeviceIdiom.Tablet)
        {
            targetSpan = Math.Clamp((int)Math.Floor(width / 200.0), 3, 5);
        }
        else
        {
            // Escritorio (Windows): 5-7 columnas dinámicas al redimensionar la ventana
            targetSpan = width >= 900
                ? Math.Clamp((int)Math.Floor(width / 190.0), 5, 7)
                : Math.Clamp((int)Math.Floor(width / 190.0), 2, 4);
        }

        if (LibraryGridLayout.Span != targetSpan)
        {
            LibraryGridLayout.Span = targetSpan;
        }
    }
}
