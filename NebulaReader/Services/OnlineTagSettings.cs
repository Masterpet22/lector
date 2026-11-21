namespace NebulaReader.Services;

public sealed class OnlineTagSettings : IOnlineTagSettings
{
    public string Language
    {
        get => TagLanguages.Normalize(AppPreferences.Get("online_tags_language", "es"));
        set => AppPreferences.Set("online_tags_language", TagLanguages.Normalize(value));
    }

    public async Task<string?> GetCredentialAsync(OnlineTagSource source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = source switch
        {
            OnlineTagSource.Metron => OnlineTagService.MetronTokenKey,
            OnlineTagSource.ComicVine => OnlineTagService.ComicVineKey,
            OnlineTagSource.GoogleBooks => OnlineTagService.GoogleBooksKey,
            _ => null
        };
        if (key == null) return null;
        var credential = await AppSecureStorage.GetAsync(key);
        ct.ThrowIfCancellationRequested();
        return credential;
    }
}
