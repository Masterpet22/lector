using NebulaReader.ViewModels;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla principal de la biblioteca con cuadrícula adaptable por tamaño y orientación de pantalla.
/// </summary>
public partial class LibraryPage : ContentPage
{
    private bool? _isToolbarWideMode;

    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateAdaptiveLayout(Width, Height);

        if (BindingContext is LibraryViewModel vm)
        {
            await vm.LoadLibraryCommand.ExecuteAsync(null);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateAdaptiveLayout(width, height);
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is LibraryViewModel vm && vm.IsInsideGroup)
        {
            vm.ExitGroupCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void UpdateAdaptiveLayout(double width, double height)
    {
        if (width <= 0 || height <= 0 || LibraryGridLayout == null)
        {
            return;
        }

        var isCompactHeight = height < 520;

        // 1. Espaciado adaptable para evitar recortes en móviles horizontales de altura reducida
        LibraryRootGrid.Padding = isCompactHeight ? new Thickness(10, 6) : new Thickness(18, 16);
        LibraryRootGrid.RowSpacing = isCompactHeight ? 8 : 16;
        LibraryToolbarBorder.Padding = isCompactHeight ? new Thickness(12, 9) : new Thickness(18, 16);
        LibraryBreadcrumbBorder.Padding = isCompactHeight ? new Thickness(10, 6) : new Thickness(12, 9);
        LibraryFooterBorder.Padding = isCompactHeight ? new Thickness(10, 5) : new Thickness(14, 9);

        UpdateToolbarLayout(width, isCompactHeight);

        // 2. Columnas adaptables para pantallas móviles (320dp..920dp), tabletas y monitores (HD/FHD/2K/4K)
        int targetSpan;
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Phone)
        {
            targetSpan = width >= 720 ? 4 : (width >= 500 ? 3 : 2);
        }
        else if (DeviceInfo.Current.Idiom == DeviceIdiom.Tablet)
        {
            targetSpan = Math.Clamp((int)Math.Floor(width / 195.0), 3, 6);
        }
        else
        {
            targetSpan = width >= 900
                ? Math.Clamp((int)Math.Floor(width / 195.0), 4, 8)
                : Math.Clamp((int)Math.Floor(width / 180.0), 2, 4);
        }

        if (LibraryGridLayout.Span != targetSpan)
        {
            LibraryGridLayout.Span = targetSpan;
        }

        var hSpacing = isCompactHeight ? 8.0 : 14.0;
        LibraryGridLayout.HorizontalItemSpacing = hSpacing;
        LibraryGridLayout.VerticalItemSpacing = hSpacing;

        // 3. Alto proporcional de portada para que las tarjetas mantengan proporción sin desbordar ni cortarse
        var horizontalChrome = LibraryRootGrid.Padding.HorizontalThickness + ((targetSpan - 1) * hSpacing);
        var approxColWidth = Math.Max(110.0, (width - horizontalChrome) / targetSpan);
        var desiredCoverHeight = Math.Clamp(approxColWidth * 1.28, 130.0, 245.0);

        if (isCompactHeight)
        {
            desiredCoverHeight = Math.Min(desiredCoverHeight, Math.Clamp(height * 0.36, 110.0, 155.0));
        }

        if (BindingContext is LibraryViewModel vm)
        {
            vm.UpdateCardCoverHeight(Math.Round(desiredCoverHeight));
        }
    }

    private void UpdateToolbarLayout(double width, bool isCompactHeight)
    {
        if (LibraryToolbarGrid == null)
        {
            return;
        }

        LibraryToolbarGrid.RowSpacing = isCompactHeight ? 6 : 8;
        LibraryToolbarGrid.ColumnSpacing = isCompactHeight ? 6 : 8;

        var isWide = width >= 660;
        if (_isToolbarWideMode == isWide)
        {
            return;
        }

        _isToolbarWideMode = isWide;

        LibraryToolbarGrid.ColumnDefinitions.Clear();
        LibraryToolbarGrid.RowDefinitions.Clear();

        if (isWide)
        {
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            LibraryToolbarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            SetToolbarItemPlacement(OpenFileButton, 0, 0, LayoutOptions.Start);
            SetToolbarItemPlacement(ScanFolderButton, 0, 1, LayoutOptions.Start);
            SetToolbarItemPlacement(SettingsButton, 0, 2, LayoutOptions.Start);
            SetToolbarItemPlacement(FullScreenButton, 0, 3, LayoutOptions.Start);

            Grid.SetRow(ToolbarBusyIndicator, 0);
            Grid.SetColumn(ToolbarBusyIndicator, 4);
            Grid.SetColumnSpan(ToolbarBusyIndicator, 1);
            ToolbarBusyIndicator.HorizontalOptions = LayoutOptions.Start;
        }
        else
        {
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            LibraryToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            LibraryToolbarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            LibraryToolbarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            LibraryToolbarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            SetToolbarItemPlacement(OpenFileButton, 0, 0, LayoutOptions.Fill);
            SetToolbarItemPlacement(ScanFolderButton, 0, 1, LayoutOptions.Fill);
            SetToolbarItemPlacement(SettingsButton, 1, 0, LayoutOptions.Fill);
            SetToolbarItemPlacement(FullScreenButton, 1, 1, LayoutOptions.Fill);

            Grid.SetRow(ToolbarBusyIndicator, 2);
            Grid.SetColumn(ToolbarBusyIndicator, 0);
            Grid.SetColumnSpan(ToolbarBusyIndicator, 2);
            ToolbarBusyIndicator.HorizontalOptions = LayoutOptions.Center;
        }
    }

    private static void SetToolbarItemPlacement(View view, int row, int column, LayoutOptions horizontalOptions)
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        view.HorizontalOptions = horizontalOptions;
    }
}
