namespace NebulaReader.Services;

/// <summary>
/// Abstracción multiplataforma para seleccionar archivos individuales o carpetas de biblioteca.
/// </summary>
public interface IFilePickerService
{
    Task<string?> PickBookFileAsync(CancellationToken ct = default);
    Task<string?> PickFolderAsync(CancellationToken ct = default);
}

