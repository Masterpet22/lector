using System.Text.Json;

namespace NebulaReader.Services;

public sealed class ImportedFolderStore : IImportedFolderStore
{
    private const string PreferenceKey = "imported_folders_v1";

    public IReadOnlyList<string> GetFolders()
    {
        var json = Preferences.Default.Get(PreferenceKey, "[]");
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Add(string pathOrUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUri);
        var folders = GetFolders().ToList();
        if (folders.Contains(pathOrUri, StringComparer.OrdinalIgnoreCase)) return;
        folders.Add(pathOrUri);
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(folders));
    }

    public void Remove(string pathOrUri)
    {
        var folders = GetFolders()
            .Where(path => !string.Equals(path, pathOrUri, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(folders));
    }
}
