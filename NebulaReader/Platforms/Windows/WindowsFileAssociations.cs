using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NebulaReader.Services;

internal static class WindowsFileAssociations
{
    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No se encontró el ejecutable de la aplicación.");
        if (Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Abre NebulaReader.exe desde su carpeta de publicación para registrar los formatos.");
        foreach (var entry in FileAssociationRegistration.Build(executable))
        {
            using var key = Registry.CurrentUser.CreateSubKey(entry.Key);
            key.SetValue(entry.Name, entry.Value, RegistryValueKind.String);
        }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
