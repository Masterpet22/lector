namespace NebulaReader.Services;

/// <summary>
/// Estado global del ciclo de vida de la aplicación.
/// Permite que los callbacks encolados en el hilo de UI se descarten cuando la ventana
/// ya se está cerrando, evitando que toquen controles nativos en proceso de destrucción.
/// </summary>
public static class AppLifetime
{
    private static volatile bool _isShuttingDown;

    /// <summary>Indica si la aplicación ya inició su cierre.</summary>
    public static bool IsShuttingDown => _isShuttingDown;

    /// <summary>Marca la aplicación como en cierre. Es idempotente.</summary>
    public static void BeginShutdown() => _isShuttingDown = true;
}
