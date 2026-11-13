using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NebulaReader.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Readers;

namespace NebulaReader.Services;

/// <summary>
/// Metadatos extraídos de un documento (serie/saga, etiquetas y título embebido).
/// </summary>
public sealed record BookMetadata(
    string? SeriesName,
    IReadOnlyList<string> Tags,
    string? EmbeddedTitle = null)
{
    public string? FormattedTags => Tags.Count == 0 ? null : string.Join(", ", Tags);
}

/// <summary>
/// Resultado de agrupación de biblioteca: representa una saga con varios documentos o un documento suelto.
/// </summary>
public sealed record LibraryGroupBucket<T>(
    bool IsGroup,
    string DisplayName,
    IReadOnlyList<T> Items,
    IReadOnlyList<string> CommonTags);

/// <summary>
/// Extrae etiquetas y nombres de serie/saga desde ComicInfo.xml (CBZ/CBR), metadatos PDF (XMP / Info)
/// y normalización inteligente de títulos, y agrupa documentos pertenecientes a la misma serie o saga.
/// </summary>
public static partial class BookMetadataExtractor
{
    private const int PdfScanWindowBytes = 262_144;

    private static readonly HashSet<string> GenericTagStoplist = new(StringComparer.OrdinalIgnoreCase)
    {
        "manga", "manhwa", "manhua", "comic", "comics", "cómic", "cómics", "bd", "graphic novel",
        "novela grafica", "novela gráfica", "pdf", "cbz", "cbr", "epub", "ebook", "book", "libro",
        "action", "accion", "acción", "adventure", "aventura", "adventures", "aventuras",
        "fantasy", "fantasia", "fantasía", "sci-fi", "scifi", "science fiction", "ciencia ficcion",
        "ciencia ficción", "horror", "terror", "romance", "comedy", "comedia", "drama",
        "mystery", "misterio", "thriller", "suspense", "suspenso", "superhero", "superheroes",
        "superhéroes", "slice of life", "seinen", "shonen", "shounen", "shojo", "shoujo", "isekai",
        "english", "ingles", "inglés", "spanish", "espanol", "español", "es", "en", "jp", "raw"
    };

    public static BookMetadata ExtractFromStream(Stream stream, BookFormat format, string? fallbackTitle = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var extracted = format switch
            {
                BookFormat.Cbz => ExtractFromCbzStream(stream),
                BookFormat.Cbr => ExtractFromCbrStream(stream),
                BookFormat.Pdf => ExtractFromPdfStream(stream),
                _ => new BookMetadata(null, Array.Empty<string>(), null)
            };

            if (string.IsNullOrWhiteSpace(extracted.SeriesName))
            {
                var seriesFromTags = ExtractExplicitSeriesFromTags(extracted.Tags);
                if (!string.IsNullOrWhiteSpace(seriesFromTags))
                {
                    extracted = extracted with { SeriesName = seriesFromTags };
                }
            }

            if (string.IsNullOrWhiteSpace(extracted.SeriesName))
            {
                var candidate = ExtractSeriesCandidateFromTitle(extracted.EmbeddedTitle ?? fallbackTitle);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    // Guardamos el candidato solo como referencia al agrupar, o lo devolvemos limpio cuando
                    // proviene de un patrón explícito con separador/volumen. No sobreescribimos un título único.
                    return extracted;
                }
            }

