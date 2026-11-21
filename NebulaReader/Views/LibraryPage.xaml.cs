using NebulaReader.ViewModels;
using NebulaReader.Models;
using NebulaReader.Services;
using Microsoft.Maui.Dispatching;
using System.ComponentModel;

namespace NebulaReader.Views;

/// <summary>
/// Pantalla principal de la biblioteca con cuadrícula adaptable por tamaño y orientación de pantalla.
/// </summary>
public partial class LibraryPage : ContentPage
{
    private const string LibraryViewPreferenceKey = "library_view_mode";
    private int _firstVisibleLibraryItem;
    private int _firstVisibleScrollRow;
    private int _lastVisibleLibraryItem;
    private bool _restoringLibraryPosition;
    private bool _initialLoadStarted;
    private int _notificationVersion;
    private bool _showingCardRows = true;
    private int _cardColumns = 1;
    private readonly HashSet<Border> _visibleSelectableCards = new();
    private readonly Dictionary<Border, PointerGestureRecognizer> _cardPointers = new();
    private CancellationTokenSource? _holdCts;
    private CancellationTokenSource? _tagDownloadCts;
    private TaskCompletionSource<OnlineTagSuggestion?>? _tagPickerTcs;
    private TaskCompletionSource<string?>? _nebulaDialogTcs;
    private bool _tagPickerStopRequested;
    private Border? _pressedCard;
    private Point _pressPosition;
    private bool _dragSelecting;
    private IDispatcherTimer? _selectionScrollTimer;
    private int _selectionScrollDirection;
    private int _selectionScrollRow = -1;
    private double _selectionPointerX;
    private LibraryDisplayItem? _suppressNextTapItem;
    private int _selectionAnchorIndex = -1;
    private int _keyboardIndex = -1;
    private readonly ExternalDocumentOpenService _externalDocuments;
#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _selectionWindowRoot;
#endif

    public LibraryViewModel ViewModel { get; }

    public LibraryPage(LibraryViewModel viewModel, ExternalDocumentOpenService externalDocuments)
    {
        _externalDocuments = externalDocuments;
        ViewModel = viewModel;
        InitializeComponent();
        LibraryViewPicker.SelectedIndex = Math.Clamp(AppPreferences.Get(LibraryViewPreferenceKey, 2), 0, 7);
        BindingContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        var collectionPointer = new PointerGestureRecognizer();
        collectionPointer.PointerMoved += OnSelectionPointerMoved;
        collectionPointer.PointerReleased += OnSelectionPointerReleased;
        LibraryCollection.GestureRecognizers.Add(collectionPointer);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateAdaptiveLayout(Width, Height);
#if WINDOWS
        AttachSelectionKeyboard();
#endif

        if (BindingContext is not LibraryViewModel vm || AppLifetime.IsShuttingDown) return;
        if (_initialLoadStarted)
        {
            _restoringLibraryPosition = true;
            await vm.LoadLibraryCommand.ExecuteAsync(null);
            if (AppLifetime.IsShuttingDown) return;
            RestoreLibraryPosition();
            _externalDocuments.AttachShell(Shell.Current);
            return;
        }
        _initialLoadStarted = true;

        try
        {
            _restoringLibraryPosition = true;
            StartupLoadingStatus.Text = "Cargando biblioteca...";
            await vm.LoadLibraryCommand.ExecuteAsync(null);
            if (AppLifetime.IsShuttingDown) return;
            StartupLoadingStatus.Text = "Actualizando biblioteca...";
            if (!_externalDocuments.HasPendingRequests)
                await vm.SynchronizeLibraryAsync();
            if (AppLifetime.IsShuttingDown) return;
            RestoreLibraryPosition();
        }
        finally
        {
            if (!AppLifetime.IsShuttingDown)
            {
                StartupLoadingOverlay.IsVisible = false;
                ShowSyncNotification(vm.ImportSummary);
            }
        }
        if (!AppLifetime.IsShuttingDown)
            _externalDocuments.AttachShell(Shell.Current);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopTransientActivities();
    }

    public void PrepareForShutdown()
    {
        StopTransientActivities();
        ViewModel.PrepareForShutdown();
    }

    private void StopTransientActivities()
    {
        _tagDownloadCts?.Cancel();
        _tagPickerTcs?.TrySetResult(null);
        _nebulaDialogTcs?.TrySetResult(null);
        CancelPendingHold();
        _dragSelecting = false;
        StopSelectionAutoScroll();
#if WINDOWS
        DetachSelectionKeyboard();
#endif
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
            UpdateContentLayout(Math.Min(GetVisibleContentWidth(Width), LibraryCollection.Width));
    }

    private void OnHomeScrollSizeChanged(object? sender, EventArgs e)
    {
        if (HomeScroll.Width > 0) UpdateRecentCardWidth(HomeScroll.Width);
    }

