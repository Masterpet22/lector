namespace NebulaReader.Services;

/// <summary>Medidas compartidas para que las cuadrículas no creen columnas fuera del área visible.</summary>
public static class ResponsiveLayout
{
    public const double RecentCardRightInset = 24;
    public const double RecentCardLeftInset = 8;

    public readonly record struct CardGridMetrics(int Columns, double CardWidth);

    public static CardGridMetrics GetCardGridMetrics(double availableWidth, double minimumCardWidth,
        double maximumCardWidth, double spacing, int maximumColumns = 12)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            return new CardGridMetrics(1, 0);

        var columns = GetColumnCount(availableWidth, minimumCardWidth, spacing, maximumColumns);
        var cellWidth = Math.Max(0, (availableWidth - (columns - 1) * spacing) / columns);
        // En un teléfono con una sola columna se usa el ancho disponible para conservar las acciones.
        var cardWidth = columns == 1 && availableWidth <= 420
            ? cellWidth
            : Math.Min(maximumCardWidth, cellWidth);
        return new CardGridMetrics(columns, cardWidth);
    }

    public static IReadOnlyList<IReadOnlyList<T>> GroupIntoRows<T>(IEnumerable<T> items, int columns) =>
        items.Chunk(Math.Max(1, columns))
            .Select(row => (IReadOnlyList<T>)row)
            .ToList();

    public static double GetVisibleWidth(double availableWidth, params double[] measuredWidths)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 0;

        foreach (var width in measuredWidths)
        {
            if (double.IsFinite(width) && width > 0)
                availableWidth = Math.Min(availableWidth, width);
        }

        return availableWidth;
    }

    public static int GetColumnCount(double availableWidth, double minimumItemWidth, double spacing, int maximumColumns)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 1;
        var count = Math.Floor((availableWidth + spacing) / (minimumItemWidth + spacing));
        return (int)Math.Clamp(count, 1, maximumColumns);
    }

    public static double GetWrappedCardWidth(double availableWidth, double preferredWidth,
        double maximumWidth, double trailingMargin, int maximumColumns)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 0;
        var columns = (int)Math.Clamp(Math.Floor(availableWidth / (preferredWidth + trailingMargin)), 1, maximumColumns);
        return Math.Max(0, Math.Min(maximumWidth, availableWidth / columns - trailingMargin));
    }

    public static CardGridMetrics GetRecentCardGridMetrics(double viewportWidth)
    {
        var metrics = GetCardGridMetrics(
            Math.Max(0, viewportWidth - RecentCardLeftInset - RecentCardRightInset), 250, 300, 10, 8);
        return metrics with { CardWidth = Math.Floor(Math.Min(300, metrics.CardWidth)) };
    }

    public static double GetRecentCardWidth(double viewportWidth) => GetRecentCardGridMetrics(viewportWidth).CardWidth;
}
