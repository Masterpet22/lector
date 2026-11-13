namespace NebulaReader.Services;

/// <summary>
/// Abstracción multiplataforma para seleccionar archivos individuales, carpetas y enumerar libros accesibles.
/// </summary>
public interface IFilePickerService
{
    Task<string?> PickBookFileAsync(CancellationToken ct = default);
    Task<string?> PickFolderAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> EnumerateBookFilesAsync(string folderPathOrUri, CancellationToken ct = default);
}
