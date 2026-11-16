using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class ResponsiveLayoutTests
{
    [Fact]
    public void DesktopLibraryShowsFiveCompactCardsPerRow()
    {
        Assert.Equal(5, ResponsiveLayout.GetColumnCount(1078, 200, 10, 12));
        Assert.Equal(1, ResponsiveLayout.GetColumnCount(276, 200, 10, 12));
    }

    [Fact]
    public void GridColumnsFitWithinAvailableWidthAcrossResizeRange()
    {
        for (var width = 200.0; width <= 2000; width += 10)
        {
            var columns = ResponsiveLayout.GetColumnCount(width, 200, 10, 12);
            Assert.InRange(columns, 1, 12);
            Assert.True(columns * 200 + (columns - 1) * 10 <= width);
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
            var usableWidth = width - ResponsiveLayout.RecentCardRightInset;
            var columns = Math.Clamp((int)Math.Floor(usableWidth / 260), 1, 8);
            var cardWidth = ResponsiveLayout.GetRecentCardWidth(width);
            Assert.True(columns * (cardWidth + 10) + ResponsiveLayout.RecentCardRightInset <= width + 0.001);
            Assert.InRange(cardWidth, 0, 300);
        }
    }
}
