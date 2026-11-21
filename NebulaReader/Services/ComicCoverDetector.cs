using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NebulaReader.Services;

/// <summary>Selecciona la portada sin modificar el orden de lectura de las imágenes.</summary>
internal static class ComicCoverDetector
{
    internal const long MaxComicInfoBytes = 1024 * 1024;

    public static bool IsComicInfoEntry(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var parts = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 &&
            !parts.Any(part => part.StartsWith('.') || part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) &&
            parts[^1].Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase);
    }

    public static int FindCoverPage(IReadOnlyList<string> pageNames, Func<Stream?> openComicInfo)
    {
        if (pageNames.Count == 0) return -1;

        try
        {
            using var stream = openComicInfo();
            if (stream != null)
            {
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaxComicInfoBytes
                });
                var root = XDocument.Load(reader).Root;
                if (root?.Name.LocalName.Equals("ComicInfo", StringComparison.OrdinalIgnoreCase) == true)
                {
                    foreach (var page in root.Elements()
                        .Where(e => e.Name.LocalName.Equals("Pages", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(e => e.Elements())
                        .Where(e => e.Name.LocalName.Equals("Page", StringComparison.OrdinalIgnoreCase)))
                    {
                        var type = ReadAttribute(page, "Type") ?? string.Empty;
                        if (type.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                                .Contains("FrontCover", StringComparer.OrdinalIgnoreCase) &&
                            int.TryParse(ReadAttribute(page, "Image"), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var index) &&
                            index >= 0 && index < pageNames.Count)
                        {
                            return index;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            // Un ComicInfo inválido no impide usar nombres de archivo o la primera página.
        }

        var selected = 0;
        var bestScore = 0;
        for (var i = 0; i < pageNames.Count; i++)
        {
            var score = ScoreName(pageNames[i]);
            if (score > bestScore)
            {
                bestScore = score;
                selected = i;
            }
        }
        return selected;
    }

    private static string? ReadAttribute(XElement element, string name) => element.Attributes()
        .FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static int ScoreName(string path)
    {
        var fileName = path.Replace('\\', '/').Split('/')[^1];
        var stem = Path.GetFileNameWithoutExtension(fileName).Normalize(NormalizationForm.FormD);
        var normalized = new string(stem.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant();
        var tokens = Regex.Split(normalized, "[^a-z0-9]+");
        if (tokens.Any(t => t is "back" or "rear" or "inner" or "inside" or "interior" or "trasera" or "contra" or
                "posterior" or "contraportada" or "contracubierta" ||
                t.StartsWith("backcover", StringComparison.Ordinal) ||
                t.StartsWith("coverback", StringComparison.Ordinal) ||
                t.StartsWith("innercover", StringComparison.Ordinal))) return 0;

        if (tokens.Any(t => t is "front" or "frontcover" or "coverfront" or "portada" or "caratula")) return 30;
        if (tokens.Any(t => t is "cover" or "covers" or "cvr" or "cubierta" ||
                (t.StartsWith("cover", StringComparison.Ordinal) && t.Length > 5 && t[5..].All(char.IsDigit)))) return 20;
        return 0;
    }
}