    protected override bool OnBackButtonPressed()
    {
        if (ViewModel.IsSelectionMode)
        {
            ViewModel.ClearSelection();
            return true;
        }
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

    private void OnSelectableCardLoaded(object sender, EventArgs e)
    {
        if (sender is not Border card || _cardPointers.ContainsKey(card)) return;
        _visibleSelectableCards.Add(card);
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => OnCardPointerEntered(card);
        pointer.PointerExited += (_, _) => OnCardPointerExited(card);
        pointer.PointerPressed += (_, args) => OnCardPointerPressed(card, args);
        pointer.PointerMoved += (_, args) => OnSelectionPointerMoved(card, args);
        pointer.PointerReleased += OnSelectionPointerReleased;
        card.GestureRecognizers.Add(pointer);
        _cardPointers.Add(card, pointer);
    }

    private void OnSelectableCardUnloaded(object sender, EventArgs e)
    {
        if (sender is not Border card) return;
        _visibleSelectableCards.Remove(card);
        if (_cardPointers.Remove(card, out var pointer)) card.GestureRecognizers.Remove(pointer);
        if (ReferenceEquals(_pressedCard, card)) CancelPendingHold();
    }

    private void OnCardPointerEntered(Border card)
    {
#if WINDOWS
        if (card.BindingContext is LibraryDisplayItem item)
        {
            item.IsHovered = true;
            var index = ViewModel.DisplayItems.IndexOf(item);
            if (index >= 0) _keyboardIndex = index;
        }
#endif
    }

    private void OnCardPointerExited(Border card)
    {
#if WINDOWS
        if (card.BindingContext is LibraryDisplayItem item) item.IsHovered = false;
#endif
    }

    private void OnCardPointerPressed(Border card, PointerEventArgs e)
    {
        if (card.BindingContext is not LibraryDisplayItem item || !ViewModel.IsLibrarySelected) return;
        CancelPendingHold();
        _pressedCard = card;
        _pressPosition = e.GetPosition(card) ?? Point.Zero;
        _holdCts = new CancellationTokenSource();
        _ = BeginSelectionAfterHoldAsync(card, item, _holdCts.Token);
    }

    private async Task BeginSelectionAfterHoldAsync(Border card, LibraryDisplayItem item, CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(_pressedCard, card)
                    || !ReferenceEquals(card.BindingContext, item)) return;
                ViewModel.AddToSelection(item);
                _selectionAnchorIndex = ViewModel.DisplayItems.IndexOf(item);
                _keyboardIndex = _selectionAnchorIndex;
                _dragSelecting = true;
                _suppressNextTapItem = item;
                StartSelectionAutoScroll();
            });
        }
        catch (OperationCanceledException) { }
    }

    private void OnSelectionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragSelecting)
        {
            if (_pressedCard is { } card && e.GetPosition(card) is { } point
                && (Math.Abs(point.X - _pressPosition.X) > 12 || Math.Abs(point.Y - _pressPosition.Y) > 12))
                CancelPendingHold();
            return;
        }

        if (e.GetPosition(LibraryCollection) is { } collectionPoint)
        {
            _selectionPointerX = collectionPoint.X;
            var edge = Math.Min(72, LibraryCollection.Height * 0.15);
            _selectionScrollDirection = collectionPoint.Y < edge ? -1
                : collectionPoint.Y > LibraryCollection.Height - edge ? 1 : 0;
            if (_selectionScrollDirection == 0) _selectionScrollRow = -1;
        }

        foreach (var card in _visibleSelectableCards.ToArray())
        {
            if (card.BindingContext is not LibraryDisplayItem item || e.GetPosition(card) is not { } point) continue;
            if (point.X >= 0 && point.Y >= 0 && point.X <= card.Width && point.Y <= card.Height)
            {
                var index = ViewModel.DisplayItems.IndexOf(item);
                if (index >= 0 && index != _keyboardIndex)
                {
                    _keyboardIndex = index;
                    ViewModel.SelectRange(_selectionAnchorIndex >= 0 ? _selectionAnchorIndex : index, index);
                }
                break;
            }
        }
    }

    private void OnSelectionPointerReleased(object? sender, PointerEventArgs e)
    {
        CancelPendingHold();
        _dragSelecting = false;
        StopSelectionAutoScroll();
        var suppressedItem = _suppressNextTapItem;
        if (suppressedItem != null)
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
            {
                if (ReferenceEquals(_suppressNextTapItem, suppressedItem)) _suppressNextTapItem = null;
            });
    }

    private void CancelPendingHold()
    {
        _holdCts?.Cancel();
        _holdCts?.Dispose();
        _holdCts = null;
        _pressedCard = null;
    }

    private void StartSelectionAutoScroll()
    {
        _selectionScrollTimer ??= Dispatcher.CreateTimer();
        _selectionScrollTimer.Interval = TimeSpan.FromMilliseconds(110);
        _selectionScrollTimer.Tick -= OnSelectionScrollTick;
        _selectionScrollTimer.Tick += OnSelectionScrollTick;
        _selectionScrollTimer.Start();
    }

    private void StopSelectionAutoScroll()
    {
        _selectionScrollDirection = 0;
        _selectionScrollRow = -1;
        _selectionScrollTimer?.Stop();
    }

    private void OnSelectionScrollTick(object? sender, EventArgs e)
    {
        if (!_dragSelecting || _selectionScrollDirection == 0 || !ViewModel.IsLibrarySelected) return;
        var rowCount = _showingCardRows ? ViewModel.DisplayRows.Count : ViewModel.DisplayItems.Count;
        if (rowCount == 0) return;

        var visibleRow = _selectionScrollDirection > 0 ? _lastVisibleLibraryItem : _firstVisibleScrollRow;
        var nextRow = Math.Clamp((_selectionScrollRow >= 0 ? _selectionScrollRow : visibleRow)
            + _selectionScrollDirection, 0, rowCount - 1);
        if (nextRow == _selectionScrollRow || nextRow == visibleRow &&
            (nextRow == 0 || nextRow == rowCount - 1)) return;
        _selectionScrollRow = nextRow;
        LibraryCollection.ScrollTo(nextRow, position: _selectionScrollDirection > 0
            ? ScrollToPosition.End : ScrollToPosition.Start, animate: false);

        var columns = _showingCardRows ? Math.Max(1, _cardColumns) : 1;
        var column = _showingCardRows ? Math.Clamp((int)(_selectionPointerX / Math.Max(1, ViewModel.CardWidth + 10)), 0, columns - 1) : 0;
        var index = Math.Min(ViewModel.DisplayItems.Count - 1, nextRow * columns + column);
        if (index >= 0 && index != _keyboardIndex)
        {
            _keyboardIndex = index;
            ViewModel.SelectRange(_selectionAnchorIndex >= 0 ? _selectionAnchorIndex : index, index);
        }
    }

    private async void OnLibraryItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element { BindingContext: LibraryDisplayItem item }) return;
        if (ReferenceEquals(_suppressNextTapItem, item))
        {
            _suppressNextTapItem = null;
            return;
        }

        var index = ViewModel.DisplayItems.IndexOf(item);
        if (index >= 0) _keyboardIndex = index;
