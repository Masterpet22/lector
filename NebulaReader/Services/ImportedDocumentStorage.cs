using System.Security.Cryptography;
using System.Text;

namespace NebulaReader.Services;

/// <summary>Conserva documentos recibidos con permisos temporales sin cargarlos enteros en memoria.</summary>
public sealed class ImportedDocumentStorage(string directory)
{
    public async Task<string> StoreAsync(string sourceId, string? displayName, Func<Task<Stream>> openRead,
        CancellationToken ct = default, Func<string, Task>? validate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ct.ThrowIfCancellationRequested();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)));
        var folder = Path.Combine(Path.GetFullPath(directory), key);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, GetSafeFileName(displayName));
        var temporary = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var source = await openRead())
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await source.CopyToAsync(target, ct);
                if (target.Length == 0) throw new InvalidDataException("El documento recibido está vacío.");
            }
            ct.ThrowIfCancellationRequested();
            if (validate != null) await validate(temporary);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string GetSafeFileName(string? displayName)
    {
        var name = (displayName ?? "documento").Replace('\\', '/').Split('/')[^1];
        name = new string(name.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "documento";
        var stem = Path.GetFileNameWithoutExtension(name);
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            name = "documento_" + name;
        if (name.Length > 180)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 10) extension = string.Empty;
            name = name[..(180 - extension.Length)] + extension;
        }
        return name;
    }
}
