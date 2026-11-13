using Microsoft.Extensions.Logging;
using NebulaReader.Services;
using NebulaReader.ViewModels;
using NebulaReader.Views;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace NebulaReader;

/// <summary>
/// Punto de configuración principal de .NET MAUI y contenedor de inyección de dependencias.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Servicios compartidos (Singleton)
        builder.Services.AddSingleton<IBookSourceFactory, BookSourceFactory>();
        builder.Services.AddSingleton<ILibraryDatabase, LibraryDatabase>();
        builder.Services.AddSingleton<ICoverThumbnailService, CoverThumbnailService>();
        builder.Services.AddSingleton<ILibraryService, LibraryService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IPageCacheService, PageCacheService>();
        builder.Services.AddSingleton<IReaderSettingsService, ReaderSettingsService>();
        builder.Services.AddSingleton<IOrientationService, OrientationService>();
        builder.Services.AddSingleton<IFullScreenService, FullScreenService>();

        // ViewModels (Transient)
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<ReaderViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Páginas / Vistas (Transient)
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<ReaderPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
