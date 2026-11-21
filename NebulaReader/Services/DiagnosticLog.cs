using System.Diagnostics;

namespace NebulaReader.Services;

/// <summary>
/// Registro de diagnóstico mínimo en disco para investigar cierres inesperados y bloqueos.
/// En Windows (versión portable) se guarda junto a los datos de la aplicación en <c>Data\logs</c>.
/// </summary>
public static class DiagnosticLog
{
    private const long MaxLogBytes = 512 * 1024;
    private static readonly object Sync = new();
    private static string? _filePath;

    public static string FilePath => _filePath ??= ResolvePath();

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, line);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("No se pudo escribir el registro de diagnóstico: {0}", ex);
        }
    }

    public static void Write(string context, Exception exception) => Write($"{context}: {exception}");

    private static string ResolvePath()
    {
#if WINDOWS
        var root = Path.Combine(AppContext.BaseDirectory, "Data");
#else
        var root = FileSystem.AppDataDirectory;
#endif
        return Path.Combine(root, "logs", "nebulareader.log");
    }
}
