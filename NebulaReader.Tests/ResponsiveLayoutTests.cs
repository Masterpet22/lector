using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class ResponsiveLayoutTests
{
    [Fact]
    public void MediumCardsAdaptToTheAvailableWidth()
    {
        Assert.Equal(5, ResponsiveLayout.GetColumnCount(1078, 180, 10, 12));
        Assert.Equal(4, ResponsiveLayout.GetColumnCount(752, 180, 10, 12));
        Assert.Equal(2, ResponsiveLayout.GetColumnCount(500, 180, 10, 12));
        Assert.Equal(1, ResponsiveLayout.GetColumnCount(276, 180, 10, 12));
    }

    [Theory]
    [InlineData(1060, 5)]
    [InlineData(750, 4)]
    [InlineData(500, 2)]
    [InlineData(320, 1)]
    public void MediumCardRowsNeverOverflow(double width, int expectedColumns)
    {
        var metrics = ResponsiveLayout.GetCardGridMetrics(width, 180, 210, 10);

        Assert.Equal(expectedColumns, metrics.Columns);
        Assert.True(metrics.Columns * metrics.CardWidth + (metrics.Columns - 1) * 10 <= width + 0.001);
        if (expectedColumns > 1)
            Assert.InRange(metrics.CardWidth, 180, 210);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 5, 1)]
    [InlineData(21, 5, 5)]
    public void CardRowsPreserveOrderAndDoNotStretchToFill(int itemCount, int columns, int expectedRows)
    {
        var items = Enumerable.Range(0, itemCount).ToArray();
        var rows = ResponsiveLayout.GroupIntoRows(items, columns);

        Assert.Equal(expectedRows, rows.Count);
        Assert.All(rows, row => Assert.InRange(row.Count, 1, columns));
        Assert.Equal(items, rows.SelectMany(row => row));
    }

    [Fact]
    public void EmptySearchProducesNoCardRows()
    {
        Assert.Empty(ResponsiveLayout.GroupIntoRows(Array.Empty<int>(), 5));
    }

    [Fact]
    public void GridColumnsFitWithinAvailableWidthAcrossResizeRange()
    {
        for (var width = 200.0; width <= 2000; width += 10)
        {
            var columns = ResponsiveLayout.GetColumnCount(width, 180, 10, 12);
            Assert.InRange(columns, 1, 12);
            Assert.True(columns * 180 + (columns - 1) * 10 <= width);
        }
    }

    [Fact]
    public void LibraryColumnsUseTheSmallestVisibleViewport()
    {
        var visibleWidth = ResponsiveLayout.GetVisibleWidth(780, 1000, 760);

        Assert.Equal(760, visibleWidth);
        Assert.Equal(3, ResponsiveLayout.GetColumnCount(visibleWidth - 8, 200, 10, 12));
    }

    [Fact]
    public void WrappedRecentCardsNeverOverflowTheirRow()
    {
        for (var width = 200.0; width <= 2000; width += 10)
        {
            var metrics = ResponsiveLayout.GetRecentCardGridMetrics(width);
            var columns = metrics.Columns;
            var cardWidth = metrics.CardWidth;
            Assert.True(ResponsiveLayout.RecentCardLeftInset + columns * cardWidth + (columns - 1) * 10
                + ResponsiveLayout.RecentCardRightInset <= width + 0.001);
            Assert.InRange(cardWidth, 0, 300);
        }
    }

    [Theory]
    [InlineData(320, 1)]
    [InlineData(540, 1)]
    [InlineData(1068, 4)]
    public void RecentCardRowsReserveBothEdgesAndFitTheViewport(double viewport, int expectedColumns)
    {
        var metrics = ResponsiveLayout.GetRecentCardGridMetrics(viewport);
        var rows = ResponsiveLayout.GroupIntoRows(Enumerable.Range(1, 6), metrics.Columns);

        Assert.Equal(expectedColumns, metrics.Columns);
        Assert.Equal(Enumerable.Range(1, 6), rows.SelectMany(row => row));
        Assert.True(ResponsiveLayout.RecentCardLeftInset > 0);
        Assert.All(rows, row => Assert.True(ResponsiveLayout.RecentCardLeftInset + row.Count * metrics.CardWidth
            + (row.Count - 1) * 10 + ResponsiveLayout.RecentCardRightInset <= viewport));
    }
}
