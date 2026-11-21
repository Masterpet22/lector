namespace NebulaReader.Services;

public sealed record TagLanguageOption(string Code, string Name);

public static class TagLanguages
{
    public static IReadOnlyList<TagLanguageOption> Options { get; } =
    [new("es", "Español"), new("en", "Inglés"), new("fr", "Francés"), new("pt", "Portugués"),
     new("it", "Italiano"), new("de", "Alemán"), new("ja", "Japonés")];

    public static string Normalize(string? code) => Options.FirstOrDefault(option =>
        string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? "es";
}
