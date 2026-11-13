namespace NebulaReader.Services;

public partial class BookSourceFactory
{
    private partial Task<(Stream Stream, string Title, string? FileName)> OpenPlatformUriAsync(string uriString, CancellationToken ct)
    {
        throw new NotSupportedException($"Esquema de URI no soportado en Windows: {uriString}");
    }
}

