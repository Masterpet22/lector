using System.Text.Json;
using System.Text.Json.Serialization;
using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>Respaldo de datos de lectura; no incluye los documentos originales.</summary>
public sealed class LibraryBackupService : ILibraryBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILibraryDatabase _database;
    private readonly IReaderSettingsService? _settings;

    public LibraryBackupService(ILibraryDatabase database, IReaderSettingsService? settings = null)
    {
        _database = database;
        _settings = settings;
    }

    public async Task<int> ExportAsync(Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var books = await _database.GetBooksAsync(ct).ConfigureAwait(false);
        var backup = new LibraryBackup
        {
            GlobalPreferences = _settings == null ? null : new BackupGlobalPreferences
            {
                LayoutMode = _settings.LayoutMode,
                PageFitMode = _settings.PageFitMode,
                RightToLeftManga = _settings.RightToLeftMangaMode,
                VisualFilter = _settings.VisualFilter
            }
        };
        foreach (var book in books)
        {
            ct.ThrowIfCancellationRequested();
            backup.Books.Add(new BackupBook
            {
                Id = book.Id,
                Title = book.Title,
                LocationHint = book.FilePath.StartsWith("backup://", StringComparison.OrdinalIgnoreCase)
                    ? book.OriginalFilePath : book.FilePath,
                Format = book.Format,
                CurrentPage = book.CurrentPage,
                TotalPages = book.TotalPages,
                Status = book.Status,
                AddedAtUtc = book.AddedAtUtc,
                LastReadAtUtc = book.LastReadAtUtc,
                SeriesName = book.SeriesName,
                Tags = book.Tags,
                TitleEditedManually = book.TitleEditedManually,
                SeriesEditedManually = book.SeriesEditedManually,
                ContentFingerprint = book.ContentFingerprint,
                BookmarkedPages = (await _database.GetBookmarkedPagesAsync(book.Id, ct).ConfigureAwait(false)).ToList(),
                Preferences = await _database.GetReaderPreferencesAsync(book.Id, ct).ConfigureAwait(false)
            });
        }
        await JsonSerializer.SerializeAsync(output, backup, JsonOptions, ct).ConfigureAwait(false);
        return backup.Books.Count;
    }

    public async Task<BackupImportResult> ImportAsync(Stream input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        LibraryBackup backup;
        try
        {
            backup = await JsonSerializer.DeserializeAsync<LibraryBackup>(input, JsonOptions, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("El respaldo está vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("El archivo no contiene un respaldo JSON válido.", ex);
        }

        Validate(backup);
        ct.ThrowIfCancellationRequested();
        var result = await _database.MergeBackupAsync(backup, ct).ConfigureAwait(false);
        if (backup.GlobalPreferences is { } preferences && _settings != null)
        {
            _settings.LayoutMode = preferences.LayoutMode;
            _settings.PageFitMode = preferences.PageFitMode;
            _settings.RightToLeftMangaMode = preferences.RightToLeftManga;
            _settings.VisualFilter = preferences.VisualFilter;
        }
        return result;
    }

    private static void Validate(LibraryBackup backup)
    {
        if (backup.Schema != LibraryBackup.CurrentSchema || backup.Version != LibraryBackup.CurrentVersion)
            throw new InvalidDataException("Versión o tipo de respaldo no compatible.");
        if (backup.Books == null) throw new InvalidDataException("Falta la lista de documentos.");
        if (backup.GlobalPreferences is { } global &&
            (!Enum.IsDefined(global.LayoutMode) || !Enum.IsDefined(global.PageFitMode) ||
             !Enum.IsDefined(global.VisualFilter)))
            throw new InvalidDataException("El respaldo contiene ajustes generales inválidos.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var book in backup.Books)
        {
            if (book == null || string.IsNullOrWhiteSpace(book.Id) || book.Id.Length > 128 ||
                !ids.Add(book.Id) || string.IsNullOrWhiteSpace(book.Title) ||
                book.Format is not (BookFormat.Cbz or BookFormat.Cbr or BookFormat.Pdf) ||
                !Enum.IsDefined(book.Status) || book.CurrentPage < 1 || book.TotalPages < 0 ||
                (book.TotalPages > 0 && book.CurrentPage > book.TotalPages) || book.BookmarkedPages == null)
                throw new InvalidDataException("El respaldo contiene un documento inválido o duplicado.");

            var pages = new HashSet<int>();
            foreach (var page in book.BookmarkedPages)
                if (page < 1 || (book.TotalPages > 0 && page > book.TotalPages) || !pages.Add(page))
                    throw new InvalidDataException($"Marcador inválido en '{book.Title}'.");

            if (book.Preferences is { } preferences &&
                (preferences.BookId != book.Id || !Enum.IsDefined(preferences.LayoutMode) ||
                 !Enum.IsDefined(preferences.PageFitMode) || !Enum.IsDefined(preferences.VisualFilter)))
                throw new InvalidDataException($"Preferencias inválidas en '{book.Title}'.");
        }
    }
}
