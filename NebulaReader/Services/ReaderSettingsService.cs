namespace NebulaReader.Services;

/// <summary>
/// Almacena y notifica las preferencias del visor usando Preferences.Default.
/// </summary>
public sealed class ReaderSettingsService : IReaderSettingsService
{
    private const string NightModeKey = "reader_night_mode";
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

