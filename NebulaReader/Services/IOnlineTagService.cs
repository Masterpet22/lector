using NebulaReader.Models;

namespace NebulaReader.Services;

public enum OnlineTagSource { Metron, ComicVine, AniList, OpenLibrary, GoogleBooks, Crossref }

public sealed record OnlineTagSuggestion(string Title, string? Author, IReadOnlyList<string> Tags, string SourceUrl)
{
    public string? Publisher { get; init; }
    public DateTime? PublicationDate { get; init; }
    public string? Language { get; init; }
    public string? Description { get; init; }
}

public interface IOnlineTagService
{
    Task<IReadOnlyList<OnlineTagSource>> GetAvailableSourcesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OnlineTagSuggestion>> SearchAsync(BookItem book, OnlineTagSource source, CancellationToken ct = default);
}
