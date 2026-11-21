using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using LanguageDetection;

namespace NebulaReader.Services;

public interface ITagLocalizationService
{
    Task<IReadOnlyList<string>> LocalizeAsync(IEnumerable<string> tags, CancellationToken ct = default);
}

/// <summary>Conserva primero los originales del idioma elegido y traduce solo los restantes.</summary>
public sealed class TagLocalizationService(HttpClient http, IOnlineTagSettings settings) : ITagLocalizationService
{
    private readonly ConcurrentDictionary<(string Text, string Source, string Target), string> _cache = new();
    private static readonly Lazy<LanguageDetector> Detector = new(() =>
    {
        var detector = new LanguageDetector();
        detector.AddLanguages("spa", "eng", "fra", "por", "ita", "deu", "jpn");
        return detector;
    });

    // Los géneros breves o compartidos entre idiomas no se detectan bien estadísticamente.
    private static readonly (string Codes, string[] Tags)[] CommonGenres =
    [
        ("es", ["Aventura", "Acción", "Ciencia ficción", "Fantasía", "Misterio", "Historia", "Comedia", "Superhéroes"]),
        ("en", ["Adventure", "Science fiction", "Fantasy", "Mystery", "History", "Comedy", "Superheroes"]),
        ("fr", ["Aventure", "Science-fiction", "Fantastique", "Mystère", "Histoire", "Comédie"]),
        ("pt", ["Aventura", "Ação", "Ficção científica", "Fantasia", "Mistério", "História", "Comédia"]),
        ("it", ["Avventura", "Azione", "Fantascienza", "Fantasia", "Mistero", "Storia", "Commedia"]),
        ("de", ["Abenteuer", "Aktion", "Wissenschaft", "Geschichte", "Komödie"]),
        ("en,fr", ["Action"]), ("es,en,fr,pt", ["Romance"]),
        ("es,en,pt,it,de", ["Drama"]), ("es,en,pt,it,de", ["Horror"]),
        ("es,en,fr,pt,it,de,ja", ["Manga", "Anime", "Shounen", "Shonen", "Seinen", "Shoujo", "Shojo", "Josei", "Isekai", "Mecha"])
    ];

    private static readonly string[][] EquivalentGenres =
    [
        ["Adventure", "Aventura", "Aventure", "Avventura", "Abenteuer", "冒険"],
        ["Action", "Acción", "Ação", "Azione", "Aktion", "アクション"],
        ["Science fiction", "Ciencia ficción", "Science-fiction", "Ficção científica", "Fantascienza"],
        ["Fantasy", "Fantasía", "Fantasia", "Fantastique", "ファンタジー"],
        ["Mystery", "Misterio", "Mystère", "Mistério", "Mistero", "ミステリー"],
        ["Comedy", "Comedia", "Comédie", "Comédia", "Commedia", "Komödie", "コメディ"],
        ["History", "Historia", "Histoire", "História", "Storia", "Geschichte", "歴史"],
        ["Superheroes", "Superhéroes", "Super-heróis", "Supereroi", "Superhelden"]
    ];

    public async Task<IReadOnlyList<string>> LocalizeAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        var target = TagLanguages.Normalize(settings.Language);
        var classified = await Task.Run(() => tags.Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => (Text: tag, Codes: DetectLanguages(tag))).ToArray(), ct).ConfigureAwait(false);
        var result = classified.Where(tag => tag.Codes.Contains(target)).Select(tag => tag.Text).ToList();
        foreach (var tag in classified.Where(tag => !tag.Codes.Contains(target)))
        {
            ct.ThrowIfCancellationRequested();
            // Si el catálogo incluye ambas versiones conocidas, basta con el original elegido.
            if (EquivalentGenres.Any(forms => forms.Contains(tag.Text, StringComparer.OrdinalIgnoreCase) &&
                result.Any(original => forms.Contains(original, StringComparer.OrdinalIgnoreCase)))) continue;
            var source = tag.Codes.FirstOrDefault();
            if (source == null)
                throw new InvalidOperationException($"No se pudo identificar el idioma de «{tag.Text}». No se modificaron las etiquetas.");
            var key = (tag.Text, source, target);
            if (!_cache.TryGetValue(key, out var translated))
            {
                var uri = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(tag.Text)}&langpair={source}%7C{target}";
                using var response = await http.GetAsync(uri, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                var root = json.RootElement;
                if (!root.TryGetProperty("responseStatus", out var status) || !status.TryGetInt32(out var code) || code != 200
                    || (root.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True)
                    || !root.TryGetProperty("responseData", out var data)
                    || !data.TryGetProperty("translatedText", out var text) || text.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("No se pudo traducir la etiqueta. El servicio puede estar temporalmente indisponible o haber alcanzado su límite de uso. Inténtalo más tarde.");
                translated = WebUtility.HtmlDecode(text.GetString())?.Trim();
                if (string.IsNullOrWhiteSpace(translated) || translated.Length > 80 || translated.Contains(';') || translated.Contains(','))
                    throw new InvalidOperationException("El servicio devolvió una traducción de etiqueta no válida. Inténtalo más tarde.");
                if (_cache.Count >= 2000) _cache.Clear();
                _cache[key] = translated;
            }
            result.Add(translated);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> DetectLanguages(string tag)
    {
        var known = CommonGenres.Where(group => group.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .SelectMany(group => group.Codes.Split(',')).Distinct().ToArray();
        if (known.Length > 0) return known;
        if (!tag.Any(char.IsLetter)) return TagLanguages.Options.Select(option => option.Code).ToArray();
        var detected = Detector.Value.Detect(tag);
        var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(culture =>
            culture.ThreeLetterISOLanguageName == detected || culture.TwoLetterISOLanguageName == detected);
        return culture == null ? ["en"] : [culture.TwoLetterISOLanguageName];
    }
}
