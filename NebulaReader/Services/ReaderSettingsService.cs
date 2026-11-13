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

    private readonly WeakEventManager _settingsChangedWeakEvent = new();

    public event EventHandler? SettingsChanged
    {
        add => _settingsChangedWeakEvent.AddEventHandler(value);
        remove => _settingsChangedWeakEvent.RemoveEventHandler(value);
    }

    public bool NightReadingModeEnabled
    {
        get => Preferences.Default.Get(NightModeKey, true);
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
        get => Preferences.Default.Get(VerticalScrollKey, false);
        set
        {
            if (ContinuousVerticalScroll == value) return;
            Preferences.Default.Set(VerticalScrollKey, value);
            NotifyChanged();
        }
    }

    public bool DoublePageSpreadEnabled
    {
        get => Preferences.Default.Get(DoublePageKey, true);
        set
        {
            if (DoublePageSpreadEnabled == value) return;
            Preferences.Default.Set(DoublePageKey, value);
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        _settingsChangedWeakEvent.HandleEvent(this, EventArgs.Empty, nameof(SettingsChanged));
    }
}