#if WINDOWS
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (shift && index >= 0)
        {
            ViewModel.SelectRange(_selectionAnchorIndex >= 0 ? _selectionAnchorIndex : index, index);
            return;
        }
        if (control)
        {
            if (index >= 0) _selectionAnchorIndex = index;
            ViewModel.ToggleSelection(item);
            return;
        }
#endif
        if (index >= 0 && !ViewModel.IsSelectionMode) _selectionAnchorIndex = index;
        await ViewModel.OpenItemCommand.ExecuteAsync(item);
    }

    private void OnSelectionIndicatorClicked(object sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: LibraryDisplayItem item }) return;
        var index = ViewModel.DisplayItems.IndexOf(item);
        if (index >= 0) _selectionAnchorIndex = _keyboardIndex = index;
        ViewModel.ToggleSelection(item);
    }

    private void OnSelectionCancelClicked(object sender, EventArgs e) => ViewModel.ClearSelection();

    private async void OnSelectedReadClicked(object sender, EventArgs e)
    {
        try { await ViewModel.SetSelectedStatusAsync(ReadingStatus.Completed); }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo completar la acción", ex.Message, "Cerrar"); }
    }

    private async void OnSelectedUnreadClicked(object sender, EventArgs e)
    {
        try { await ViewModel.SetSelectedStatusAsync(ReadingStatus.Unread); }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo completar la acción", ex.Message, "Cerrar"); }
    }

    private async void OnSelectedRemoveClicked(object sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Quitar documentos",
                $"Se quitarán {ViewModel.SelectedDocumentCount} documentos y su progreso. Los archivos originales se conservarán. ¿Continuar?",
                "Quitar", "Cancelar")) return;
        try { await ViewModel.RemoveSelectedAsync(); }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo completar la acción", ex.Message, "Cerrar"); }
    }

#if WINDOWS
    private void AttachSelectionKeyboard()
    {
        if ((Window?.Handler?.PlatformView ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView)
            is not Microsoft.UI.Xaml.Window nativeWindow || nativeWindow.Content is not Microsoft.UI.Xaml.UIElement root
            || ReferenceEquals(_selectionWindowRoot, root)) return;
        DetachSelectionKeyboard();
        _selectionWindowRoot = root;
        root.PreviewKeyDown += OnSelectionPreviewKeyDown;
    }

    private void DetachSelectionKeyboard()
    {
        if (_selectionWindowRoot == null) return;
        _selectionWindowRoot.PreviewKeyDown -= OnSelectionPreviewKeyDown;
        _selectionWindowRoot = null;
    }

    private void OnSelectionPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsLibrarySelected || LibrarySearchBar.IsFocused) return;
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (e.Key == Windows.System.VirtualKey.Escape && ViewModel.IsSelectionMode)
        {
            ViewModel.ClearSelection();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Windows.System.VirtualKey.A)
        {
            ViewModel.SelectAllVisible();
            e.Handled = true;
            return;
        }
        if (!shift || ViewModel.DisplayItems.Count == 0) return;
        var verticalStep = _showingCardRows ? Math.Max(1, _cardColumns) : 1;
        var step = e.Key switch
        {
            Windows.System.VirtualKey.Left => -1,
            Windows.System.VirtualKey.Right => 1,
            Windows.System.VirtualKey.Up => -verticalStep,
            Windows.System.VirtualKey.Down => verticalStep,
            _ => 0
        };
        if (step == 0) return;

        var current = _keyboardIndex >= 0 ? _keyboardIndex : 0;
        var target = Math.Clamp(current + step, 0, ViewModel.DisplayItems.Count - 1);
        if (_selectionAnchorIndex < 0) _selectionAnchorIndex = current;
        _keyboardIndex = target;
        ViewModel.SelectRange(_selectionAnchorIndex, target);
        LibraryCollection.ScrollTo(_showingCardRows ? target / Math.Max(1, _cardColumns) : target,
            position: ScrollToPosition.Center, animate: false);
        e.Handled = true;
    }
#endif

    private void UpdateAdaptiveLayout(double width, double height)
    {
        if (width <= 0 || height <= 0 || LibraryCollection == null)
        {
            return;
        }

        var isCompactHeight = height < 520;
        var isWide = width >= 1000;
        var isCompact = width < 760;
        CompactFilterPanel.MaximumHeightRequest = Math.Clamp(height * 0.35, 100, 300);

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
            Dispatcher.Dispatch(() =>
            {
                if (!AppLifetime.IsShuttingDown) LibraryRootGrid.InvalidateMeasure();
            });
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
        if (contentWidth <= 0 || LibraryCollection == null) return;

        var mode = Math.Max(0, LibraryViewPicker.SelectedIndex);
        DetailsColumnHeader.IsVisible = mode == 5;
        var isCard = mode <= 2;
        var isCompactIcon = mode is 3 or 6;
        var templateKey = mode switch
        {
            <= 2 => "CardRowTemplate",
            3 or 6 => "CompactRowTemplate",
            4 => "ListItemTemplate",
            5 => "DetailsItemTemplate",
            7 => "ContentItemTemplate",
            _ => "CardRowTemplate"
        };
        var template = LibraryCollection.Resources[templateKey] as DataTemplate;
        if (template != null && !ReferenceEquals(LibraryCollection.ItemTemplate, template))
            LibraryCollection.ItemTemplate = template;

        var showRows = isCard || isCompactIcon;
        if (_showingCardRows != showRows)
        {
            _showingCardRows = showRows;
            LibraryCollection.SetBinding(ItemsView.ItemsSourceProperty,
                new Binding(showRows ? nameof(LibraryViewModel.DisplayRows) : nameof(LibraryViewModel.DisplayItems)));
        }

        // Las filas son virtualizadas; cada tarjeta tiene un ancho acotado y queda alineada a la izquierda.
        // Se reservan 18 DIP a la derecha para que la barra de desplazamiento vertical no pise las tarjetas.
        const double hSpacing = 10;
        const double scrollbarReserve = 18;
        var gridWidth = Math.Max(0, Math.Min(contentWidth, LibraryCollection.Width > 0
            ? LibraryCollection.Width : contentWidth) - scrollbarReserve);
        var (minimumItemWidth, maximumItemWidth) = mode switch
        {
            0 => (240.0, 280.0),
            1 => (210.0, 240.0),
            2 => (180.0, 210.0),
            3 => (120.0, 160.0),
            6 => (96.0, 136.0),
            _ => (gridWidth, gridWidth)
        };
        var metrics = ResponsiveLayout.GetCardGridMetrics(gridWidth, minimumItemWidth,
            maximumItemWidth, hSpacing);
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"Biblioteca: vista={mode}, ancho={gridWidth:F0} DIP, " +
            $"columnas={metrics.Columns}, tarjeta={metrics.CardWidth:F0} DIP");
