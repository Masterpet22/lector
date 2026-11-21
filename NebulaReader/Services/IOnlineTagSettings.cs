namespace NebulaReader.Services;

public interface IOnlineTagSettings
{
    string Language { get; set; }
    Task<string?> GetCredentialAsync(OnlineTagSource source, CancellationToken ct = default);
}
