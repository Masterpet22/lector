using Microsoft.UI.Xaml;
using NebulaReader.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NebulaReader.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// Sin estos registros, una excepción no controlada en WinUI solo deja un 0xC000027B
		// en el Visor de eventos, sin pista de qué la provocó.
		UnhandledException += (_, e) =>
			DiagnosticLog.Write($"Excepción no controlada en WinUI (cierre={AppLifetime.IsShuttingDown})", e.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			DiagnosticLog.Write($"Excepción no controlada (cierre={AppLifetime.IsShuttingDown}): {e.ExceptionObject}");
		TaskScheduler.UnobservedTaskException += (_, e) =>
			DiagnosticLog.Write("Excepción de tarea no observada", e.Exception);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
