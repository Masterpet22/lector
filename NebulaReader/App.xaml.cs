using System.Diagnostics;
using System.Runtime.InteropServices;
using NebulaReader.Services;
using NebulaReader.Views;

namespace NebulaReader;

public partial class App : Application
{
#if WINDOWS
	private Microsoft.UI.Xaml.Window? _nativeWindow;
	private bool _windowClosing;

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("kernel32.dll")]
	private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
#endif

	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
#if WINDOWS
		window.HandlerChanged += (_, _) =>
		{
			if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
				ReferenceEquals(_nativeWindow, nativeWindow)) return;
			_nativeWindow = nativeWindow;
			nativeWindow.AppWindow.Closing += (_, _) =>
			{
				if (_windowClosing) return;
				_windowClosing = true;
				AppLifetime.BeginShutdown();
				try
				{
					if (window.Page is AppShell shell)
					{
						foreach (var page in shell.Navigation.NavigationStack)
						{
							if (page is ReaderPage readerPage)
								readerPage.PrepareForShutdown();
							else if (page is LibraryPage libraryPage)
								libraryPage.PrepareForShutdown();
						}

						if (shell.CurrentPage is ReaderPage currentReader)
							currentReader.PrepareForShutdown();
						else if (shell.CurrentPage is LibraryPage currentLibrary)
							currentLibrary.PrepareForShutdown();
					}
				}
				catch (Exception ex)
				{
					DiagnosticLog.Write("Error al guardar la lectura antes de cerrar", ex);
					Trace.TraceError("Error al guardar la lectura antes de cerrar: {0}", ex);
				}
			};
			nativeWindow.Closed += (_, _) =>
			{
				AppLifetime.BeginShutdown();
				var watchdog = new Thread(() =>
				{
					Thread.Sleep(800);
					TerminateProcess(GetCurrentProcess(), 0);
				})
				{
					IsBackground = true,
					Name = "NebulaReaderShutdownWatchdog"
				};
				watchdog.Start();
			};
		};
#endif
		return window;
	}
}
