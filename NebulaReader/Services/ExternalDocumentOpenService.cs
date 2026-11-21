using System.Diagnostics;
using NebulaReader.ViewModels;
using NebulaReader.Views;

namespace NebulaReader.Services;

/// <summary>Coordina la apertura desde el sistema operativo con la navegación y el progreso existentes.</summary>
public sealed class ExternalDocumentOpenService
{
    private readonly ILibraryService _library;
    private readonly IBookSourceFactory _sources;
    private readonly DocumentOpenQueue _queue;
    private volatile Shell? _shell;

    public ExternalDocumentOpenService(ILibraryService library, IBookSourceFactory sources)
    {
        _library = library;
        _sources = sources;
        _queue = new DocumentOpenQueue(ShowErrorAsync);
    }

    public bool HasPendingRequests => _queue.HasPendingRequests;

    public void Enqueue(string path) => Enqueue(() => Task.FromResult(path));

    public void Enqueue(Func<Task<string>> resolvePath)
    {
        _queue.Enqueue(async () =>
        {
            var shell = _shell!;
            var busyViewModel = shell.CurrentPage?.BindingContext as BaseViewModel;
            var ownsBusyState = busyViewModel is { IsBusy: false };
            if (ownsBusyState) busyViewModel!.IsBusy = true;
            if (busyViewModel is LibraryViewModel libraryVm) libraryVm.StatusMessage = "Abriendo documento...";
            try
            {
                var path = await resolvePath();
                using (var source = await _sources.CreateAsync(path))
                {
                    if (source.PageCount == 0)
                        throw new InvalidDataException("El documento no contiene páginas compatibles.");
                }

                // Guardar y cerrar el lector anterior antes de consultar el progreso del documento recibido.
                var reader = shell.Navigation.NavigationStack.OfType<ReaderPage>().LastOrDefault();
                if (reader?.BindingContext is ReaderViewModel readerVm)
                {
                    if (readerVm.CloseReaderCommand.ExecutionTask is { IsCompleted: false } closing)
                        await closing;
                    else
                        await readerVm.CloseReaderCommand.ExecuteAsync(null);
                }
                if (shell.Navigation.NavigationStack.Count > 1)
                    await shell.Navigation.PopToRootAsync(false);

                var book = await _library.ImportFileAsync(path);
                await shell.GoToAsync(nameof(ReaderPage), new Dictionary<string, object> { ["Book"] = book });
#if WINDOWS
                if (shell.Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                    nativeWindow.Activate();
#endif
            }
            finally
            {
                if (ownsBusyState) busyViewModel!.IsBusy = false;
            }
        });
        Schedule();
    }

    public void AttachShell(Shell shell)
    {
        _shell = shell;
        _queue.MarkReady();
        Schedule();
    }

    private void Schedule()
    {
        _shell?.Dispatcher.Dispatch(async () =>
        {
            try { await _queue.DrainAsync(); }
            catch (Exception ex) { Trace.TraceError("No se pudo procesar la apertura externa: {0}", ex); }
        });
    }

    private async Task ShowErrorAsync(Exception error)
    {
        Trace.TraceError("No se pudo abrir un documento externo: {0}", error);
        if (AppLifetime.IsShuttingDown) return;
        if (_shell is { } shell)
        {
            if (shell.CurrentPage?.BindingContext is LibraryViewModel libraryVm)
                libraryVm.StatusMessage = "No se pudo abrir el documento: " + error.Message;
            try
            {
                await shell.DisplayAlertAsync("No se pudo abrir el documento", error.Message, "Aceptar");
            }
            catch (Exception alertEx)
            {
                Trace.TraceError("No se pudo mostrar la alerta de apertura externa: {0}", alertEx);
            }
        }
    }
}
