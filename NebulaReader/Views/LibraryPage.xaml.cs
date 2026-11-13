using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla principal de la biblioteca de cómics y documentos.
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
        if (BindingContext is LibraryViewModel vm)
        {
            await vm.LoadLibraryCommand.ExecuteAsync(null);
        }
    }
}

