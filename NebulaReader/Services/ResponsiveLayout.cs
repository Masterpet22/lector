namespace NebulaReader.Services;

/// <summary>Medidas compartidas para que las cuadrículas no creen columnas fuera del área visible.</summary>
public static class ResponsiveLayout
{
    public const double RecentCardRightInset = 24;

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

    public static double GetRecentCardWidth(double viewportWidth) =>
        GetWrappedCardWidth(Math.Max(0, viewportWidth - RecentCardRightInset), 250, 300, 10, 8);
}
