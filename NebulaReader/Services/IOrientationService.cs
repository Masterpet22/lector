namespace NebulaReader.Services;

/// <summary>
/// Control multiplataforma para bloquear o desbloquear la rotación de pantalla durante la lectura.
/// </summary>
public interface IOrientationService
{
    bool IsRotationLocked { get; }
    void SetRotationLocked(bool locked);
}