#endif
        ViewModel.CardWidth = Math.Floor(metrics.CardWidth);
        var columnsChanged = showRows && _cardColumns != metrics.Columns;
        if (columnsChanged)
        {
            _firstVisibleLibraryItem = _firstVisibleLibraryItem * _cardColumns / metrics.Columns;
            _cardColumns = metrics.Columns;
            _restoringLibraryPosition = true;
        }
        ViewModel.UpdateCardColumns(showRows ? metrics.Columns : 1);
        if (columnsChanged && ViewModel.IsLibrarySelected)
            RestoreLibraryPosition();

        if (LibraryCollection.ItemsLayout is LinearItemsLayout linearLayout)
            linearLayout.ItemSpacing = hSpacing;

        // Las tarjetas mantienen sus acciones; las vistas compactas abren al tocar el elemento.
        var approxColWidth = metrics.CardWidth;
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
        if (LibraryViewPicker.SelectedIndex < 0 || LibraryCollection == null) return;
        AppPreferences.Set(LibraryViewPreferenceKey, LibraryViewPicker.SelectedIndex);
        _firstVisibleLibraryItem = 0;
        UpdateContentLayout(GetVisibleContentWidth(Width));
        if (ViewModel.IsLibrarySelected) LibraryCollection.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
    }

    private void UpdateRecentCardWidth(double contentWidth)
    {
        // ScrollView puede reservar una barra vertical dentro de su Width. Dejar aire
        // adicional evita que el borde derecho de la última tarjeta quede recortado.
        var viewportWidth = HomeScroll.Width > 0 ? Math.Min(contentWidth, HomeScroll.Width) : contentWidth;
        ViewModel.UpdateRecentCardLayout(ResponsiveLayout.GetRecentCardGridMetrics(viewportWidth));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryViewModel.SearchText) or
            nameof(LibraryViewModel.SelectedFormatIndex) or
            nameof(LibraryViewModel.SelectedStatusIndex) or
            nameof(LibraryViewModel.SelectedTagFilter) or
            nameof(LibraryViewModel.SelectedGroupingIndex) or
            nameof(LibraryViewModel.ActiveGroupTitle))
        {
            _firstVisibleLibraryItem = 0;
            _restoringLibraryPosition = true;
            RestoreLibraryPosition();
        }
        else if (e.PropertyName == nameof(LibraryViewModel.IsHomeSelected))
        {
            _restoringLibraryPosition = true;
            Dispatcher.Dispatch(() =>
            {
                if (!AppLifetime.IsShuttingDown) UpdateContentLayout(GetVisibleContentWidth(Width));
            });
            RestoreLibraryPosition();
        }
        else if (e.PropertyName == nameof(LibraryViewModel.IsSelectionMode) && !ViewModel.IsSelectionMode)
        {
            _selectionAnchorIndex = -1;
        }
    }

    private void OnLibraryScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (e.FirstVisibleItemIndex >= 0) _firstVisibleScrollRow = e.FirstVisibleItemIndex;
        if (e.LastVisibleItemIndex >= 0) _lastVisibleLibraryItem = e.LastVisibleItemIndex;
        if (!_restoringLibraryPosition && ViewModel.IsLibrarySelected && e.FirstVisibleItemIndex >= 0)
            _firstVisibleLibraryItem = e.FirstVisibleItemIndex;
    }

    private void RestoreLibraryPosition()
    {
        if (!ViewModel.IsLibrarySelected) return;
        var itemCount = _showingCardRows ? ViewModel.DisplayRows.Count : ViewModel.DisplayItems.Count;
        if (itemCount == 0)
        {
            _restoringLibraryPosition = false;
            return;
        }

        var index = Math.Min(_firstVisibleLibraryItem, itemCount - 1);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () =>
        {
            if (AppLifetime.IsShuttingDown) return;
            var visibleCount = _showingCardRows ? ViewModel.DisplayRows.Count : ViewModel.DisplayItems.Count;
            if (ViewModel.IsLibrarySelected && visibleCount > index)
                LibraryCollection.ScrollTo(index, position: ScrollToPosition.Start, animate: false);

            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
                _restoringLibraryPosition = false);
        });
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            // Antes se ignoraba el clic sin aviso mientras la biblioteca se cargaba o revisaba.
            ShowSyncNotification(ViewModel.IsImporting
                ? "Espera a que termine la revisión de la biblioteca o cancélala para añadir documentos."
                : "Espera a que termine la operación en curso para añadir documentos.");
            return;
        }

        var action = await DisplayActionSheetAsync("Añadir a la biblioteca", "Cancelar", null,
            "Abrir archivo", "Escanear carpeta");
        switch (action)
        {
            case "Abrir archivo":
                await ViewModel.OpenFileCommand.ExecuteAsync(null);
                ShowSyncNotification(ViewModel.StatusMessage);
                break;
            case "Escanear carpeta":
                await ViewModel.ImportFolderCommand.ExecuteAsync(null);
                ShowSyncNotification(ViewModel.StatusMessage);
                break;
        }
    }

    private async void OnRefreshLibraryClicked(object sender, EventArgs e)
    {
        if (ViewModel.IsBusy) return;
        await ViewModel.SynchronizeLibraryAsync();
        ShowSyncNotification(ViewModel.ImportSummary);
    }

    private void ShowSyncNotification(string message)
    {
        if (AppLifetime.IsShuttingDown || string.IsNullOrWhiteSpace(message)) return;

        var version = ++_notificationVersion;
        LibrarySyncNotificationText.Text = message;
        LibrarySyncNotification.IsVisible = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(5), () =>
        {
            if (!AppLifetime.IsShuttingDown && version == _notificationVersion)
                LibrarySyncNotification.IsVisible = false;
        });
    }

    private async void OnManagementClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new ManagementPage(ViewModel));
    }

    private async void OnGroupSeriesClicked(object sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: LibraryDisplayItem { CanEditSeriesGroup: true } group }) return;
        var name = await DisplayPromptAsync("Editar serie del grupo",
            "Nuevo nombre para todos los documentos del grupo (vacío para quitar la serie).",
            initialValue: group.Title, maxLength: 255);
        if (name == null) return;
        try { await ViewModel.EditGroupSeriesAsync(group, name); }
        catch (Exception ex) { await DisplayAlertAsync("No se pudo editar la serie", ex.Message, "Cerrar"); }
    }

    private async void OnBookActionsClicked(object sender, EventArgs e)
    {
        if (ViewModel.IsDownloadingTags) return;
        if (sender is not Button { BindingContext: LibraryDisplayItem { Book: { } book } }) return;
        try
        {
            var action = await DisplayActionSheetAsync(book.Title, "Cancelar", null,
                "Editar título y serie", "Editar etiquetas", "Descargar etiquetas", "Marcar como leído", "Marcar como no leído",
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
                case "Editar etiquetas":
                    var tags = await DisplayPromptAsync("Editar etiquetas",
                        "Separa las etiquetas con comas o punto y coma. Déjalo vacío para quitarlas.",
                        initialValue: book.Tags ?? "", maxLength: 1000);
                    if (tags == null) return;
                    await ViewModel.EditTagsAsync(book, tags);
                    break;
                case "Descargar etiquetas":
                    var seriesBucket = LibraryBrowseService.FilterGroups(ViewModel.Books, grouping: LibraryGrouping.Series)
                        .FirstOrDefault(bucket => bucket.IsGroup && bucket.Items.Any(item => item.Id == book.Id));
                    if (seriesBucket is { Items.Count: > 1 })
                        await DownloadTagsAsync(seriesBucket.Items, seriesBucket.DisplayName);
                    else
                        await DownloadTagsAsync([book], book.Title);
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

    private static string TagSourceName(OnlineTagSource source) => source switch
    {
        OnlineTagSource.ComicVine => "Comic Vine",
        OnlineTagSource.OpenLibrary => "Open Library",
        OnlineTagSource.GoogleBooks => "Google Books",
        _ => source.ToString()
    };

    private static (Color Background, Color Border, Color Text) GetSourceBadgeColors(OnlineTagSource source) => source switch
    {
        OnlineTagSource.AniList => (Color.FromArgb("#3B1B68"), Color.FromArgb("#B026FF"), Color.FromArgb("#F2D9FF")),
        OnlineTagSource.OpenLibrary => (Color.FromArgb("#16324F"), Color.FromArgb("#38BDF8"), Color.FromArgb("#E0F2FE")),
        OnlineTagSource.Crossref => (Color.FromArgb("#1A3A32"), Color.FromArgb("#34D399"), Color.FromArgb("#D1FAE5")),
        OnlineTagSource.ComicVine => (Color.FromArgb("#4A213B"), Color.FromArgb("#F472B6"), Color.FromArgb("#FCE7F3")),
        OnlineTagSource.Metron => (Color.FromArgb("#3F2A14"), Color.FromArgb("#FBBF24"), Color.FromArgb("#FEF3C7")),
        OnlineTagSource.GoogleBooks => (Color.FromArgb("#1E294B"), Color.FromArgb("#818CF8"), Color.FromArgb("#E0E7FF")),
        _ => (Color.FromArgb("#272142"), Color.FromArgb("#9D4EDD"), Color.FromArgb("#F5F3FF"))
    };

    private void OnTagPickerCancelClicked(object? sender, EventArgs e)
    {
        _tagPickerStopRequested = false;
        _tagPickerTcs?.TrySetResult(null);
    }

    private void OnTagPickerStopClicked(object? sender, EventArgs e)
    {
        _tagPickerStopRequested = true;
        _tagPickerTcs?.TrySetResult(null);
    }

    private async Task<OnlineTagSuggestion?> ShowStyledTagPickerAsync(
        string title,
        IReadOnlyList<(OnlineTagSource Source, OnlineTagSuggestion Suggestion)> suggestions,
        string cancelText,
        string? stopText,
        CancellationToken ct)
    {
        _tagPickerStopRequested = false;
        var tcs = new TaskCompletionSource<OnlineTagSuggestion?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tagPickerTcs = tcs;

        var surface2 = Color.FromArgb("#1C1830");
        var surface3 = Color.FromArgb("#272142");
        var subtleBorder = Color.FromArgb("#2A2346");
        var neonBright = Color.FromArgb("#D884FF");
        var textPrimary = Color.FromArgb("#F5F3FF");
        var textSecondary = Color.FromArgb("#A99FC8");

        TagPickerTitleLabel.Text = title;
        TagPickerSubtitleLabel.Text = $"{suggestions.Count} resultado(s) encontrados en todas las fuentes disponibles. Haz clic en uno para aplicarlo.";
        TagPickerCancelButton.Text = cancelText;
        TagPickerStopButton.IsVisible = !string.IsNullOrWhiteSpace(stopText);
        if (!string.IsNullOrWhiteSpace(stopText))
            TagPickerStopButton.Text = stopText;

        TagPickerItemsLayout.Children.Clear();
        for (var i = 0; i < suggestions.Count; i++)
        {
            var entry = suggestions[i];
            var (badgeBg, badgeBorder, badgeText) = GetSourceBadgeColors(entry.Source);

            var card = new Border
            {
                BackgroundColor = surface2,
                Stroke = subtleBorder,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Padding = new Thickness(14, 11)
            };

            var topRow = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                ],
                ColumnSpacing = 10
            };

            var badge = new Border
            {
                BackgroundColor = badgeBg,
                Stroke = badgeBorder,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(8, 2),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = TagSourceName(entry.Source),
                    TextColor = badgeText,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold
                }
            };

            var titleLabel = new Label
            {
                Text = entry.Suggestion.Title,
                TextColor = textPrimary,
                FontFamily = "OpenSansSemibold",
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap
            };

            topRow.Add(badge, 0, 0);
            topRow.Add(titleLabel, 1, 0);

            var contentStack = new VerticalStackLayout { Spacing = 5 };
            contentStack.Children.Add(topRow);

            if (!string.IsNullOrWhiteSpace(entry.Suggestion.Author))
            {
                contentStack.Children.Add(new Label
                {
                    Text = entry.Suggestion.Author,
                    TextColor = textSecondary,
                    FontSize = 12,
                    LineBreakMode = LineBreakMode.TailTruncation
                });
            }

            if (entry.Suggestion.Tags.Count > 0)
            {
                var previewTags = string.Join(" · ", entry.Suggestion.Tags.Take(8));
                if (entry.Suggestion.Tags.Count > 8)
                    previewTags += $" (+{entry.Suggestion.Tags.Count - 8})";

                contentStack.Children.Add(new Label
                {
                    Text = previewTags,
                    TextColor = neonBright,
                    FontSize = 11,
                    MaxLines = 2,
                    LineBreakMode = LineBreakMode.TailTruncation
                });
            }

            card.Content = contentStack;

            var tap = new TapGestureRecognizer();
            var captured = entry.Suggestion;
            tap.Tapped += (_, _) => tcs.TrySetResult(captured);
            card.GestureRecognizers.Add(tap);

            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) =>
            {
                card.BackgroundColor = surface3;
                card.Stroke = neonBright;
            };
            pointer.PointerExited += (_, _) =>
            {
                card.BackgroundColor = surface2;
                card.Stroke = subtleBorder;
            };
            card.GestureRecognizers.Add(pointer);

            TagPickerItemsLayout.Children.Add(card);
        }

        TagPickerOverlay.IsVisible = true;
        try
        {
            await TagPickerScrollView.ScrollToAsync(0, 0, false);
            await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }
        finally
        {
            TagPickerOverlay.IsVisible = false;
            TagPickerItemsLayout.Children.Clear();
            if (ReferenceEquals(_tagPickerTcs, tcs))
                _tagPickerTcs = null;
        }
    }

    private void OnStopTagDownloadClicked(object sender, EventArgs e) => _tagDownloadCts?.Cancel();

    private async void OnSelectedDownloadTagsClicked(object sender, EventArgs e)
    {
        if (ViewModel.IsDownloadingTags) return;
        var books = ViewModel.GetSelectedBooks();
        if (books.Count == 0) return;

        // Agrupar los documentos seleccionados por serie/saga para preguntar una sola vez por manga
        // y aplicar las etiquetas elegidas a todos sus tomos/volúmenes automáticamente.
        var groups = LibraryBrowseService.FilterGroups(books, grouping: LibraryGrouping.Series);
        if (groups.Count == 0) return;

        using var cts = new CancellationTokenSource();
        _tagDownloadCts = cts;
        ViewModel.IsDownloadingTags = true;
        var saved = 0;
        var skipped = 0;
        var failures = new List<string>();
        var processedBooks = 0;
        try
        {
            var commonSources = (await ViewModel.GetAvailableTagSourcesAsync(cts.Token)).ToArray();
            if (commonSources.Length == 0)
            {
                await DisplayAlertAsync("Sin fuentes disponibles", "Revisa las fuentes de etiquetas en Configuración.", "Aceptar");
                return;
            }

            for (var i = 0; i < groups.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var group = groups[i];
                var groupLabel = group.Items.Count > 1
                    ? $"{group.DisplayName} ({group.Items.Count} tomos)"
                    : group.DisplayName;
                ViewModel.TagDownloadProgress = $"Serie/documento {i + 1} de {groups.Count}: {groupLabel}";
                try
                {
                    if (await DownloadTagsAsync(group.Items, group.DisplayName, commonSources, cts.Token,
                        $"{i + 1}/{groups.Count} · {groupLabel}"))
                        saved += group.Items.Count;
                    else
                        skipped += group.Items.Count;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
                catch (Exception ex) { failures.Add($"{group.DisplayName}: {ex.Message}"); }
                processedBooks += group.Items.Count;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) { failures.Add(ex.Message); }
        finally
        {
            _tagDownloadCts = null;
            ViewModel.IsDownloadingTags = false;
            ViewModel.TagDownloadProgress = string.Empty;
        }
        await ShowTagDownloadSummaryAsync(cts.IsCancellationRequested, saved, skipped, books.Count - processedBooks, failures);
    }

    private void OnNebulaDialogBackdropTapped(object? sender, EventArgs e)
    {
        _nebulaDialogTcs?.TrySetResult(null);
    }

    private void OnNebulaDialogCancelClicked(object? sender, EventArgs e)
    {
        _nebulaDialogTcs?.TrySetResult(null);
    }

    private void OnNebulaDialogAcceptClicked(object? sender, EventArgs e)
    {
        if (NebulaDialogInputBorder.IsVisible)
            _nebulaDialogTcs?.TrySetResult(NebulaDialogEntry.Text ?? string.Empty);
        else
            _nebulaDialogTcs?.TrySetResult("__ACCEPT__");
    }

    private new async Task DisplayAlertAsync(string title, string message, string cancel)
    {
        await ShowNebulaDialogCoreAsync("NEBULA READER", title, message, acceptText: cancel, cancelText: null);
    }

    private new async Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        var res = await ShowNebulaDialogCoreAsync("CONFIRMACIÓN", title, message, acceptText: accept, cancelText: cancel);
        return res == "__ACCEPT__";
    }

    private new async Task<string?> DisplayActionSheetAsync(string title, string? cancel, string? destruction, params string[] buttons)
    {
        var surface2 = Color.FromArgb("#1C1830");
        var surface3 = Color.FromArgb("#272142");
        var subtleBorder = Color.FromArgb("#2A2346");
        var neonBright = Color.FromArgb("#D884FF");
        var textPrimary = Color.FromArgb("#F5F3FF");

        var options = new List<View>();
        if (!string.IsNullOrWhiteSpace(destruction))
        {
            options.Add(CreateDialogOptionCard(destruction, Color.FromArgb("#3B1629"), Color.FromArgb("#F43F5E"), Color.FromArgb("#FFE4E6"), surface3, neonBright));
        }
        foreach (var btn in buttons)
        {
            if (string.IsNullOrWhiteSpace(btn)) continue;
            var isDestructive = btn.StartsWith("Quitar", StringComparison.OrdinalIgnoreCase);
            options.Add(CreateDialogOptionCard(
                btn,
                isDestructive ? Color.FromArgb("#2E1424") : surface2,
                isDestructive ? Color.FromArgb("#9F1239") : subtleBorder,
                isDestructive ? Color.FromArgb("#FDA4AF") : textPrimary,
                surface3,
                isDestructive ? Color.FromArgb("#FB7185") : neonBright));
        }

        return await ShowNebulaDialogCoreAsync("ACCIONES", title, null, acceptText: null, cancelText: cancel ?? "Cancelar", customViews: options);
    }

    private Border CreateDialogOptionCard(string label, Color bg, Color border, Color textColor, Color hoverBg, Color hoverBorder)
    {
        var card = new Border
        {
            BackgroundColor = bg,
            Stroke = border,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14, 11),
            Content = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                Children =
                {
                    new Label
                    {
                        Text = label,
                        TextColor = textColor,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 13.5,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var arrow = new Label
        {
            Text = "›",
            TextColor = hoverBorder,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        ((Grid)card.Content).Add(arrow, 1, 0);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _nebulaDialogTcs?.TrySetResult(label);
        card.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            card.BackgroundColor = hoverBg;
            card.Stroke = hoverBorder;
        };
        pointer.PointerExited += (_, _) =>
        {
            card.BackgroundColor = bg;
            card.Stroke = border;
        };
        card.GestureRecognizers.Add(pointer);
        return card;
    }

    private new async Task<string?> DisplayPromptAsync(
        string title,
        string message,
        string accept = "Guardar",
        string cancel = "Cancelar",
        string? placeholder = null,
        int maxLength = -1,
        Keyboard? keyboard = null,
        string initialValue = "")
    {
        return await ShowNebulaDialogCoreAsync(
            "EDITAR METADATOS",
            title,
            message,
            acceptText: accept,
            cancelText: cancel,
            isInputPrompt: true,
            initialInput: initialValue,
            placeholder: placeholder,
            maxLength: maxLength);
    }

    private async Task ShowTagDownloadSummaryAsync(bool stopped, int saved, int skipped, int pending, IReadOnlyList<string> failures)
    {
        var statsGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            ],
            ColumnSpacing = 8
        };

        static Border CreateStatChip(string label, int count, string bgHex, string borderHex, string valueHex) => new()
        {
            BackgroundColor = Color.FromArgb(bgHex),
            Stroke = Color.FromArgb(borderHex),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(8, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = count.ToString(),
                        TextColor = Color.FromArgb(valueHex),
                        FontFamily = "OpenSansSemibold",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = label,
                        TextColor = Color.FromArgb("#A99FC8"),
                        FontSize = 11,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        statsGrid.Add(CreateStatChip("Guardados", saved, "#21163E", "#B026FF", "#D884FF"), 0, 0);
        statsGrid.Add(CreateStatChip("Omitidos", skipped, "#1C1830", "#3B3359", "#E9E4FA"), 1, 0);
        statsGrid.Add(CreateStatChip("Errores", failures.Count, failures.Count > 0 ? "#381526" : "#1C1830", failures.Count > 0 ? "#F43F5E" : "#2A2346", failures.Count > 0 ? "#FDA4AF" : "#9F94C4"), 2, 0);
        statsGrid.Add(CreateStatChip("Pendientes", pending, "#1C1830", "#2A2346", "#9F94C4"), 3, 0);

        var views = new List<View> { statsGrid };
        foreach (var failure in failures)
        {
            views.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#2B1321"),
                Stroke = Color.FromArgb("#9F1239"),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12, 8),
                Content = new Label
                {
                    Text = failure,
                    TextColor = Color.FromArgb("#FECDD3"),
                    FontSize = 12,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            });
        }

        await ShowNebulaDialogCoreAsync(
            "RESUMEN DE METADATOS",
            stopped ? "Descarga detenida" : "Descarga de etiquetas",
            null,
            acceptText: "Aceptar",
            cancelText: null,
            customViews: views);
    }

    private async Task<string?> ShowNebulaDialogCoreAsync(
        string eyebrow,
        string title,
        string? message,
        string? acceptText,
        string? cancelText,
        bool isInputPrompt = false,
        string initialInput = "",
        string? placeholder = null,
        int maxLength = -1,
        IReadOnlyList<View>? customViews = null)
    {
        _nebulaDialogTcs?.TrySetResult(null);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _nebulaDialogTcs = tcs;

        NebulaDialogEyebrowLabel.Text = eyebrow;
        NebulaDialogTitleLabel.Text = title;
        NebulaDialogMessageLabel.Text = message ?? string.Empty;
        NebulaDialogMessageLabel.IsVisible = !string.IsNullOrWhiteSpace(message);

        NebulaDialogInputBorder.IsVisible = isInputPrompt;
        if (isInputPrompt)
        {
            NebulaDialogEntry.MaxLength = maxLength > 0 ? maxLength : int.MaxValue;
            NebulaDialogEntry.Placeholder = placeholder ?? string.Empty;
            NebulaDialogEntry.Text = initialInput;
        }

        NebulaDialogCustomContent.Children.Clear();
        if (customViews != null)
        {
            foreach (var v in customViews)
                NebulaDialogCustomContent.Children.Add(v);
        }

        NebulaDialogCancelButton.IsVisible = !string.IsNullOrWhiteSpace(cancelText);
        if (!string.IsNullOrWhiteSpace(cancelText))
            NebulaDialogCancelButton.Text = cancelText;

        NebulaDialogAcceptButton.IsVisible = !string.IsNullOrWhiteSpace(acceptText);
        if (!string.IsNullOrWhiteSpace(acceptText))
            NebulaDialogAcceptButton.Text = acceptText;

        NebulaDialogOverlay.IsVisible = true;
        try
        {
            await NebulaDialogScrollView.ScrollToAsync(0, 0, false);
            if (isInputPrompt)
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
                {
                    if (NebulaDialogOverlay.IsVisible)
                    {
                        NebulaDialogEntry.Focus();
                        NebulaDialogEntry.CursorPosition = 0;
                        NebulaDialogEntry.SelectionLength = (NebulaDialogEntry.Text ?? string.Empty).Length;
                    }
                });
            }
            return await tcs.Task;
        }
        finally
        {
            NebulaDialogOverlay.IsVisible = false;
            NebulaDialogCustomContent.Children.Clear();
            if (ReferenceEquals(_nebulaDialogTcs, tcs))
                _nebulaDialogTcs = null;
        }
    }

    private async Task<bool> DownloadTagsAsync(IReadOnlyList<BookItem> targetBooks, string displayTitle,
        IReadOnlyList<OnlineTagSource>? availableSources = null, CancellationToken ct = default, string? batchContext = null)
    {
        if (targetBooks.Count == 0) return false;
        var targetLabel = targetBooks.Count > 1
            ? $"{displayTitle} ({targetBooks.Count} tomos)"
            : displayTitle;
        var cancel = batchContext == null ? "Cancelar" : (targetBooks.Count > 1 ? "Omitir serie" : "Omitir documento");
        var stop = batchContext == null ? null : "Detener descarga";

        var sources = availableSources ?? await ViewModel.GetAvailableTagSourcesAsync(ct);
        if (sources.Count == 0)
        {
            await DisplayAlertAsync("Sin fuentes disponibles", "Revisa las fuentes de etiquetas en Configuración.", "Aceptar");
            return false;
        }

        var firstBook = targetBooks[0];
        var probeBook = targetBooks.Count > 1 && string.IsNullOrWhiteSpace(firstBook.SeriesName)
            ? new BookItem
            {
                Id = firstBook.Id,
                Title = firstBook.Title,
                SeriesName = displayTitle,
                Format = firstBook.Format
            }
            : firstBook;

        var searchTasks = sources.Select(async source =>
        {
            try
            {
                var items = await ViewModel.SearchTagsAsync(probeBook, source, ct);
                return (Source: source, Items: items, Error: (Exception?)null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (Source: source, Items: (IReadOnlyList<OnlineTagSuggestion>)[], Error: ex);
            }
        }).ToArray();

        var searchResults = await Task.WhenAll(searchTasks);
        ct.ThrowIfCancellationRequested();

        var suggestions = searchResults
            .SelectMany(result => result.Items.Select(item => (result.Source, Suggestion: item)))
            .ToList();

        if (suggestions.Count == 0)
        {
            var firstError = searchResults.Select(r => r.Error).FirstOrDefault(e => e != null);
            if (firstError != null && searchResults.All(r => r.Error != null))
                throw firstError;

            var sourceNames = string.Join(", ", sources.Select(TagSourceName));
            await DisplayAlertAsync("Sin resultados", $"No se encontraron etiquetas para «{displayTitle}» en las fuentes disponibles ({sourceNames}).", "Aceptar");
            return false;
        }

        var suggestion = await ShowStyledTagPickerAsync(
            batchContext ?? targetLabel,
            suggestions,
            cancel,
            stop,
            ct);
        if (_tagPickerStopRequested && stop != null) _tagDownloadCts?.Cancel();
        ct.ThrowIfCancellationRequested();
        if (suggestion == null) return false;

        // Traducir/localizar las etiquetas sugeridas una sola vez para toda la serie
        // y combinarlas con las etiquetas previas de cada tomo.
        var localizedSuggestionTags = await ViewModel.LocalizeTagsAsync(suggestion.Tags, ct);
        ct.ThrowIfCancellationRequested();

        var chosenTitle = suggestion.Title.Trim();
        var cleanSeriesName = LibraryTitlePresentation.Split(chosenTitle).Name;
        if (string.IsNullOrWhiteSpace(cleanSeriesName))
            cleanSeriesName = chosenTitle;
        if (cleanSeriesName.Length > 255)
            cleanSeriesName = cleanSeriesName[..255].Trim();

        foreach (var book in targetBooks)
        {
            ct.ThrowIfCancellationRequested();
            var existingTags = (book.Tags ?? string.Empty)
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var mergedTags = existingTags
                .Concat(localizedSuggestionTags)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32);

            var boundedTags = new List<string>();
            var totalLength = 0;
            foreach (var tag in mergedTags)
            {
                var nextLength = boundedTags.Count == 0 ? tag.Length : totalLength + 2 + tag.Length;
                if (nextLength > 1000) break;
                boundedTags.Add(tag);
                totalLength = nextLength;
            }
            var proposedTags = string.Join(", ", boundedTags);

            var hasVolumeNumber = LibraryTitlePresentation.Split(book.Title).Number != null;
            var newTitle = LibraryTitlePresentation.ReplaceName(book.Title, chosenTitle);
            var newSeries = (targetBooks.Count > 1 || hasVolumeNumber || !string.IsNullOrWhiteSpace(book.SeriesName))
                ? cleanSeriesName
                : book.SeriesName;

            await ViewModel.EditDetailsAsync(book, newTitle, newSeries);
            await ViewModel.EditTagsAsync(book, proposedTags);
        }

        if (batchContext == null)
        {
            ShowSyncNotification(targetBooks.Count > 1
                ? $"Título y etiquetas aplicados a «{cleanSeriesName}» ({targetBooks.Count} tomos)."
                : $"Título y etiquetas aplicados a «{chosenTitle}».");
        }
        return true;
    }
}
