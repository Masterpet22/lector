using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla del visor de lectura.
/// </summary>
public partial class ReaderPage : ContentPage
{
    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

