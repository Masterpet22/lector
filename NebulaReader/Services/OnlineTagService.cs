using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>Busca etiquetas en catálogos públicos y en fuentes configuradas por el usuario.</summary>
public sealed class OnlineTagService : IOnlineTagService
{
    public const string MetronTokenKey = "online_tags_metron_token";
    public const string ComicVineKey = "online_tags_comicvine_key";
    public const string GoogleBooksKey = "online_tags_googlebooks_key";

    private readonly HttpClient _http;
    private readonly IOnlineTagSettings _settings;

    public OnlineTagService(HttpClient http, IOnlineTagSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<IReadOnlyList<OnlineTagSource>> GetAvailableSourcesAsync(CancellationToken ct = default)
    {
        var sources = new List<OnlineTagSource>();
        foreach (var source in Enum.GetValues<OnlineTagSource>())
        {
            ct.ThrowIfCancellationRequested();
            if (source is OnlineTagSource.AniList or OnlineTagSource.OpenLibrary or OnlineTagSource.Crossref)
                sources.Add(source);
            else
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(await _settings.GetCredentialAsync(source, ct))) sources.Add(source);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* Una clave inaccesible requiere configuración y no se ofrece. */ }
            }
        }
        return sources;
    }

    public async Task<IReadOnlyList<OnlineTagSuggestion>> SearchAsync(
        BookItem book, OnlineTagSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (!(await GetAvailableSourcesAsync(ct)).Contains(source))
            throw new InvalidOperationException("Esta fuente no está disponible. Revisa su configuración.");
        var query = !string.IsNullOrWhiteSpace(book.SeriesName)
            ? book.SeriesName
            : LibraryTitlePresentation.Split(book.Title).Name;
        if (string.IsNullOrWhiteSpace(query)) return [];
        return source switch
        {
            OnlineTagSource.Metron => await SearchMetronAsync(query, ct),
            OnlineTagSource.ComicVine => await SearchComicVineAsync(query, ct),
            OnlineTagSource.AniList => await SearchAniListAsync(query, ct),
            OnlineTagSource.OpenLibrary => await SearchOpenLibraryAsync(query, ct),
            OnlineTagSource.GoogleBooks => await SearchGoogleBooksAsync(query, ct),
            OnlineTagSource.Crossref => await SearchCrossrefAsync(query, ct),
            _ => []
        };
    }

    private async Task<IReadOnlyList<OnlineTagSuggestion>> SearchOpenLibraryAsync(string query, CancellationToken ct)
    {
        var url = $"https://openlibrary.org/search.json?title={Uri.EscapeDataString(query)}&fields=key,title,author_name,subject,publisher,language&limit=5";
        using var json = await GetJsonAsync(url, ct);
        if (!TryArray(json.RootElement, "docs", out var docs)) return [];
        var results = new List<OnlineTagSuggestion>();
        foreach (var doc in docs.EnumerateArray().Take(5))
        {
            var key = GetString(doc, "key");
            Add(results, GetString(doc, "title"), ReadStrings(doc, "author_name").FirstOrDefault(),
                ReadStrings(doc, "subject"), key is { Length: > 0 } && key.StartsWith("/works/", StringComparison.Ordinal)
                    ? $"https://openlibrary.org{key}" : "https://openlibrary.org",
                publisher: ReadStrings(doc, "publisher").FirstOrDefault(),
                language: ReadStrings(doc, "language").FirstOrDefault());
        }
        return results;
    }

    private async Task<IReadOnlyList<OnlineTagSuggestion>> SearchAniListAsync(string query, CancellationToken ct)
    {
        const string gql = "query ($search: String) { Page(perPage: 5) { media(search: $search, type: MANGA) { id title { romaji english native } description(asHtml: false) startDate { year month day } genres tags { name rank isMediaSpoiler } } } }";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graphql.anilist.co")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query = gql, variables = new { search = query } }),
                Encoding.UTF8, "application/json")
        };
        using var json = await SendJsonAsync(request, ct);
        if (!json.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("Page", out var page)
            || !TryArray(page, "media", out var media)) return [];
        var results = new List<OnlineTagSuggestion>();
        foreach (var item in media.EnumerateArray())
        {
            if (!item.TryGetProperty("title", out var titles)) continue;
            var title = GetString(titles, "english") ?? GetString(titles, "romaji") ?? GetString(titles, "native");
            var tags = ReadStrings(item, "genres").ToList();
            if (TryArray(item, "tags", out var tagObjects))
                tags.AddRange(tagObjects.EnumerateArray()
                    .Where(tag => !GetBool(tag, "isMediaSpoiler") && GetInt(tag, "rank") >= 60)
                    .Select(tag => GetString(tag, "name")).OfType<string>());
            var id = GetInt(item, "id");
            Add(results, title, null, tags, $"https://anilist.co/manga/{id}",
                publicationDate: item.TryGetProperty("startDate", out var startDate) ? ReadDateParts(startDate) : null,
                description: GetString(item, "description"));
        }
        return results;
    }

    private async Task<IReadOnlyList<OnlineTagSuggestion>> SearchCrossrefAsync(string query, CancellationToken ct)
    {
        var url = $"https://api.crossref.org/works?query.title={Uri.EscapeDataString(query)}&rows=5&select=DOI,title,subject,author,publisher,published,language,abstract";
        using var json = await GetJsonAsync(url, ct);
        if (!json.RootElement.TryGetProperty("message", out var message)
            || !TryArray(message, "items", out var items)) return [];
        var results = new List<OnlineTagSuggestion>();
        foreach (var item in items.EnumerateArray())
        {
            var doi = GetString(item, "DOI");
            string? author = null;
            if (TryArray(item, "author", out var authors) && authors.GetArrayLength() > 0)
            {
                var first = authors[0];
                author = string.Join(" ", new[] { GetString(first, "given"), GetString(first, "family") }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
            }
            Add(results, ReadStrings(item, "title").FirstOrDefault(), author, ReadStrings(item, "subject"),
                doi == null ? "https://www.crossref.org" : $"https://doi.org/{Uri.EscapeDataString(doi)}",
                publisher: GetString(item, "publisher"),
                publicationDate: item.TryGetProperty("published", out var published) ? ReadCrossrefDate(published) : null,
                language: GetString(item, "language"), description: GetString(item, "abstract"));
        }
        return results;
    }

    private async Task<IReadOnlyList<OnlineTagSuggestion>> SearchGoogleBooksAsync(string query, CancellationToken ct)
    {
        var key = await RequireCredentialAsync(OnlineTagSource.GoogleBooks, "Google Books", ct);
        var url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString("intitle:" + query)}&maxResults=5&key={Uri.EscapeDataString(key)}";
        using var json = await GetJsonAsync(url, ct);
        if (!TryArray(json.RootElement, "items", out var items)) return [];
        var results = new List<OnlineTagSuggestion>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("volumeInfo", out var info)) continue;
            Add(results, GetString(info, "title"), ReadStrings(info, "authors").FirstOrDefault(),
                ReadStrings(info, "categories"), GetString(info, "infoLink") ?? "https://books.google.com",
                publisher: GetString(info, "publisher"), publicationDate: ParseFullDate(GetString(info, "publishedDate")),
                language: GetString(info, "language"), description: GetString(info, "description"));
        }
        return results;
    }

    private async Task<IReadOnlyList<OnlineTagSuggestion>> SearchMetronAsync(string query, CancellationToken ct)
    {
        var token = await RequireCredentialAsync(OnlineTagSource.Metron, "Metron", ct);
        using var json = await GetJsonAsync($"https://metron.cloud/api/series/?q={Uri.EscapeDataString(query)}", ct, token);
        if (!TryArray(json.RootElement, "results", out var items)) return [];
        var results = new List<OnlineTagSuggestion>();
        foreach (var item in items.EnumerateArray().Take(3))
        {
            var id = GetInt(item, "id");
            if (id <= 0) continue;
            using var detail = await GetJsonAsync($"https://metron.cloud/api/series/{id}/", ct, token);
            var root = detail.RootElement;
            var genres = ReadNames(root, "genres");
            Add(results, GetString(root, "name") ?? GetString(item, "name"),
                null, genres, GetString(root, "resource_url") ?? $"https://metron.cloud/api/series/{id}/",
                publisher: root.TryGetProperty("publisher", out var publisher) ? GetString(publisher, "name") : null,
                description: GetString(root, "desc") ?? GetString(root, "description"));
        }
        return results;
    }

    private async Task<IReadOnlyList<OnlineTagSuggestion>> SearchComicVineAsync(string query, CancellationToken ct)
    {
        var key = await RequireCredentialAsync(OnlineTagSource.ComicVine, "Comic Vine", ct);
        var url = $"https://comicvine.gamespot.com/api/search/?api_key={Uri.EscapeDataString(key)}&format=json&resources=volume&query={Uri.EscapeDataString(query)}&limit=5";
        using var json = await GetJsonAsync(url, ct);
        if (!TryArray(json.RootElement, "results", out var items)) return [];
        var results = new List<OnlineTagSuggestion>();
        foreach (var item in items.EnumerateArray())
        {
            var publisher = item.TryGetProperty("publisher", out var publisherField)
                ? GetString(publisherField, "name") : null;
            var tags = ReadNames(item, "concepts").Concat(ReadNames(item, "genres"));
            Add(results, GetString(item, "name"), null, tags,
                GetString(item, "site_detail_url") ?? "https://comicvine.gamespot.com",
                publisher: publisher, description: GetString(item, "description"));
        }
        return results;
    }

    private async Task<string> RequireCredentialAsync(OnlineTagSource provider, string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = await _settings.GetCredentialAsync(provider, ct);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Configura la clave de {source} en Configuración antes de buscar.");
        return value;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await SendJsonAsync(request, ct);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("La fuente rechazó la clave configurada. Revísala en Opciones.");
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("La fuente alcanzó su límite de consultas. Inténtalo más tarde.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static void Add(List<OnlineTagSuggestion> results, string? title, string? author,
        IEnumerable<string> tags, string url, string? publisher = null, DateTime? publicationDate = null,
        string? language = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        var normalized = tags.Select(tag => tag.Trim()).Where(tag => tag.Length is > 1 and <= 80)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        if (normalized.Length > 0 || !string.IsNullOrWhiteSpace(author)
            || !string.IsNullOrWhiteSpace(publisher) || publicationDate != null
            || !string.IsNullOrWhiteSpace(language) || !string.IsNullOrWhiteSpace(description))
            results.Add(new OnlineTagSuggestion(title.Trim(), CleanText(author, 255), normalized, url)
            {
                Publisher = CleanText(publisher, 255),
                PublicationDate = publicationDate,
                Language = CleanText(language, 100),
                Description = CleanText(description, 4000)
            });
    }

    private static DateTime? ParseFullDate(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Date : null;

    private static DateTime? ReadDateParts(JsonElement value)
    {
        var year = GetInt(value, "year");
        var month = GetInt(value, "month");
        var day = GetInt(value, "day");
        return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1
            && day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : null;
    }

    private static DateTime? ReadCrossrefDate(JsonElement value)
    {
        if (!TryArray(value, "date-parts", out var parts) || parts.GetArrayLength() == 0) return null;
        var first = parts[0];
        if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() < 3) return null;
        if (!first[0].TryGetInt32(out var year) || !first[1].TryGetInt32(out var month)
            || !first[2].TryGetInt32(out var day)) return null;
        return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1
            && day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : null;
    }

    private static string? CleanText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var plain = Regex.Replace(value, "<[^>]+>", " ");
        plain = System.Net.WebUtility.HtmlDecode(plain);
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        return plain.Length > maxLength ? plain[..maxLength].TrimEnd() : plain;
    }

    private static bool TryArray(JsonElement value, string name, out JsonElement array)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out array)
            && array.ValueKind == JsonValueKind.Array) return true;
        array = default;
        return false;
    }

    private static string? GetString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static int GetInt(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var number) ? number : 0;

    private static bool GetBool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.True;

    private static IEnumerable<string> ReadStrings(JsonElement value, string name) =>
        TryArray(value, name, out var array)
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()).OfType<string>()
            : [];

    private static IEnumerable<string> ReadNames(JsonElement value, string name) =>
        TryArray(value, name, out var array)
            ? array.EnumerateArray().Select(item => GetString(item, "name")).OfType<string>()
            : [];
}
