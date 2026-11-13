namespace NebulaReader.Services;

public partial class FilePickerService
{
    private partial Task<string?> PickFolderPlatformAsync(CancellationToken ct)
    {
        // En Fase 3 se conectará Intent.ActionOpenDocumentTree con Scoped Storage persistente.
        return Task.FromResult<string?>(null);
    }
}

