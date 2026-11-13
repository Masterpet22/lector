namespace NebulaReader.Platforms.Android;

/// <summary>
/// Utilidad de plataforma para gestionar permisos de lectura en Android (Scoped Storage).
/// </summary>
public static class StoragePermissions
{
    public static async Task<PermissionStatus> RequestReadStorageAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.StorageRead>();
        }
        return status;
    }
}

