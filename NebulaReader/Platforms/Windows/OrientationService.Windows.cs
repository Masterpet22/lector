namespace NebulaReader.Services;

public sealed partial class OrientationService
{
    private partial void ApplyRotationLockPlatform(bool locked)
    {
        // Las ventanas de escritorio en Windows mantienen su orientación sin rotación automática por sensores.
    }
}

