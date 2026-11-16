using NebulaReader.ViewModels;
using NebulaReader.Models;
using NebulaReader.Services;
using System.ComponentModel;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla principal de la biblioteca con cuadrícula adaptable por tamaño y orientación de pantalla.
/// </summary>
public partial class LibraryPage : ContentPage
{
    private const string LibraryViewPreferenceKey = "library_view_mode";
    private int _firstVisibleLibraryItem;
    private bool _restoringLibraryPosition;
    private bool _initialLoadStarted;
    private int _notificationVersion;
    private DataTemplate? _cardTemplate;

    public LibraryViewModel ViewModel { get; }

    public LibraryPage(LibraryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _cardTemplate = LibraryCollection.ItemTemplate as DataTemplate;
        LibraryViewPicker.SelectedIndex = Math.Clamp(Preferences.Default.Get(LibraryViewPreferenceKey, 2), 0, 7);
        BindingContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateAdaptiveLayout(Width, Height);

        if (_initialLoadStarted || BindingContext is not LibraryViewModel vm) return;
        _initialLoadStarted = true;

        try
        {
            _restoringLibraryPosition = true;
            StartupLoadingStatus.Text = "Cargando biblioteca...";
            await vm.LoadLibraryCommand.ExecuteAsync(null);
            StartupLoadingStatus.Text = "Actualizando biblioteca...";
            await vm.SynchronizeLibraryAsync();
            RestoreLibraryPosition();
        }
        finally
        {
            StartupLoadingOverlay.IsVisible = false;
            ShowSyncNotification(vm.ImportSummary);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateAdaptiveLayout(width, height);
    }

    private void OnMainContentSizeChanged(object? sender, EventArgs e)
    {
        if (MainContentGrid.Width <= 0) return;
        var contentWidth = GetVisibleContentWidth(Width);
        UpdateHeaderLayout(contentWidth, Width >= 1000, Width < 760);
        UpdateContentLayout(contentWidth);
    }

    private void OnLibraryCollectionSizeChanged(object? sender, EventArgs e)
    {
        if (LibraryCollection.Width > 0)
            UpdateContentLayout(GetVisibleContentWidth(Width));
    }

    private void OnHomeScrollSizeChanged(object? sender, EventArgs e)
    {
        if (HomeScroll.Width > 0) UpdateRecentCardWidth(HomeScroll.Width);
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is LibraryViewModel vm && vm.IsLibrarySelected && vm.IsInsideGroup)
        {
            vm.ExitGroupCommand.Execute(null);
            return true;
        }

        if (BindingContext is LibraryViewModel { IsLibrarySelected: true } library)
        {
            library.ShowHomeCommand.Execute(null);
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
        var isWide = width >= 1000;
        var isCompact = width < 760;

        // 1. Espaciado adaptable para evitar recortes en móviles horizontales de altura reducida
        LibraryRootGrid.Padding = isCompactHeight ? new Thickness(10, 6) : new Thickness(18, 16);
        LibraryRootGrid.RowSpacing = isCompactHeight ? 8 : 16;
        LibraryToolbarBorder.Padding = isCompactHeight ? new Thickness(12, 9) : new Thickness(18, 16);
        LibraryBreadcrumbBorder.Padding = isCompactHeight ? new Thickness(10, 6) : new Thickness(12, 9);
        LibraryFooterBorder.Padding = isCompactHeight ? new Thickness(10, 5) : new Thickness(14, 9);

        PrimarySidebar.IsVisible = isWide;
        PrimarySidebar.WidthRequest = Math.Clamp(width * 0.2, 200, 246);
        BottomNavigation.IsVisible = !isWide;
        ApplyRootGridMetrics(isWide);
        ManagementHeaderButton.IsVisible = !isWide;
        CompactFilterToggle.IsVisible = isCompact;
        InlineFilters.IsVisible = !isCompact && !isWide;
        ViewModel.UseCompactFilters = isCompact;

        var contentWidth = GetVisibleContentWidth(width);
        UpdateHeaderLayout(contentWidth, isWide, isCompact);
        UpdateContentLayout(contentWidth);
    }

    private const double SidebarSpacing = 16;

    /// <summary>
    /// Fija el ancho de la columna del panel lateral y la fila de navegación inferior. Con columnas Auto,
    /// cambiar IsVisible durante OnSizeAllocated no remedía el Grid y la columna central conservaba el ancho
    /// de pantalla completo, de modo que el contenido se salía por la derecha.
    /// </summary>
    private void ApplyRootGridMetrics(bool isWide)
    {
        var sidebarColumn = isWide ? PrimarySidebar.WidthRequest : 0;
        var changed = false;

        if (LibraryRootGrid.ColumnDefinitions[0].Width.Value != sidebarColumn ||
            !LibraryRootGrid.ColumnDefinitions[0].Width.IsAbsolute)
        {
            LibraryRootGrid.ColumnDefinitions[0].Width = new GridLength(sidebarColumn);
            changed = true;
        }

        var spacing = isWide ? SidebarSpacing : 0;
        if (LibraryRootGrid.ColumnSpacing != spacing)
        {
            LibraryRootGrid.ColumnSpacing = spacing;
            changed = true;
        }

        var bottomRow = isWide ? new GridLength(0) : GridLength.Auto;
        if (LibraryRootGrid.RowDefinitions[1].Height != bottomRow)
        {
            LibraryRootGrid.RowDefinitions[1].Height = bottomRow;
            changed = true;
        }

        if (changed)
            Dispatcher.Dispatch(() => LibraryRootGrid.InvalidateMeasure());
    }

    private double GetVisibleContentWidth(double pageWidth)
    {
        var expectedWidth = pageWidth - LibraryRootGrid.Padding.HorizontalThickness -
            (PrimarySidebar.IsVisible ? PrimarySidebar.WidthRequest + SidebarSpacing : 0);

        return ResponsiveLayout.GetVisibleWidth(expectedWidth, MainContentGrid.Width,
            LibraryCollection.IsVisible ? LibraryCollection.Width : 0);
    }

    private void UpdateHeaderLayout(double contentWidth, bool isWide, bool isCompact)
    {
        var stackedToolbar = contentWidth < 560;
        LibraryToolbarGrid.RowSpacing = stackedToolbar ? 8 : 0;
        Grid.SetColumnSpan(LibraryToolbarTitle, stackedToolbar ? 4 : 1);
        Grid.SetRow(AddHeaderButton, stackedToolbar ? 1 : 0);
        Grid.SetColumn(AddHeaderButton, stackedToolbar ? 0 : 1);
        Grid.SetColumnSpan(AddHeaderButton, stackedToolbar ? 2 : 1);
        Grid.SetRow(RefreshLibraryButton, stackedToolbar ? 1 : 0);
        Grid.SetColumn(RefreshLibraryButton, 2);
        Grid.SetColumnSpan(RefreshLibraryButton, stackedToolbar ? 2 : 1);
        Grid.SetRow(ManagementHeaderButton, stackedToolbar ? 2 : 0);
        Grid.SetColumn(ManagementHeaderButton, stackedToolbar ? 0 : 3);
        Grid.SetColumnSpan(ManagementHeaderButton, stackedToolbar ? 4 : 1);

        var stackedSearch = contentWidth < 520;
        Grid.SetColumnSpan(LibrarySearchBar, stackedSearch ? 2 : 1);
        Grid.SetRow(CompactFilterToggle, stackedSearch ? 1 : 0);
        Grid.SetColumn(CompactFilterToggle, stackedSearch ? 0 : 1);
        Grid.SetColumnSpan(CompactFilterToggle, stackedSearch ? 2 : 1);
        if (!isWide && !isCompact)
        {
            Grid.SetRow(InlineFilters, 0);
            Grid.SetColumn(InlineFilters, 1);
            Grid.SetColumnSpan(InlineFilters, 1);
            InlineFilters.WidthRequest = Math.Min(360, Math.Max(0, contentWidth * 0.43));
        }
        else
        {
            Grid.SetRow(InlineFilters, 1);
            Grid.SetColumn(InlineFilters, 0);
            Grid.SetColumnSpan(InlineFilters, 2);
            InlineFilters.WidthRequest = -1;
        }

    }

    private void UpdateContentLayout(double contentWidth)
    {
        if (contentWidth <= 0 || LibraryGridLayout == null) return;

        var mode = Math.Max(0, LibraryViewPicker.SelectedIndex);
        DetailsColumnHeader.IsVisible = mode == 5;
        var isCard = mode <= 2;
        var isCompactIcon = mode is 3 or 6;
        var templateKey = mode switch
        {
            3 or 6 => "CompactIconTemplate",
            4 => "ListItemTemplate",
            5 => "DetailsItemTemplate",
            7 => "ContentItemTemplate",
            _ => null
        };
        var template = templateKey is null ? _cardTemplate : LibraryCollection.Resources[templateKey] as DataTemplate;
        if (template != null && !ReferenceEquals(LibraryCollection.ItemTemplate, template))
            LibraryCollection.ItemTemplate = template;

        // Reservar unos DIP para el scrollbar y medir con el ancho real del área central.
        var hSpacing = Height < 520 ? 7.0 : 10.0;
        var gridWidth = Math.Max(0, contentWidth - 8);
        var minimumItemWidth = mode switch
        {
            0 => 320.0,
            1 => 260.0,
            2 => 200.0,
            3 => 120.0,
            6 => 96.0,
            _ => gridWidth
        };
        var targetSpan = isCard || isCompactIcon
            ? ResponsiveLayout.GetColumnCount(gridWidth, minimumItemWidth, hSpacing, 12)
            : 1;

        if (LibraryCollection.ItemsLayout is GridItemsLayout currentLayout && currentLayout.Span != targetSpan)
        {
            // WinUI necesita un nuevo ItemsLayout para distribuir de nuevo las tarjetas al reducir la ventana.
            LibraryCollection.ItemsLayout = new GridItemsLayout(targetSpan, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = hSpacing,
                VerticalItemSpacing = hSpacing
            };
            if (ViewModel.IsLibrarySelected) RestoreLibraryPosition();
        }

        if (LibraryCollection.ItemsLayout is GridItemsLayout gridLayout)
        {
            gridLayout.HorizontalItemSpacing = hSpacing;
            gridLayout.VerticalItemSpacing = hSpacing;
        }

        // Las tarjetas mantienen sus acciones; las vistas compactas abren al tocar el elemento.
        var approxColWidth = Math.Max(110.0, (gridWidth - ((targetSpan - 1) * hSpacing)) / targetSpan);
        ViewModel.UseStackedBookActions = approxColWidth < 190;
        var desiredCoverHeight = mode switch
        {
            0 => Math.Clamp(approxColWidth * 0.85, 180.0, 280.0),
            1 => Math.Clamp(approxColWidth * 0.85, 150.0, 220.0),
            _ => Math.Clamp(approxColWidth * 0.85, 110.0, 180.0)
        };

        if (Height < 520)
        {
            desiredCoverHeight = Math.Min(desiredCoverHeight, Math.Clamp(Height * 0.32, 100.0, 140.0));
        }

        UpdateRecentCardWidth(contentWidth);
        ViewModel.UpdateCardCoverHeight(Math.Round(desiredCoverHeight));
    }

    private void OnLibraryViewChanged(object sender, EventArgs e)
    {
        if (LibraryViewPicker.SelectedIndex < 0 || LibraryGridLayout == null) return;
        Preferences.Default.Set(LibraryViewPreferenceKey, LibraryViewPicker.SelectedIndex);
        _firstVisibleLibraryItem = 0;
        UpdateContentLayout(GetVisibleContentWidth(Width));
        if (ViewModel.IsLibrarySelected) LibraryCollection.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
    }

    private void UpdateRecentCardWidth(double contentWidth)
    {
        // ScrollView puede reservar una barra vertical dentro de su Width. Dejar aire
        // adicional evita que el borde derecho de la última tarjeta quede recortado.
        var viewportWidth = HomeScroll.Width > 0 ? Math.Min(contentWidth, HomeScroll.Width) : contentWidth;
        ViewModel.RecentCardWidth = Math.Round(ResponsiveLayout.GetRecentCardWidth(viewportWidth));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryViewModel.SearchText) or
            nameof(LibraryViewModel.SelectedFormatIndex) or
            nameof(LibraryViewModel.SelectedStatusIndex) or
            nameof(LibraryViewModel.ActiveGroupTitle))
        {
            _firstVisibleLibraryItem = 0;
            _restoringLibraryPosition = true;
            RestoreLibraryPosition();
        }
        else if (e.PropertyName == nameof(LibraryViewModel.IsHomeSelected))
        {
            _restoringLibraryPosition = true;
            RestoreLibraryPosition();
        }
    }

    private void OnLibraryScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (!_restoringLibraryPosition && ViewModel.IsLibrarySelected && e.FirstVisibleItemIndex >= 0)
            _firstVisibleLibraryItem = e.FirstVisibleItemIndex;
    }

    private void RestoreLibraryPosition()
    {
        if (!ViewModel.IsLibrarySelected) return;
        if (ViewModel.DisplayItems.Count == 0)
        {
            _restoringLibraryPosition = false;
            return;
        }

        var index = Math.Min(_firstVisibleLibraryItem, ViewModel.DisplayItems.Count - 1);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () =>
        {
            if (ViewModel.IsLibrarySelected && ViewModel.DisplayItems.Count > index)
                LibraryCollection.ScrollTo(index, position: ScrollToPosition.Start, animate: false);

            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
                _restoringLibraryPosition = false);
        });
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var action = await DisplayActionSheetAsync("Añadir a la biblioteca", "Cancelar", null,
            "Abrir archivo", "Escanear carpeta");

        if (action == "Abrir archivo")
            await ViewModel.OpenFileCommand.ExecuteAsync(null);
        else if (action == "Escanear carpeta")
            await ViewModel.ImportFolderCommand.ExecuteAsync(null);
    }

    private async void OnRefreshLibraryClicked(object sender, EventArgs e)
    {
        if (ViewModel.IsBusy) return;
        await ViewModel.SynchronizeLibraryAsync();
        ShowSyncNotification(ViewModel.ImportSummary);
    }

    private void ShowSyncNotification(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var version = ++_notificationVersion;
        LibrarySyncNotificationText.Text = message;
        LibrarySyncNotification.IsVisible = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(5), () =>
        {
            if (version == _notificationVersion)
                LibrarySyncNotification.IsVisible = false;
        });
    }

    private async void OnManagementClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new ManagementPage(ViewModel));
    }

    private async void OnBookActionsClicked(object sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: LibraryDisplayItem { Book: { } book } }) return;
        try
        {
            var action = await DisplayActionSheetAsync(book.Title, "Cancelar", null,
                "Editar título y serie", "Marcar como leído", "Marcar como no leído",
                "Volver a localizar / Conceder acceso", "Reintentar portada", "Quitar de la biblioteca");
            switch (action)
            {
                case "Editar título y serie":
                    var title = await DisplayPromptAsync("Editar título", "Título", initialValue: book.Title);
                    if (title == null) return;
                    var series = await DisplayPromptAsync("Editar serie", "Serie (vacío para quitar)", initialValue: book.SeriesName ?? "");
                    if (series == null) return;
                    await ViewModel.EditDetailsAsync(book, title, series);
                    break;
                case "Marcar como leído":
                    if (await DisplayAlertAsync("Marcar como leído", "Se guardará la última página como progreso. ¿Continuar?", "Sí", "Cancelar"))
                        await ViewModel.SetStatusAsync(book, ReadingStatus.Completed);
                    break;
                case "Marcar como no leído":
                    if (await DisplayAlertAsync("Marcar como no leído", "El progreso volverá a la página 1. ¿Continuar?", "Sí", "Cancelar"))
                        await ViewModel.SetStatusAsync(book, ReadingStatus.Unread);
                    break;
                case "Volver a localizar / Conceder acceso":
                    await ViewModel.RecoverBookAsync(book);
                    break;
                case "Reintentar portada":
                    await ViewModel.RetryCoverAsync(book);
                    break;
                case "Quitar de la biblioteca":
                    if (await DisplayAlertAsync("Quitar documento", "Se quitará la entrada y su progreso de la biblioteca. El archivo original se conservará. ¿Continuar?", "Quitar", "Cancelar"))
                        await ViewModel.RemoveBookAsync(book);
                    break;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("No se pudo completar la acción", ex.Message, "Cerrar");
        }
    }
}
