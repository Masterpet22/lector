namespace NebulaReader.Services;

/// <summary>Separa las credenciales de Windows portable de otras instalaciones.</summary>
public static class AppSecureStorage
{
    private static string StorageKey(string key)
    {
#if WINDOWS
        return "portable_" + key;
#else
        return key;
#endif
    }

    public static Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(StorageKey(key));
    public static Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(StorageKey(key), value);
    public static bool Remove(string key) => SecureStorage.Default.Remove(StorageKey(key));
}
