namespace NebulaReader.Services;

/// <summary>
/// Control multiplataforma para activar o desactivar el modo de pantalla completa en toda la aplicación.
/// </summary>
public interface IFullScreenService
{
    event EventHandler<bool>? FullScreenChanged;

    bool IsFullScreen { get; }
    void SetFullScreen(bool enabled);
    bool ToggleFullScreen();
}

