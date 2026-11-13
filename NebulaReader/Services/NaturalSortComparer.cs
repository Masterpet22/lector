namespace NebulaReader.Services;

/// <summary>
/// Comparador alfanumérico natural para ordenar páginas de cómics (ej. pagina_2.jpg antes que pagina_10.jpg).
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public static NaturalSortComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ix = 0;
        var iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            var isDigitX = char.IsDigit(x[ix]);
            var isDigitY = char.IsDigit(y[iy]);

            if (isDigitX && isDigitY)
            {
                var result = CompareNumericChunks(x, ref ix, y, ref iy);
                if (result != 0) return result;
                continue;
            }

            if (isDigitX != isDigitY)
            {
                return isDigitX ? -1 : 1;
            }

            var cx = char.ToUpperInvariant(x[ix]);
            var cy = char.ToUpperInvariant(y[iy]);
            if (cx != cy) return cx.CompareTo(cy);

            ix++;
            iy++;
        }

        return (x.Length - ix).CompareTo(y.Length - iy);
    }

    private static int CompareNumericChunks(string x, ref int ix, string y, ref int iy)
    {
        var startX = ix;
        while (ix < x.Length && char.IsDigit(x[ix])) ix++;

        var startY = iy;
        while (iy < y.Length && char.IsDigit(y[iy])) iy++;

        var lenX = ix - startX;
        var lenY = iy - startY;

        // Omitir ceros a la izquierda para comparar magnitud sin riesgo de desbordamiento
        var nzX = startX;
        while (nzX < ix && x[nzX] == '0') nzX++;

        var nzY = startY;
        while (nzY < iy && y[nzY] == '0') nzY++;

        var sigLenX = ix - nzX;
        var sigLenY = iy - nzY;

        if (sigLenX != sigLenY)
        {
            return sigLenX.CompareTo(sigLenY);
        }

        for (var i = 0; i < sigLenX; i++)
        {
            var cmp = x[nzX + i].CompareTo(y[nzY + i]);
            if (cmp != 0) return cmp;
        }

        // Si numéricamente son iguales (p. ej. "02" vs "2"), desempata la longitud original
        return lenX.CompareTo(lenY);
    }
}

/// <summary>
/// Utilidades compartidas para filtrar entradas de imagen en archivos comprimidos CBZ/CBR.
/// </summary>
internal static class ComicArchiveEntryFilter
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    public static bool IsValidImageEntry(string? entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) return false;

        var normalized = entryPath.Replace('\\', '/');
        if (normalized.EndsWith('/')) return false;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;

        foreach (var segment in segments)
        {
            if (string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Ignorar archivos/carpetas ocultos estilo macOS/Unix (._pagina.jpg, .DS_Store, etc.)
            if (segment.StartsWith('.'))
            {
                return false;
            }
        }

        var extension = Path.GetExtension(segments[^1]);
        return SupportedImageExtensions.Contains(extension);
    }
}

