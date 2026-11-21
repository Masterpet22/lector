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

    private static readonly Regex NumberValue = new(
        @"(?<value>\d{1,3}|[IVXLCDM]{1,6})$",
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
                {
                    var number = match.Groups["number"].Value.Trim();
                    return (name, $"#{NumberValue.Match(number).Groups["value"].Value.ToUpperInvariant()}");
                }
            }
        }

        return (value, null);
    }

    /// <summary>
    /// Reemplaza el nombre base del documento por <paramref name="newName"/> conservando
    /// su numeración de volumen/tomo si ya la tenía.
    /// </summary>
    public static string ReplaceName(string currentTitle, string newName)
    {
        var cleanNew = (newName ?? string.Empty).Trim();
        if (cleanNew.Length == 0) return currentTitle;

        var (newBaseName, _) = Split(cleanNew);
        if (string.IsNullOrWhiteSpace(newBaseName))
            newBaseName = cleanNew;

        var value = (currentTitle ?? string.Empty).Trim();
        foreach (var pattern in new[] { Suffix, Prefix })
        {
            var match = pattern.Match(value);
            if (match.Success)
            {
                var name = match.Groups["name"].Value.Trim();
                if (name.Length > 0)
                {
                    var numberToken = match.Groups["number"].Value.Trim();
                    return $"{newBaseName} - {numberToken}";
                }
            }
        }

        return cleanNew;
    }
}
