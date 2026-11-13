namespace NebulaReader.Services;

/// <summary>
/// Preferencias persistidas de lectura compartidas entre la pantalla de ajustes y el visor.
/// </summary>
public interface IReaderSettingsService
{
    event EventHandler? SettingsChanged;

    bool NightReadingModeEnabled { get; set; }
    bool RightToLeftMangaMode { get; set; }
    bool ContinuousVerticalScroll { get; set; }
    bool DoublePageSpreadEnabled { get; set; }
}

