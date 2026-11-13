namespace NebulaReader.Services;

public sealed partial class FullScreenService : IFullScreenService
{
    private readonly WeakEventManager _fullScreenChangedWeakEvent = new();

    public event EventHandler<bool>? FullScreenChanged
    {
        add => _fullScreenChangedWeakEvent.AddEventHandler(value);
        remove => _fullScreenChangedWeakEvent.RemoveEventHandler(value);
    }

    public bool IsFullScreen { get; private set; }

    public void SetFullScreen(bool enabled)
    {
        if (IsFullScreen == enabled)
        {
            ApplyFullScreenPlatform(enabled);
            return;
        }

        IsFullScreen = enabled;
        ApplyFullScreenPlatform(enabled);
        _fullScreenChangedWeakEvent.HandleEvent(this, enabled, nameof(FullScreenChanged));
    }

    public bool ToggleFullScreen()
    {
        SetFullScreen(!IsFullScreen);
        return IsFullScreen;
    }

    private partial void ApplyFullScreenPlatform(bool enabled);
}

