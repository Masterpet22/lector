using System.Net;
using System.Text;
using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class OnlineTagTests
{
    private sealed class Settings : IOnlineTagSettings
    {
        public string Language { get; set; } = "es";
        public Dictionary<OnlineTagSource, string> Keys { get; } = new();
        public bool ThrowOnRead { get; set; }
        public Task<string?> GetCredentialAsync(OnlineTagSource source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (ThrowOnRead) throw new InvalidOperationException("Secure storage unavailable");
            return Task.FromResult(Keys.TryGetValue(source, out var value) ? value : null);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task AvailableSourcesOnlyIncludePublicAndCurrentlyConfiguredProviders()
    {
        var settings = new Settings();
        using var http = new HttpClient(new Handler(_ => throw new Exception("Availability must not make network requests")));
        var service = new OnlineTagService(http, settings);
        Assert.Equal(new[] { OnlineTagSource.AniList, OnlineTagSource.OpenLibrary, OnlineTagSource.Crossref },
            await service.GetAvailableSourcesAsync());

        settings.Keys[OnlineTagSource.ComicVine] = "configured";
        settings.Keys[OnlineTagSource.Metron] = "   ";
        Assert.Contains(OnlineTagSource.ComicVine, await service.GetAvailableSourcesAsync());
        Assert.DoesNotContain(OnlineTagSource.Metron, await service.GetAvailableSourcesAsync());
        settings.Keys.Clear();
        Assert.DoesNotContain(OnlineTagSource.ComicVine, await service.GetAvailableSourcesAsync());
        settings.ThrowOnRead = true;
        Assert.Equal(3, (await service.GetAvailableSourcesAsync()).Count);
    }

    [Theory]
    [InlineData(BookFormat.Cbz)]
    [InlineData(BookFormat.Cbr)]
    [InlineData(BookFormat.Pdf)]
    public async Task EveryDocumentFormatCanSearchTheSameSource(BookFormat format)
    {
        using var http = new HttpClient(new Handler(_ =>
            """{"data":{"Page":{"media":[{"id":1,"title":{"english":"A"},"genres":["Adventure"]}]}}}"""));
        var service = new OnlineTagService(http, new Settings());
        Assert.Single(await service.SearchAsync(new BookItem { Title = "A", Format = format }, OnlineTagSource.AniList));
    }

    [Fact]
    public async Task MissingCredentialStopsSearchBeforeAnyNetworkRequest()
    {
        var handler = new Handler(_ => throw new Exception("Must not send"));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OnlineTagService(http, new Settings())
            .SearchAsync(new BookItem { Title = "A" }, OnlineTagSource.Metron));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ComicPublishersAreNotMistakenForAuthorsOrTags()
    {
        var settings = new Settings();
        settings.Keys[OnlineTagSource.Metron] = "token";
        settings.Keys[OnlineTagSource.ComicVine] = "key";
        using var http = new HttpClient(new Handler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("metron.cloud/api/series/?", StringComparison.Ordinal))
                return """{"results":[{"id":7,"name":"Serie"}]}""";
            if (url.Contains("metron.cloud/api/series/7/", StringComparison.Ordinal))
                return """{"name":"Serie","publisher":{"name":"Editorial Metron"},"genres":[{"name":"Fantasía"}]}""";
            return """{"results":[{"name":"Serie","publisher":{"name":"Editorial Vine"},"genres":[{"name":"Aventura"}]}]}""";
        }));
        var service = new OnlineTagService(http, settings);
        var book = new BookItem { Title = "Serie" };
        var metron = Assert.Single(await service.SearchAsync(book, OnlineTagSource.Metron));
        Assert.Null(metron.Author);
        Assert.Equal("Editorial Metron", metron.Publisher);
        var comicVine = Assert.Single(await service.SearchAsync(book, OnlineTagSource.ComicVine));
        Assert.Null(comicVine.Author);
        Assert.Equal("Editorial Vine", comicVine.Publisher);
        Assert.DoesNotContain("Editorial Vine", comicVine.Tags);
    }

    [Fact]
    public async Task GoogleBooksSuggestionKeepsItsDocumentFields()
    {
        var settings = new Settings();
        settings.Keys[OnlineTagSource.GoogleBooks] = "key";
        using var http = new HttpClient(new Handler(_ => """
            {"items":[{"volumeInfo":{"title":"Libro","authors":["Autora"],"categories":["Historia"],
            "publisher":"Editorial","publishedDate":"2024-05-17","language":"es",
            "description":"<b>Resumen</b> del libro"}},
            {"volumeInfo":{"title":"Otro","authors":["Autor"],"publishedDate":"2024"}}]}
            """));
        var results = await new OnlineTagService(http, settings)
            .SearchAsync(new BookItem { Title = "Libro" }, OnlineTagSource.GoogleBooks);
        var result = Assert.Single(results, item => item.Title == "Libro");
        Assert.Equal("Autora", result.Author);
        Assert.Equal("Editorial", result.Publisher);
        Assert.Equal(new DateTime(2024, 5, 17), result.PublicationDate);
        Assert.Equal("es", result.Language);
        Assert.Equal("Resumen del libro", result.Description);
        Assert.Null(Assert.Single(results, item => item.Title == "Otro").PublicationDate);
    }

    [Fact]
    public async Task PublicCatalogsKeepAvailablePublisherDateLanguageAndDescription()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            var url = request.RequestUri!.Host;
            if (url == "openlibrary.org")
                return """{"docs":[{"key":"/works/OL1W","title":"Libro","author_name":["Autora"],"subject":["Historia"],"publisher":["Editorial"],"language":["spa"]}]}""";
            if (url == "api.crossref.org")
                return """{"message":{"items":[{"DOI":"10.1/test","title":["Artículo"],"subject":["Ciencia"],"author":[{"given":"Ana","family":"Pérez"}],"publisher":"Editorial","published":{"date-parts":[[2024,5,17]]},"language":"es","abstract":"<p>Resumen</p>"}]}}""";
            return """{"data":{"Page":{"media":[{"id":1,"title":{"english":"Manga"},"genres":["Fantasía"],"startDate":{"year":2023,"month":9,"day":2},"description":"Una aventura"}]}}}""";
        }));
        var service = new OnlineTagService(http, new Settings());
        var book = new BookItem { Title = "Libro" };
        var openLibrary = Assert.Single(await service.SearchAsync(book, OnlineTagSource.OpenLibrary));
        Assert.Equal("Editorial", openLibrary.Publisher);
        Assert.Equal("spa", openLibrary.Language);
        var crossref = Assert.Single(await service.SearchAsync(book, OnlineTagSource.Crossref));
        Assert.Equal("Ana Pérez", crossref.Author);
        Assert.Equal(new DateTime(2024, 5, 17), crossref.PublicationDate);
        Assert.Equal("Resumen", crossref.Description);
        var aniList = Assert.Single(await service.SearchAsync(book, OnlineTagSource.AniList));
        Assert.Equal(new DateTime(2023, 9, 2), aniList.PublicationDate);
        Assert.Equal("Una aventura", aniList.Description);
    }

    [Fact]
    public async Task OriginalTargetLanguageWinsOverEquivalentTranslatedTagAndTranslationsAreCached()
    {
        var handler = new Handler(request =>
        {
            Assert.Contains("langpair=en%7Ces", request.RequestUri!.OriginalString);
            return """{"responseStatus":200,"responseData":{"translatedText":"aventura"}}""";
        });
        using var http = new HttpClient(handler);
        var service = new TagLocalizationService(http, new Settings());

        Assert.Equal(new[] { "Aventura", "Ciencia ficción" },
            await service.LocalizeAsync(["Adventure", "Aventura", "Ciencia ficción"]));
        Assert.Equal(new[] { "aventura" }, await service.LocalizeAsync(["Adventure"]));
        Assert.Equal(new[] { "aventura" }, await service.LocalizeAsync(["Adventure"]));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ChangingLanguageUsesNewTargetAndKeepsItsOriginalTags()
    {
        var settings = new Settings();
        var handler = new Handler(request => request.RequestUri!.OriginalString.Contains("%7Ces")
            ? """{"responseStatus":200,"responseData":{"translatedText":"Aventura"}}"""
            : """{"responseStatus":200,"responseData":{"translatedText":"Adventure"}}""");
        using var http = new HttpClient(handler);
        var service = new TagLocalizationService(http, settings);
        Assert.Equal(new[] { "Aventura" }, await service.LocalizeAsync(["Adventure"]));
        settings.Language = "en";
        Assert.Equal(new[] { "Adventure" }, await service.LocalizeAsync(["Aventura", "Adventure"]));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task FailedOrExhaustedTranslationDoesNotReturnPartialTagsOrCacheErrors()
    {
        var fail = true;
        var handler = new Handler(_ => fail
            ? """{"responseStatus":403,"quotaFinished":true,"responseData":{"translatedText":"LIMIT REACHED"}}"""
            : """{"responseStatus":200,"responseData":{"translatedText":"Aventura"}}""");
        using var http = new HttpClient(handler);
        var service = new TagLocalizationService(http, new Settings());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LocalizeAsync(["Ciencia ficción", "Adventure"]));
        fail = false;
        Assert.Equal(new[] { "Aventura" }, await service.LocalizeAsync(["Adventure"]));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task LanguageDetectionModelsHandleOriginalLongerTagsWithoutTranslation()
    {
        var handler = new Handler(_ => throw new Exception("An original Spanish tag must not need translation"));
        using var http = new HttpClient(handler);
        var service = new TagLocalizationService(http, new Settings());
        Assert.Equal(new[] { "historias de aventuras y viajes por el mundo" },
            await service.LocalizeAsync(["historias de aventuras y viajes por el mundo"]));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AvailableOriginalSkipsTranslationEvenWhenTranslatorIsUnavailable()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Original available; do not translate")));
        Assert.Equal(new[] { "Aventura", "Ciencia ficción" },
            await new TagLocalizationService(http, new Settings()).LocalizeAsync(
                ["Adventure", "Science fiction", "Aventura", "Ciencia ficción"]));
    }

    [Fact]
    public async Task NumericTagsAreLanguageIndependentAndDoNotNeedTranslation()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not send")));
        Assert.Equal(new[] { "2024", "18+" },
            await new TagLocalizationService(http, new Settings()).LocalizeAsync(["2024", "18+"]));
    }

    [Fact]
    public async Task CancellationStopsLocalization()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not send")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TagLocalizationService(http, new Settings()).LocalizeAsync(["Adventure"], cts.Token));
    }

    [Fact]
    public async Task ShortAmbiguousCatalogTagsFallbackToEnglishSourceInsteadOfFailing()
    {
        var handler = new Handler(request =>
        {
            Assert.Contains("%7Ces", request.RequestUri!.OriginalString);
            return """{"responseStatus":200,"responseData":{"translatedText":"Manipulación del tiempo"}}""";
        });
        using var http = new HttpClient(handler);
        var service = new TagLocalizationService(http, new Settings());
        var localized = await service.LocalizeAsync(["Time Manipulation", "Shounen"]);
        Assert.Contains("Manipulación del tiempo", localized);
        Assert.Contains("Shounen", localized);
    }
}
