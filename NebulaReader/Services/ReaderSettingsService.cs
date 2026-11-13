using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Almacena y notifica las preferencias del visor usando Preferences.Default.
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
        get => Preferences.Default.Get(NightModeKey, false);
        set
        {
            if (NightReadingModeEnabled == value) return;
            Preferences.Default.Set(NightModeKey, value);
            if (value && VisualFilter == ReaderVisualFilter.None)
            {
                Preferences.Default.Set(VisualFilterKey, (int)ReaderVisualFilter.NightInvert);
            }
            else if (!value && VisualFilter == ReaderVisualFilter.NightInvert)
            {
                Preferences.Default.Set(VisualFilterKey, (int)ReaderVisualFilter.None);
            }
            NotifyChanged();
        }
    }

    public ReaderVisualFilter VisualFilter
    {
        get
        {
            var defaultFilter = NightReadingModeEnabled ? ReaderVisualFilter.NightInvert : ReaderVisualFilter.None;
            var raw = Preferences.Default.Get(VisualFilterKey, (int)defaultFilter);
            return Enum.IsDefined(typeof(ReaderVisualFilter), raw) ? (ReaderVisualFilter)raw : defaultFilter;
        }
        set
        {
            if (VisualFilter == value) return;
            Preferences.Default.Set(VisualFilterKey, (int)value);
            Preferences.Default.Set(NightModeKey, value != ReaderVisualFilter.None);
            NotifyChanged();
        }
    }

    public bool RightToLeftMangaMode
    {
        get => Preferences.Default.Get(MangaRtlKey, false);
        set
        {
            if (RightToLeftMangaMode == value) return;
            Preferences.Default.Set(MangaRtlKey, value);
            NotifyChanged();
        }
    }

    public bool ContinuousVerticalScroll
    {
        get => LayoutMode == ReaderLayoutMode.ContinuousVertical;
        set
        {
            if (ContinuousVerticalScroll == value) return;
            Preferences.Default.Set(VerticalScrollKey, value);
            var nextMode = value
                ? ReaderLayoutMode.ContinuousVertical
                : (DoublePageSpreadEnabled ? ReaderLayoutMode.DoublePage : ReaderLayoutMode.SinglePage);
            Preferences.Default.Set(LayoutModeKey, (int)nextMode);
            NotifyChanged();
        }
    }

    public bool DoublePageSpreadEnabled
    {
        get => Preferences.Default.Get(DoublePageKey, true);
        set
        {
            if (DoublePageSpreadEnabled == value && LayoutMode != ReaderLayoutMode.ContinuousVertical) return;
            Preferences.Default.Set(DoublePageKey, value);
            if (!ContinuousVerticalScroll)
            {
                Preferences.Default.Set(
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
            if (Preferences.Default.ContainsKey(LayoutModeKey))
            {
                var raw = Preferences.Default.Get(LayoutModeKey, (int)ReaderLayoutMode.DoublePage);
                if (Enum.IsDefined(typeof(ReaderLayoutMode), raw))
                {
                    return (ReaderLayoutMode)raw;
                }
            }

            if (Preferences.Default.Get(VerticalScrollKey, false))
            {
                return ReaderLayoutMode.ContinuousVertical;
            }

            return Preferences.Default.Get(DoublePageKey, true)
                ? ReaderLayoutMode.DoublePage
                : ReaderLayoutMode.SinglePage;
        }
        set
        {
            if (LayoutMode == value && Preferences.Default.ContainsKey(LayoutModeKey)) return;
            Preferences.Default.Set(LayoutModeKey, (int)value);
            Preferences.Default.Set(VerticalScrollKey, value == ReaderLayoutMode.ContinuousVertical);
            if (value == ReaderLayoutMode.SinglePage)
            {
                Preferences.Default.Set(DoublePageKey, false);
            }
            else if (value == ReaderLayoutMode.DoublePage)
            {
                Preferences.Default.Set(DoublePageKey, true);
            }
            NotifyChanged();
        }
    }

    public PageFitMode PageFitMode
    {
        get
        {
            var raw = Preferences.Default.Get(PageFitModeKey, (int)PageFitMode.FitPage);
            return Enum.IsDefined(typeof(PageFitMode), raw) ? (PageFitMode)raw : PageFitMode.FitPage;
        }
        set
        {
            if (PageFitMode == value) return;
            Preferences.Default.Set(PageFitModeKey, (int)value);
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        _settingsChangedWeakEvent.HandleEvent(this, EventArgs.Empty, nameof(SettingsChanged));
    }
}
