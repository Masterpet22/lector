namespace NebulaReader.Services;

/// <summary>Carpetas elegidas para importar documentos, conservadas entre sesiones.</summary>
public interface IImportedFolderStore
{
    IReadOnlyList<string> GetFolders();
    void Add(string pathOrUri);
    void Remove(string pathOrUri);
}