            return extracted;
        }
        catch
        {
            return new BookMetadata(null, Array.Empty<string>(), null);
        }
    }

    public static BookMetadata ParseComicInfoXml(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);

        var doc = XDocument.Load(xmlStream, LoadOptions.None);
        var root = doc.Root;
        if (root == null)
        {
            return new BookMetadata(null, Array.Empty<string>(), null);
        }

        string? ReadElement(string localName)
        {
            var value = root.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                ?.Value;
            return NormalizeDisplayText(value);
        }

        var series = ReadElement("Series")
            ?? ReadElement("LocalizedSeries")
            ?? ReadElement("SeriesGroup")
            ?? ReadElement("StoryArc");

        var title = ReadElement("Title");

        var rawTagSources = new[]
        {
            ReadElement("Tags"),
            ReadElement("Genre"),
            ReadElement("SeriesGroup"),
            ReadElement("StoryArc")
        };

        var tags = ParseTagList(rawTagSources);
        series ??= ExtractExplicitSeriesFromTags(tags);

        return new BookMetadata(series, tags, title);
    }

    public static BookMetadata ParsePdfMetadataText(string pdfText)
    {
        if (string.IsNullOrEmpty(pdfText))
        {
            return new BookMetadata(null, Array.Empty<string>(), null);
        }

        string? series = null;
        string? embeddedTitle = null;
        var tags = new List<string>();

        // 1. Intentar leer bloque XMP embebido (<x:xmpmeta ...> o <rdf:RDF ...>)
        var xmpXml = ExtractXmpPacket(pdfText);
        if (!string.IsNullOrWhiteSpace(xmpXml))
        {
            try
            {
                var doc = XDocument.Parse(xmpXml);
                var descendants = doc.Descendants().ToList();

                series ??= NormalizeDisplayText(
                    descendants.FirstOrDefault(e =>
                        e.Name.LocalName.Equals("series", StringComparison.OrdinalIgnoreCase))?.Value);

                if (string.IsNullOrWhiteSpace(series))
                {
                    var calibreSeriesAttr = descendants
                        .FirstOrDefault(e =>
                            e.Name.LocalName.Equals("meta", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(e.Attribute("name")?.Value, "calibre:series", StringComparison.OrdinalIgnoreCase))
                        ?.Attribute("content")?.Value;
                    series = NormalizeDisplayText(calibreSeriesAttr);
                }

                var dcTitle = descendants
                    .Where(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(e => e.Descendants().Where(d => d.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
                    .Select(li => NormalizeDisplayText(li.Value))
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                embeddedTitle ??= dcTitle;

                var xmpSubjects = descendants
                    .Where(e => e.Name.LocalName.Equals("subject", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(e => e.Descendants().Where(d => d.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
                    .Select(li => li.Value);
                tags.AddRange(ParseTagList(xmpSubjects));

                var xmpKeywords = descendants
                    .Where(e => e.Name.LocalName.Equals("Keywords", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Value);
                tags.AddRange(ParseTagList(xmpKeywords));
            }
            catch
            {
                // XMP malformado: continuar con diccionario /Info
            }
        }

        // 2. Leer diccionario PDF /Info (/Series, /Saga, /Keywords, /Subject, /Title)
        series ??= ExtractPdfInfoString(pdfText, "Series") ?? ExtractPdfInfoString(pdfText, "Saga");
        embeddedTitle ??= ExtractPdfInfoString(pdfText, "Title");

        var infoKeywords = ExtractPdfInfoString(pdfText, "Keywords");
        if (!string.IsNullOrWhiteSpace(infoKeywords))
        {
            tags.AddRange(ParseTagList([infoKeywords]));
        }

        var infoSubject = ExtractPdfInfoString(pdfText, "Subject");
        if (!string.IsNullOrWhiteSpace(infoSubject))
        {
            var explicitFromSubject = ExtractExplicitSeriesFromTags([infoSubject]);
            series ??= explicitFromSubject;

            if (infoSubject.Length <= 80 &&
                !infoSubject.Contains("://", StringComparison.OrdinalIgnoreCase))
            {
                tags.AddRange(ParseTagList([infoSubject]));
            }
        }

        var deduplicatedTags = DeduplicateTags(tags);
        series ??= ExtractExplicitSeriesFromTags(deduplicatedTags);

        return new BookMetadata(series, deduplicatedTags, embeddedTitle);
    }

    public static IReadOnlyList<LibraryGroupBucket<T>> BuildLibraryGroups<T>(
        IReadOnlyList<T> items,
        Func<T, string> titleSelector,
        Func<T, string?> seriesSelector,
        Func<T, string?> tagsSelector)
    {
        if (items.Count == 0)
        {
            return Array.Empty<LibraryGroupBucket<T>>();
        }

        var sortedItems = items
            .OrderBy(titleSelector, NaturalSortComparer.Instance)
            .ToList();

        var assignedKeys = new string?[sortedItems.Count];
        var displayNameByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Fase 1: Serie o saga explícita persistida en metadatos (ComicInfo <Series>, PDF /Series, serie:...)
        for (var i = 0; i < sortedItems.Count; i++)
        {
            var explicitSeries = NormalizeDisplayText(seriesSelector(sortedItems[i]));
            if (string.IsNullOrWhiteSpace(explicitSeries))
            {
                continue;
            }

            var key = NormalizeSeriesKey(explicitSeries);
            assignedKeys[i] = key;
            if (!displayNameByKey.ContainsKey(key))
            {
                displayNameByKey[key] = explicitSeries;
            }
        }

        // Fase 2: Para documentos sin serie explícita, inferir nombre común por patrón de título/archivo
        // (p. ej. "Batman #001", "Batman Vol. 2", "Dune 1", "One Piece Cap 03")
        var titleCandidateCounts = new Dictionary<string, (string Display, int Count)>(StringComparer.OrdinalIgnoreCase);
        var itemTitleCandidates = new (string Key, string Display)?[sortedItems.Count];

        for (var i = 0; i < sortedItems.Count; i++)
        {
            if (assignedKeys[i] != null)
            {
                continue;
            }

            var candidate = ExtractSeriesCandidateFromTitle(titleSelector(sortedItems[i]));
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var key = NormalizeSeriesKey(candidate);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            itemTitleCandidates[i] = (key, candidate);
            if (titleCandidateCounts.TryGetValue(key, out var existing))
            {
                titleCandidateCounts[key] = (existing.Display, existing.Count + 1);
            }
            else
            {
                titleCandidateCounts[key] = (candidate, 1);
            }
        }

        for (var i = 0; i < sortedItems.Count; i++)
        {
            if (assignedKeys[i] != null || itemTitleCandidates[i] is not { } candidate)
            {
                continue;
            }

            // Agrupamos por candidato de título si existen 2+ documentos con esa serie
            // O si ya existía una serie explícita con exactamente la misma clave.
            var matchesExplicitGroup = displayNameByKey.ContainsKey(candidate.Key);
            var matchesMultipleByTitle = titleCandidateCounts.TryGetValue(candidate.Key, out var countInfo) && countInfo.Count >= 2;

            if (matchesExplicitGroup || matchesMultipleByTitle)
            {
                assignedKeys[i] = candidate.Key;
                displayNameByKey.TryAdd(candidate.Key, candidate.Display);
            }
        }

        // Fase 3: Para documentos aún sueltos con prefijo común separado por " - " o ": "
        var prefixCandidateCounts = new Dictionary<string, (string Display, int Count)>(StringComparer.OrdinalIgnoreCase);
        var itemPrefixCandidates = new (string Key, string Display)?[sortedItems.Count];

        for (var i = 0; i < sortedItems.Count; i++)
        {
            if (assignedKeys[i] != null)
            {
                continue;
            }

            var prefix = ExtractSeparatorPrefix(titleSelector(sortedItems[i]));
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            var key = NormalizeSeriesKey(prefix);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            itemPrefixCandidates[i] = (key, prefix);
            if (prefixCandidateCounts.TryGetValue(key, out var existing))
            {
                prefixCandidateCounts[key] = (existing.Display, existing.Count + 1);
            }
            else
            {
                prefixCandidateCounts[key] = (prefix, 1);
            }
        }

        for (var i = 0; i < sortedItems.Count; i++)
        {
            if (assignedKeys[i] != null || itemPrefixCandidates[i] is not { } candidate)
            {
                continue;
            }

            if (displayNameByKey.ContainsKey(candidate.Key) ||
                (prefixCandidateCounts.TryGetValue(candidate.Key, out var countInfo) && countInfo.Count >= 2))
            {
                assignedKeys[i] = candidate.Key;
                displayNameByKey.TryAdd(candidate.Key, candidate.Display);
            }
        }

        // Fase 4: Para documentos aún sueltos que comparten una etiqueta específica (no genérica de género/idioma)
        var tagCounts = new Dictionary<string, (string Display, int Count)>(StringComparer.OrdinalIgnoreCase);
        var parsedItemTags = new IReadOnlyList<string>[sortedItems.Count];

        for (var i = 0; i < sortedItems.Count; i++)
        {
            var rawTags = tagsSelector(sortedItems[i]);
            IReadOnlyList<string> tags = string.IsNullOrWhiteSpace(rawTags)
                ? Array.Empty<string>()
                : ParseTagList([rawTags]);
            parsedItemTags[i] = tags;

            if (assignedKeys[i] != null)
            {
                continue;
            }

            foreach (var tag in tags)
            {
                if (IsGenericTag(tag))
                {
                    continue;
                }

                var key = NormalizeSeriesKey(tag);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (tagCounts.TryGetValue(key, out var existing))
                {
                    tagCounts[key] = (existing.Display, existing.Count + 1);
                }
                else
                {
                    tagCounts[key] = (tag, 1);
                }
            }
        }

        for (var i = 0; i < sortedItems.Count; i++)
        {
            if (assignedKeys[i] != null || parsedItemTags[i].Count == 0)
            {
                continue;
            }

            string? bestKey = null;
            var bestCount = 1;
            string? bestDisplay = null;

            foreach (var tag in parsedItemTags[i])
            {
                if (IsGenericTag(tag))
                {
                    continue;
                }

                var key = NormalizeSeriesKey(tag);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (displayNameByKey.ContainsKey(key))
                {
                    bestKey = key;
                    bestDisplay = displayNameByKey[key];
                    bestCount = int.MaxValue;
                    break;
                }

                if (tagCounts.TryGetValue(key, out var info) && info.Count > bestCount)
                {
                    bestCount = info.Count;
                    bestKey = key;
                    bestDisplay = info.Display;
                }
            }

            if (bestKey != null && bestDisplay != null)
            {
                assignedKeys[i] = bestKey;
                displayNameByKey.TryAdd(bestKey, bestDisplay);
            }
        }

        // Construir buckets: solo creamos grupo visual si hay >= 2 documentos en la serie/saga.
        var groupedBuckets = new Dictionary<string, List<(T Item, IReadOnlyList<string> Tags)>>(StringComparer.OrdinalIgnoreCase);
        var ungroupedItems = new List<(T Item, IReadOnlyList<string> Tags)>();

        for (var i = 0; i < sortedItems.Count; i++)
        {
            var key = assignedKeys[i];
            if (key == null)
            {
                ungroupedItems.Add((sortedItems[i], parsedItemTags[i]));
                continue;
            }

            if (!groupedBuckets.TryGetValue(key, out var list))
            {
                list = [];
                groupedBuckets[key] = list;
            }

            list.Add((sortedItems[i], parsedItemTags[i]));
        }

        var result = new List<LibraryGroupBucket<T>>();

        foreach (var (key, entries) in groupedBuckets)
        {
            if (entries.Count >= 2)
            {
                var orderedGroupItems = entries
                    .Select(e => e.Item)
                    .OrderBy(titleSelector, NaturalSortComparer.Instance)
                    .ToList();

                var commonTags = DeduplicateTags(entries.SelectMany(e => e.Tags)).Take(4).ToList();
                var displayName = displayNameByKey.TryGetValue(key, out var name)
                    ? name
                    : titleSelector(orderedGroupItems[0]);

                result.Add(new LibraryGroupBucket<T>(
                    IsGroup: true,
                    DisplayName: displayName,
                    Items: orderedGroupItems,
                    CommonTags: commonTags));
            }
            else
            {
                ungroupedItems.Add(entries[0]);
            }
        }

        foreach (var (item, itemTags) in ungroupedItems)
        {
            result.Add(new LibraryGroupBucket<T>(
                IsGroup: false,
                DisplayName: titleSelector(item),
                Items: [item],
                CommonTags: itemTags.Take(3).ToList()));
        }

        result.Sort((a, b) => NaturalSortComparer.Instance.Compare(a.DisplayName, b.DisplayName));
        return result;
    }

    public static string? ExtractSeriesCandidateFromTitle(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return null;
        }

        var cleaned = rawTitle.Replace('_', ' ').Trim();

        // Quitar etiquetas finales entre paréntesis o corchetes: "(2024)", "[Digital]", "(Spa)"
        cleaned = TrailingBracketRegex().Replace(cleaned, string.Empty).Trim();

        // Quitar sufijos explícitos de volumen/tomo/capítulo/número de manera iterativa (p. ej. "Saga Vol. 1 Cap. 2")
        for (var pass = 0; pass < 3; pass++)
        {
            var next = TrailingVolumeOrIssueRegex().Replace(cleaned, string.Empty).Trim(' ', '-', '–', '—', ':', '_');
            if (next.Equals(cleaned, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(next))
            {
                break;
            }
            cleaned = next;
        }

        // Quitar número final suelto ("Batman 001", "Dune 2", "Saga - 04")
        var strippedTrailingNum = TrailingBareNumberRegex().Replace(cleaned, string.Empty).Trim(' ', '-', '–', '—', ':', '_');
        if (!string.IsNullOrWhiteSpace(strippedTrailingNum) && strippedTrailingNum.Length >= 2)
        {
            cleaned = strippedTrailingNum;
        }

        cleaned = MultiWhitespaceRegex().Replace(cleaned, " ").Trim();
        return cleaned.Length >= 2 ? cleaned : null;
    }

    private static string? ExtractSeparatorPrefix(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return null;
        }

        var cleaned = TrailingBracketRegex().Replace(rawTitle.Replace('_', ' ').Trim(), string.Empty).Trim();
        var dashIdx = cleaned.IndexOf(" - ", StringComparison.Ordinal);
        var colonIdx = cleaned.IndexOf(": ", StringComparison.Ordinal);

        var splitIdx = (dashIdx, colonIdx) switch
        {
            (>= 3, >= 3) => Math.Min(dashIdx, colonIdx),
            (>= 3, _) => dashIdx,
            (_, >= 3) => colonIdx,
            _ => -1
        };

        if (splitIdx < 3)
        {
            return null;
        }

        var prefix = MultiWhitespaceRegex().Replace(cleaned[..splitIdx], " ").Trim();
        return prefix.Length >= 3 ? prefix : null;
    }

    private static BookMetadata ExtractFromCbzStream(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var comicInfoEntry = zip.Entries.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.Name) &&
            e.Name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));

        if (comicInfoEntry == null)
        {
            return new BookMetadata(null, Array.Empty<string>(), null);
        }

        using var entryStream = comicInfoEntry.Open();
        return ParseComicInfoXml(entryStream);
    }

    private static BookMetadata ExtractFromCbrStream(Stream stream)
    {
        using var archive = RarArchive.OpenArchive(stream, new SharpCompress.Readers.ReaderOptions { LeaveStreamOpen = true });
        var comicInfoEntry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory &&
            !string.IsNullOrWhiteSpace(e.Key) &&
            Path.GetFileName(e.Key).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));

        if (comicInfoEntry == null)
        {
            return new BookMetadata(null, Array.Empty<string>(), null);
        }

        using var entryStream = comicInfoEntry.OpenEntryStream();
        using var memory = new MemoryStream();
        entryStream.CopyTo(memory);
        memory.Position = 0;
        return ParseComicInfoXml(memory);
    }

    private static BookMetadata ExtractFromPdfStream(Stream stream)
    {
        if (!stream.CanSeek)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[Math.Min(PdfScanWindowBytes, 65536)];
            int read;
            while (ms.Length < PdfScanWindowBytes * 2 && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }

            var partialText = Encoding.Latin1.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            return ParsePdfMetadataText(partialText);
        }

        var length = stream.Length;
        if (length <= PdfScanWindowBytes * 2)
        {
            var allBytes = new byte[length];
            stream.Position = 0;
            ReadExactlyUpTo(stream, allBytes);
            return ParsePdfMetadataText(Encoding.Latin1.GetString(allBytes));
        }

        var headBytes = new byte[PdfScanWindowBytes];
        stream.Position = 0;
        var headRead = ReadExactlyUpTo(stream, headBytes);

        var tailBytes = new byte[PdfScanWindowBytes];
        stream.Position = length - PdfScanWindowBytes;
        var tailRead = ReadExactlyUpTo(stream, tailBytes);

        var combinedText = string.Concat(
            Encoding.Latin1.GetString(headBytes, 0, headRead),
            "\n",
            Encoding.Latin1.GetString(tailBytes, 0, tailRead));

        return ParsePdfMetadataText(combinedText);
    }

    private static int ReadExactlyUpTo(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    private static string? ExtractXmpPacket(string text)
    {
        var start = text.IndexOf("<x:xmpmeta", StringComparison.OrdinalIgnoreCase);
        var endTag = "</x:xmpmeta>";
        if (start < 0)
        {
            start = text.IndexOf("<rdf:RDF", StringComparison.OrdinalIgnoreCase);
            endTag = "</rdf:RDF>";
        }

        if (start < 0)
        {
            return null;
        }

        var end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return null;
        }

        var rawPacket = text.Substring(start, end - start + endTag.Length);
        // Si el stream fue leído como Latin-1 pero contiene UTF-8 válido en el paquete XML, recodificar a UTF-8
        var utf8Bytes = Encoding.Latin1.GetBytes(rawPacket);
        var decoded = Encoding.UTF8.GetString(utf8Bytes);
        return decoded.Contains('\uFFFD') ? rawPacket : decoded;
    }

    private static string? ExtractPdfInfoString(string pdfText, string keyName)
    {
        var literalMatch = Regex.Match(
            pdfText,
            $@"/{Regex.Escape(keyName)}\s*\((?<val>(?:\\.|[^\\)])*)\)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (literalMatch.Success)
        {
            var decoded = DecodePdfLiteralString(literalMatch.Groups["val"].Value);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        var hexMatch = Regex.Match(
            pdfText,
            $@"/{Regex.Escape(keyName)}\s*<(?<hex>[0-9A-Fa-f\s]+)>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (hexMatch.Success)
        {
            var decoded = DecodePdfHexString(hexMatch.Groups["hex"].Value);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        return null;
    }

    private static string? DecodePdfLiteralString(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var bytes = new List<byte>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (ch == '\\' && i + 1 < raw.Length)
            {
                var next = raw[++i];
                switch (next)
                {
                    case 'n': bytes.Add((byte)'\n'); break;
                    case 'r': bytes.Add((byte)'\r'); break;
                    case 't': bytes.Add((byte)'\t'); break;
                    case 'b': bytes.Add((byte)'\b'); break;
                    case 'f': bytes.Add((byte)'\f'); break;
                    case '(' or ')' or '\\': bytes.Add((byte)next); break;
                    case '\r':
                        if (i + 1 < raw.Length && raw[i + 1] == '\n') i++;
                        break;
                    case '\n':
                        break;
                    default:
                        if (next is >= '0' and <= '7')
                        {
                            var octalDigits = 1;
                            var val = next - '0';
                            while (octalDigits < 3 && i + 1 < raw.Length && raw[i + 1] is >= '0' and <= '7')
                            {
                                val = (val * 8) + (raw[++i] - '0');
                                octalDigits++;
                            }
                            bytes.Add((byte)(val & 0xFF));
                        }
                        else
                        {
                            bytes.Add((byte)next);
                        }
                        break;
                }
            }
            else
            {
                bytes.Add((byte)(ch & 0xFF));
            }
        }

        return DecodePdfByteBuffer(bytes.ToArray());
    }

    private static string? DecodePdfHexString(string rawHex)
    {
        var hex = Regex.Replace(rawHex, @"\s+", string.Empty);
        if (hex.Length == 0)
        {
            return null;
        }

        if (hex.Length % 2 == 1)
        {
            hex += "0";
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }

        return DecodePdfByteBuffer(bytes);
    }

    private static string? DecodePdfByteBuffer(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else
        {
            var utf8 = Encoding.UTF8.GetString(bytes);
            text = utf8.Contains('\uFFFD') ? Encoding.Latin1.GetString(bytes) : utf8;
        }

        return NormalizeDisplayText(text);
    }

    private static string? ExtractExplicitSeriesFromTags(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            var match = ExplicitSeriesPrefixRegex().Match(tag);
            if (match.Success)
            {
                var candidate = NormalizeDisplayText(match.Groups["val"].Value);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static List<string> ParseTagList(IEnumerable<string?> sources)
    {
        var raw = new List<string>();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var parts = source.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var normalized = NormalizeDisplayText(part);
                if (!string.IsNullOrWhiteSpace(normalized) && normalized.Length <= 60)
                {
                    raw.Add(normalized);
                }
            }
        }

        return DeduplicateTags(raw);
    }

    private static List<string> DeduplicateTags(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var tag in tags)
        {
            var normalized = NormalizeDisplayText(tag);
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
            {
                list.Add(normalized);
            }
        }
        return list;
    }

    private static bool IsGenericTag(string tag)
    {
        var normalized = NormalizeDisplayText(tag);
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Length < 3
            || GenericTagStoplist.Contains(normalized);
    }

    private static string? NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = MultiWhitespaceRegex().Replace(value.Replace('\0', ' '), " ").Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeSeriesKey(string value)
    {
        return MultiWhitespaceRegex().Replace(value, " ").Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"(?:\s*(?:\[[^\]]*\]|\([^)]*\)))+$", RegexOptions.Compiled)]
    private static partial Regex TrailingBracketRegex();

    [GeneratedRegex(@"(?:\s*[-–—:_]\s*|\s+)(?:vol(?:umen|\.)?|tomo|t\.?|cap(?:[ií]tulo|\.)?|ch(?:apter|\.)?|num(?:ero|éro|\.)?|n[°ºo.]?|part(?:e)?|libro|book|issue|ed(?:ici[oó]n)?|ep(?:isodio)?|#)\s*\d+(?:\.\d+)?(?:\s*(?:de|of|/|-)\s*\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrailingVolumeOrIssueRegex();

    [GeneratedRegex(@"(?:\s*[-–—_]\s*|\s+)#?\d+(?:\.\d+)?$", RegexOptions.Compiled)]
    private static partial Regex TrailingBareNumberRegex();

    [GeneratedRegex(@"^(?:serie|series|saga)\s*:\s*(?<val>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ExplicitSeriesPrefixRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();
}
