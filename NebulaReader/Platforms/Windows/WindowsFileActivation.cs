using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace NebulaReader.Services;

internal static class WindowsFileActivation
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        var instance = AppInstance.GetCurrent();
        instance.Activated += OnActivated;
        var activation = instance.GetActivatedEventArgs();
        if (activation.Kind == ExtendedActivationKind.File)
            HandleActivation(activation);
        else
            OpenPaths(DocumentFileTypes.GetPathsFromArguments(Environment.GetCommandLineArgs().Skip(1)));
    }

    private static void OnActivated(object? sender, AppActivationArguments args) => HandleActivation(args);

    private static void HandleActivation(AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.File && args.Data is IFileActivatedEventArgs files)
            OpenPaths(files.Files.OfType<StorageFile>().Select(file => file.Path));
    }

    private static void OpenPaths(IEnumerable<string> paths)
    {
        var service = IPlatformApplication.Current?.Services.GetRequiredService<ExternalDocumentOpenService>();
        if (service == null) return;
        foreach (var path in paths) service.Enqueue(path);
    }
}
