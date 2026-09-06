using WindowsSimpleTaskTabBar.Core.Layout;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class TabStripTests
{
    // The bar's real numbers at 100% scaling: gap 2, floor 98, cap 220.
    private static TabStripLayout Measure(int available, int count)
    {
        return TabStrip.Measure(available, count, 2, 98, 220);
    }

    [Fact]
    public void NoTabsMeasuresToNothing()
    {
        TabStripLayout layout = Measure(1000, 0);

        Assert.Equal(0, layout.TabWidth);
        Assert.Equal(0, layout.ContentWidth);
        Assert.False(layout.CanScroll);
    }

    [Fact]
    public void FewTabsAreCappedAndDoNotScroll()
    {
        TabStripLayout layout = Measure(1000, 2);

        Assert.Equal(220, layout.TabWidth);
        Assert.False(layout.CanScroll);
    }

    [Fact]
    public void TabsShareTheWidthEvenlyWhileThereIsRoom()
    {
        TabStripLayout layout = Measure(1000, 8);

        Assert.Equal(123, layout.TabWidth);         // (1000 - 7 * 2) / 8
        Assert.False(layout.CanScroll);
    }

    [Fact]
    public void TabsShrinkAsFarAsTheFloorWithoutScrolling()
    {
        // 10 tabs in 1000px: (1000 - 9 * 2) / 10 = 98px each, exactly the floor.
        TabStripLayout layout = Measure(1000, 10);

        Assert.Equal(98, layout.TabWidth);
        Assert.False(layout.CanScroll);
    }

    [Fact]
    public void TheRowScrollsOnceTheFloorIsReached()
    {
        // 11 tabs in 1000px would be 89px each, below the floor. Tabs hold 98px instead and
        // the row scrolls, so every tab keeps its icon and the start of its title.
        TabStripLayout layout = Measure(1000, 11);

        Assert.Equal(98, layout.TabWidth);
        Assert.True(layout.CanScroll);
        Assert.Equal(11 * 98 + 10 * 2 - 1000, layout.MaxScroll);
    }

    [Fact]
    public void ManyTabsNeverShrinkPastTheFloor()
    {
        TabStripLayout layout = Measure(1000, 40);

        Assert.Equal(98, layout.TabWidth);
        Assert.True(layout.CanScroll);
        Assert.Equal(40 * 98 + 39 * 2 - 1000, layout.MaxScroll);
    }

    [Fact]
    public void NoTabIsEverDropped()
    {
        // The point of the whole exercise: whatever the count, the row still holds every tab.
        foreach (int count in new[] { 1, 5, 25, 40, 200 })
        {
            TabStripLayout layout = Measure(1000, count);
            int reachable = layout.ContentWidth - layout.MaxScroll;

            Assert.True(layout.TabWidth > 0);
            Assert.Equal(count * layout.TabWidth + (count - 1) * 2, layout.ContentWidth);
            Assert.True(reachable <= 1000);
        }
    }

    [Fact]
    public void TheCapWinsWhenTheFloorIsLarger()
    {
        // Nonsense input, but it must not produce a tab wider than the cap allows.
        TabStripLayout layout = TabStrip.Measure(1000, 100, 2, 200, 50);

        Assert.Equal(50, layout.TabWidth);
    }

    [Fact]
    public void OnlyTheGapsBetweenTabsAreReserved()
    {
        // Five tabs have four gaps, not five: the row must still fit what it measured.
        TabStripLayout layout = TabStrip.Measure(600, 5, 4, 1, 1000);

        Assert.Equal(116, layout.TabWidth);          // (600 - 4 * 4) / 5
        Assert.Equal(5 * 116 + 4 * 4, layout.ContentWidth);
        Assert.False(layout.CanScroll);
    }

    [Theory]
    [InlineData(-5, 100, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(150, 100, 100)]
    [InlineData(50, 0, 0)]
    public void ScrollIsHeldInRange(int scroll, int maxScroll, int expected)
    {
        Assert.Equal(expected, TabStrip.ClampScroll(scroll, maxScroll));
    }

    [Fact]
    public void ATabAlreadyInViewDoesNotMoveTheRow()
    {
        // Tabs are 100px with a 2px gap; the window shows 500px starting at 0.
        Assert.Equal(0, TabStrip.ScrollToShow(2, 100, 2, 500, 0, 1000));
    }

    [Fact]
    public void ATabOffTheRightEdgeScrollsJustFarEnough()
    {
        // Tab 6 spans 612..712. Showing its right edge in a 500px window needs 212.
        Assert.Equal(212, TabStrip.ScrollToShow(6, 100, 2, 500, 0, 1000));
    }

    [Fact]
    public void ATabOffTheLeftEdgeScrollsBackToIt()
    {
        // Tab 1 starts at 102, so that becomes the new left edge.
        Assert.Equal(102, TabStrip.ScrollToShow(1, 100, 2, 500, 400, 1000));
    }

    [Fact]
    public void ScrollingToATabStaysInRange()
    {
        Assert.Equal(50, TabStrip.ScrollToShow(9, 100, 2, 500, 0, 50));
    }
}
