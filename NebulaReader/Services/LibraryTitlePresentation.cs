using System.Text.RegularExpressions;

namespace NebulaReader.Services;

/// <summary>Separa una numeración editorial del nombre mostrado sin modificar el título original.</summary>
public static class LibraryTitlePresentation
{
    private const string Number = @"(?:#\s*\d{1,3}|(?:vol(?:umen)?|tomo|cap(?:ítulo|itulo)?|n[º°o.]?)\.?\s*(?:\d{1,3}|[IVXLCDM]{1,6})|\d{1,3})";

    private static readonly Regex Suffix = new(
        @"^(?<name>.+?)(?:\s+|\s*[-–—:]\s*)(?<number>" + Number + @")$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Prefix = new(
        @"^(?<number>" + Number + @")\s*[-–—:]\s*(?<name>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (string Name, string? Number) Split(string title)
    {
        var value = title.Trim();
        foreach (var pattern in new[] { Suffix, Prefix })
        {
            var match = pattern.Match(value);
            if (match.Success)
            {
                var name = match.Groups["name"].Value.Trim();
                if (name.Length > 0)
                    return (name, match.Groups["number"].Value.Trim());
            }
        }

        return (value, null);
    }
}
