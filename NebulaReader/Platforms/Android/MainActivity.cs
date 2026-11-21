using Android.App;
using Android.Content;
using Android.Content.PM;
using NebulaReader.Services;

namespace NebulaReader;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"], DataMimeTypes = ["application/pdf", "application/vnd.comicbook+zip",
        "application/vnd.comicbook-rar", "application/vnd.comicbook+rar", "application/x-cbz", "application/x-cbr",
        "application/zip", "application/x-zip-compressed", "application/vnd.rar", "application/x-rar", "application/x-rar-compressed"])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["content", "file"], DataHost = "*", DataMimeType = "application/octet-stream",
    DataPathPatterns = [".*\\.cbz", ".*\\.CBZ", ".*\\.cbr", ".*\\.CBR", ".*\\.pdf", ".*\\.PDF"])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "file", DataHost = "*",
    DataPathPatterns = [".*\\.cbz", ".*\\.CBZ", ".*\\.cbr", ".*\\.CBR", ".*\\.pdf", ".*\\.PDF"])]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (savedInstanceState == null) ExternalDocumentIntent.Handle(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        ExternalDocumentIntent.Handle(intent);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        FilePickerService.TryCompleteFolderPickResult(requestCode, resultCode, data);
        FilePickerService.TryCompleteBookPickResult(requestCode, resultCode, data);
        FilePickerService.TryCompleteBackupSaveResult(requestCode, resultCode, data);
    }
}
