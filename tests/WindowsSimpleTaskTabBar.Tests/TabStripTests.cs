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

    [Theory]
    // 100px tabs with a 2px gap: slot 0 starts at 0, slot 1 at 102, slot 2 at 204.
    [InlineData(0, 0)]        // where it started
    [InlineData(50, 0)]       // not yet halfway to the next slot
    [InlineData(51, 1)]       // just past halfway, so the neighbour steps aside
    [InlineData(102, 1)]      // exactly over slot 1
    [InlineData(204, 2)]
    [InlineData(-40, 0)]      // dragged off the left end
    [InlineData(9999, 4)]     // dragged off the right end, five tabs in the row
    public void ATabDropsIntoTheSlotItsLeadingEdgeHasPassed(int offset, int expected)
    {
        Assert.Equal(expected, TabStrip.DropIndex(offset, 100, 2, 5));
    }

    [Fact]
    public void ASingleTabHasNowhereToGo()
    {
        Assert.Equal(0, TabStrip.DropIndex(500, 100, 2, 1));
        Assert.Equal(0, TabStrip.DropIndex(500, 100, 2, 0));
    }

    [Fact]
    public void MovingATabKeepsTheOthersInOrder()
    {
        var items = new List<string> { "a", "b", "c", "d" };

        TabStrip.Move(items, 0, 2);
        Assert.Equal(new[] { "b", "c", "a", "d" }, items);

        TabStrip.Move(items, 3, 0);
        Assert.Equal(new[] { "d", "b", "c", "a" }, items);
    }

    [Fact]
    public void AMoveThatChangesNothingIsLeftAlone()
    {
        var items = new List<string> { "a", "b", "c" };

        TabStrip.Move(items, 1, 1);
        TabStrip.Move(items, -1, 0);
        TabStrip.Move(items, 0, 3);
        TabStrip.Move<string>(null, 0, 1);

        Assert.Equal(new[] { "a", "b", "c" }, items);
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

    // ---------------------------------------------------------------
    // Moving a range, which is how a whole tab group is dragged
    // ---------------------------------------------------------------
    private static List<string> Row() => new() { "a", "b", "c", "d", "e" };

    [Fact]
    public void MovingARangeToTheRightKeepsItsOrder()
    {
        List<string> row = Row();

        // Take a and b out, put them back starting at 2 of what is left: c, d, a, b, e.
        TabStrip.MoveRange(row, 0, 2, 2);

        Assert.Equal(new[] { "c", "d", "a", "b", "e" }, row);
    }

    [Fact]
    public void MovingARangeToTheLeftKeepsItsOrder()
    {
        List<string> row = Row();

        TabStrip.MoveRange(row, 3, 2, 0);

        Assert.Equal(new[] { "d", "e", "a", "b", "c" }, row);
    }

    [Fact]
    public void MovingARangeOfOneIsTheSameAsMovingOneItem()
    {
        List<string> byRange = Row();
        List<string> byMove = Row();

        TabStrip.MoveRange(byRange, 0, 1, 3);
        TabStrip.Move(byMove, 0, 3);

        Assert.Equal(byMove, byRange);
    }

    [Fact]
    public void MovingARangeWhereItAlreadyIsChangesNothing()
    {
        List<string> row = Row();

        TabStrip.MoveRange(row, 1, 2, 1);

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, row);
    }

    [Fact]
    public void ARangeOutsideTheRowIsIgnored()
    {
        List<string> row = Row();

        TabStrip.MoveRange(row, 4, 3, 0);    // runs off the end
        TabStrip.MoveRange(row, -1, 2, 0);   // starts before the beginning
        TabStrip.MoveRange(row, 0, 2, 4);    // lands past what is left
        TabStrip.MoveRange(row, 0, 0, 2);    // nothing to move
        TabStrip.MoveRange<string>(null, 0, 1, 0);

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, row);
    }

    // ---------------------------------------------------------------
    // Holding a group still until the pointer has travelled
    // ---------------------------------------------------------------
    [Fact]
    public void APointerThatHasNotMovedIsNotFarEnough()
    {
        // The whole point: nothing may shift again while the pointer sits still.
        Assert.False(TabStrip.MovedFarEnough(500, 500, 98, 1));
        Assert.False(TabStrip.MovedFarEnough(503, 500, 98, 1));
    }

    [Fact]
    public void ASingleSlotAsksForOneTabWidth()
    {
        // The ordinary swap with a neighbour. A slot is a tab and a gap, so this is always
        // reached by the time the next slot is: reordering is not held back.
        Assert.True(TabStrip.MovedFarEnough(598, 500, 98, 1));
        Assert.False(TabStrip.MovedFarEnough(597, 500, 98, 1));
    }

    [Fact]
    public void AWiderJumpAsksForMoreMovementBack()
    {
        // A jump over a group of two shifts the row by two, so undoing it asks for the two tabs
        // of travel that made it. One tab is no longer enough.
        Assert.False(TabStrip.MovedFarEnough(598, 500, 98, 2));
        Assert.True(TabStrip.MovedFarEnough(696, 500, 98, 2));
    }

    [Fact]
    public void TheDistanceCountsInEitherDirection()
    {
        Assert.True(TabStrip.MovedFarEnough(402, 500, 98, 1));
        Assert.False(TabStrip.MovedFarEnough(497, 500, 98, 1));
    }

    [Fact]
    public void NothingToMeasureAgainstStillAsksForSomeMovement()
    {
        // An empty row has no tab width, and a move of no slots is not a reason to let every
        // shift through: that would be the flicker back again.
        Assert.False(TabStrip.MovedFarEnough(500, 500, 0, 0));
        Assert.True(TabStrip.MovedFarEnough(501, 500, 0, 0));
    }
}
