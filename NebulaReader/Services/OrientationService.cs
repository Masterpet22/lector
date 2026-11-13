namespace NebulaReader.Services;

public sealed partial class OrientationService : IOrientationService
{
    public bool IsRotationLocked { get; private set; }

    public void SetRotationLocked(bool locked)
    {
        IsRotationLocked = locked;
        ApplyRotationLockPlatform(locked);
    }

    private partial void ApplyRotationLockPlatform(bool locked);
}

