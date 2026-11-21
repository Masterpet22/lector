namespace NebulaReader.Services;

public sealed record FileAssociationRegistryEntry(string Key, string Name, string Value);

/// <summary>Registro por usuario: ofrece la aplicación sin sustituir las elecciones predeterminadas.</summary>
public static class FileAssociationRegistration
{
    public static IReadOnlyList<FileAssociationRegistryEntry> Build(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.Contains('"') ||
            !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Se necesita la ruta absoluta de NebulaReader.exe.", nameof(executablePath));
        var command = $"\"{executablePath}\" \"%1\"";
        var capabilities = @"Software\NebulaReader\Capabilities";
        var application = $@"Software\Classes\Applications\{Path.GetFileName(executablePath)}";
        var entries = new List<FileAssociationRegistryEntry>
        {
            new(capabilities, "ApplicationName", "Nebula Reader"),
            new(capabilities, "ApplicationDescription", "Lector de cómics CBZ, CBR y documentos PDF"),
            new(capabilities, "ApplicationIcon", $"\"{executablePath}\",0"),
            new(@"Software\RegisteredApplications", "Nebula Reader", capabilities),
            new(application, "FriendlyAppName", "Nebula Reader"),
            new(application + @"\shell\open\command", "", command)
        };
        foreach (var extension in DocumentFileTypes.Extensions)
        {
            var progId = "NebulaReader." + extension[1..].ToUpperInvariant();
            var progKey = @"Software\Classes\" + progId;
            entries.Add(new(progKey, "", "Documento " + extension[1..].ToUpperInvariant() + " de Nebula Reader"));
            entries.Add(new(progKey + @"\DefaultIcon", "", $"\"{executablePath}\",0"));
            entries.Add(new(progKey + @"\shell\open\command", "", command));
            entries.Add(new(@"Software\Classes\" + extension + @"\OpenWithProgids", progId, ""));
            entries.Add(new(capabilities + @"\FileAssociations", extension, progId));
            entries.Add(new(application + @"\SupportedTypes", extension, ""));
        }
        return entries;
    }
}
