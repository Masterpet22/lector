using System.Text.Json.Serialization;

namespace NebulaReader.Models;

public sealed class LibraryBackup
{
    public const string CurrentSchema = "nebula-reader-data";
    public const int CurrentVersion = 1;

    [JsonRequired]
    public string Schema { get; set; } = CurrentSchema;
    [JsonRequired]
    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public BackupGlobalPreferences? GlobalPreferences { get; set; }
    [JsonRequired]
    public List<BackupBook> Books { get; set; } = [];
}

public sealed class BackupGlobalPreferences
{
    public ReaderLayoutMode LayoutMode { get; set; }
    public PageFitMode PageFitMode { get; set; }
    public bool RightToLeftManga { get; set; }
    public ReaderVisualFilter VisualFilter { get; set; }
    public string? OnlineTagLanguage { get; set; }
}

public sealed class BackupBook
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public BookFormat Format { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public ReadingStatus Status { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime? LastReadAtUtc { get; set; }
    public string? SeriesName { get; set; }
    public string? Tags { get; set; }
    public bool TitleEditedManually { get; set; }
    public bool SeriesEditedManually { get; set; }
    public bool TagsEditedManually { get; set; }
    public string? ContentFingerprint { get; set; }
    public List<int> BookmarkedPages { get; set; } = [];
    public BookReaderPreferences? Preferences { get; set; }

    public static string? GetFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("backup://", StringComparison.OrdinalIgnoreCase))
            return null;
#if ANDROID
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var cursor = Android.App.Application.Context.ContentResolver?.Query(
                    Android.Net.Uri.Parse(path), [Android.Provider.IOpenableColumns.DisplayName], null, null, null);
                var column = cursor?.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName) ?? -1;
                if (column >= 0 && cursor!.MoveToFirst())
                {
                    var displayName = cursor.GetString(column);
                    if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
                }
            }
            catch (Exception)
            {
                // El proveedor puede dejar de estar disponible; usar el nombre en la URI si existe.
            }
        }
#endif
        var normalized = Uri.UnescapeDataString(path).Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

public sealed record BackupImportResult(int Added, int Merged);
