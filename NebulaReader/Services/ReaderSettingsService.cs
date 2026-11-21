using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Almacena y notifica las preferencias del visor usando AppPreferences.
/// </summary>
public sealed class ReaderSettingsService : IReaderSettingsService
{
    private const string NightModeKey = "reader_night_mode";
    private const string VisualFilterKey = "reader_visual_filter";
    private const string MangaRtlKey = "reader_rtl_manga";
    private const string VerticalScrollKey = "reader_vertical_scroll";
    private const string DoublePageKey = "reader_double_page";
    private const string LayoutModeKey = "reader_layout_mode";
    private const string PageFitModeKey = "reader_page_fit_mode";

    private readonly WeakEventManager _settingsChangedWeakEvent = new();

    public event EventHandler? SettingsChanged
    {
        add => _settingsChangedWeakEvent.AddEventHandler(value);
        remove => _settingsChangedWeakEvent.RemoveEventHandler(value);
    }

    public bool NightReadingModeEnabled
    {
        get => AppPreferences.Get(NightModeKey, false);
        set
        {
            if (NightReadingModeEnabled == value) return;
            AppPreferences.Set(NightModeKey, value);
            if (value && VisualFilter == ReaderVisualFilter.None)
            {
                AppPreferences.Set(VisualFilterKey, (int)ReaderVisualFilter.NightInvert);
            }
            else if (!value && VisualFilter == ReaderVisualFilter.NightInvert)
            {
                AppPreferences.Set(VisualFilterKey, (int)ReaderVisualFilter.None);
            }
            NotifyChanged();
        }
    }

    public ReaderVisualFilter VisualFilter
    {
        get
        {
            var defaultFilter = NightReadingModeEnabled ? ReaderVisualFilter.NightInvert : ReaderVisualFilter.None;
            var raw = AppPreferences.Get(VisualFilterKey, (int)defaultFilter);
            return Enum.IsDefined(typeof(ReaderVisualFilter), raw) ? (ReaderVisualFilter)raw : defaultFilter;
        }
        set
        {
            if (VisualFilter == value) return;
            AppPreferences.Set(VisualFilterKey, (int)value);
            AppPreferences.Set(NightModeKey, value != ReaderVisualFilter.None);
            NotifyChanged();
        }
    }

    public bool RightToLeftMangaMode
    {
        get => AppPreferences.Get(MangaRtlKey, false);
        set
        {
            if (RightToLeftMangaMode == value) return;
            AppPreferences.Set(MangaRtlKey, value);
            NotifyChanged();
        }
    }

    public bool ContinuousVerticalScroll
    {
        get => LayoutMode == ReaderLayoutMode.ContinuousVertical;
        set
        {
            if (ContinuousVerticalScroll == value) return;
            AppPreferences.Set(VerticalScrollKey, value);
            var nextMode = value
                ? ReaderLayoutMode.ContinuousVertical
                : (DoublePageSpreadEnabled ? ReaderLayoutMode.DoublePage : ReaderLayoutMode.SinglePage);
            AppPreferences.Set(LayoutModeKey, (int)nextMode);
            NotifyChanged();
        }
    }

    public bool DoublePageSpreadEnabled
    {
        get => AppPreferences.Get(DoublePageKey, true);
        set
        {
            if (DoublePageSpreadEnabled == value && LayoutMode != ReaderLayoutMode.ContinuousVertical) return;
            AppPreferences.Set(DoublePageKey, value);
            if (!ContinuousVerticalScroll)
            {
                AppPreferences.Set(
                    LayoutModeKey,
                    (int)(value ? ReaderLayoutMode.DoublePage : ReaderLayoutMode.SinglePage));
            }
            NotifyChanged();
        }
    }

    public ReaderLayoutMode LayoutMode
    {
        get
        {
            if (AppPreferences.ContainsKey(LayoutModeKey))
            {
                var raw = AppPreferences.Get(LayoutModeKey, (int)ReaderLayoutMode.DoublePage);
                if (Enum.IsDefined(typeof(ReaderLayoutMode), raw))
                {
                    return (ReaderLayoutMode)raw;
                }
            }

            if (AppPreferences.Get(VerticalScrollKey, false))
            {
                return ReaderLayoutMode.ContinuousVertical;
            }

            return AppPreferences.Get(DoublePageKey, true)
                ? ReaderLayoutMode.DoublePage
                : ReaderLayoutMode.SinglePage;
        }
        set
        {
            if (LayoutMode == value && AppPreferences.ContainsKey(LayoutModeKey)) return;
            AppPreferences.Set(LayoutModeKey, (int)value);
            AppPreferences.Set(VerticalScrollKey, value == ReaderLayoutMode.ContinuousVertical);
            if (value == ReaderLayoutMode.SinglePage)
            {
                AppPreferences.Set(DoublePageKey, false);
            }
            else if (value == ReaderLayoutMode.DoublePage)
            {
                AppPreferences.Set(DoublePageKey, true);
            }
            NotifyChanged();
        }
    }

    public PageFitMode PageFitMode
    {
        get
        {
            var raw = AppPreferences.Get(PageFitModeKey, (int)PageFitMode.FitPage);
            return Enum.IsDefined(typeof(PageFitMode), raw) ? (PageFitMode)raw : PageFitMode.FitPage;
        }
        set
        {
            if (PageFitMode == value) return;
            AppPreferences.Set(PageFitModeKey, (int)value);
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        _settingsChangedWeakEvent.HandleEvent(this, EventArgs.Empty, nameof(SettingsChanged));
    }
}
