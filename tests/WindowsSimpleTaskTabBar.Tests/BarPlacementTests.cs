using WindowsSimpleTaskTabBar.Core.Layout;
using WindowsSimpleTaskTabBar.Core.Settings;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

/// <summary>
/// Which edge the bar lands on, and the rectangle it occupies there.
/// </summary>
public class BarPlacementTests
{
    // ---------------------------------------------------------------
    // Which edge
    // ---------------------------------------------------------------

    [Fact]
    public void FollowingTheTaskbarPutsTheBarAtTheBottomWhenTheTaskbarIsThere()
    {
        Assert.Equal(ScreenEdge.Bottom,
            BarPlacement.Resolve(BarEdgeMode.FollowTaskbar, ScreenEdge.Bottom));
    }

    [Fact]
    public void FollowingTheTaskbarPutsTheBarAtTheTopWhenTheTaskbarIsThere()
    {
        Assert.Equal(ScreenEdge.Top,
            BarPlacement.Resolve(BarEdgeMode.FollowTaskbar, ScreenEdge.Top));
    }

    [Theory]
    [InlineData(ScreenEdge.Left)]
    [InlineData(ScreenEdge.Right)]
    public void ATaskbarOnASideEdgeLeavesTheBarAtTheBottom(ScreenEdge taskbar)
    {
        // The bar is a horizontal row, so there is no side edge for it to follow the taskbar
        // onto. The bottom is where it has always been.
        Assert.Equal(ScreenEdge.Bottom,
            BarPlacement.Resolve(BarEdgeMode.FollowTaskbar, taskbar));
    }

    [Theory]
    [InlineData(ScreenEdge.Left)]
    [InlineData(ScreenEdge.Top)]
    [InlineData(ScreenEdge.Right)]
    [InlineData(ScreenEdge.Bottom)]
    public void AChosenEdgeIgnoresWhereTheTaskbarIs(ScreenEdge taskbar)
    {
        Assert.Equal(ScreenEdge.Top, BarPlacement.Resolve(BarEdgeMode.Top, taskbar));
        Assert.Equal(ScreenEdge.Bottom, BarPlacement.Resolve(BarEdgeMode.Bottom, taskbar));
    }

    [Fact]
    public void ASettingThisVersionDoesNotKnowFollowsTheTaskbar()
    {
        // A hand-edited settings file reaches this arithmetic through the bar, and a number
        // outside the enumeration must not land the bar somewhere it cannot draw.
        Assert.Equal(ScreenEdge.Top, BarPlacement.Resolve((BarEdgeMode)99, ScreenEdge.Top));
        Assert.Equal(ScreenEdge.Bottom, BarPlacement.Resolve((BarEdgeMode)99, ScreenEdge.Bottom));
    }

    // ---------------------------------------------------------------
    // The rectangle asked for
    // ---------------------------------------------------------------

    [Fact]
    public void TheBarAsksForTheFullWidthOfTheScreenAtTheBottom()
    {
        BarBox box = BarPlacement.Requested(0, 0, 1920, 1080, ScreenEdge.Bottom, 34);

        Assert.Equal(0, box.Left);
        Assert.Equal(1920, box.Right);
        Assert.Equal(1080 - 34, box.Top);
        Assert.Equal(1080, box.Bottom);
        Assert.Equal(34, box.Height);
    }

    [Fact]
    public void TheBarAsksForTheFullWidthOfTheScreenAtTheTop()
    {
        BarBox box = BarPlacement.Requested(0, 0, 1920, 1080, ScreenEdge.Top, 34);

        Assert.Equal(0, box.Left);
        Assert.Equal(1920, box.Right);
        Assert.Equal(0, box.Top);
        Assert.Equal(34, box.Bottom);
        Assert.Equal(34, box.Height);
    }

    [Fact]
    public void ASecondScreenIsMeasuredFromItsOwnEdges()
    {
        // A screen to the right of the primary one starts at 1920, and one above it starts at a
        // negative coordinate.
        BarBox box = BarPlacement.Requested(1920, -1080, 3840, 0, ScreenEdge.Top, 24);

        Assert.Equal(1920, box.Left);
        Assert.Equal(3840, box.Right);
        Assert.Equal(-1080, box.Top);
        Assert.Equal(-1080 + 24, box.Bottom);
    }

    [Fact]
    public void AThicknessOfNothingStillLeavesABarToDrawOn()
    {
        Assert.Equal(1, BarPlacement.Requested(0, 0, 1920, 1080, ScreenEdge.Bottom, 0).Height);
        Assert.Equal(1, BarPlacement.Requested(0, 0, 1920, 1080, ScreenEdge.Top, -5).Height);
    }

    // ---------------------------------------------------------------
    // The rectangle granted
    // ---------------------------------------------------------------

    [Fact]
    public void ABarAtTheBottomKeepsItsThicknessAgainstTheBottomEdge()
    {
        // The system answered with the whole strip above the taskbar, which is deeper than the
        // bar needs. The bar stays against the taskbar, so the top edge is the one that moves.
        var granted = new BarBox(0, 600, 1920, 1032);

        BarBox box = BarPlacement.Settled(granted, ScreenEdge.Bottom, 34);

        Assert.Equal(1032 - 34, box.Top);
        Assert.Equal(1032, box.Bottom);
        Assert.Equal(0, box.Left);
        Assert.Equal(1920, box.Right);
    }

    [Fact]
    public void ABarAtTheTopKeepsItsThicknessAgainstTheTopEdge()
    {
        // The taskbar is at the top, so the free strip starts below it and the bar sits at the
        // start of that strip rather than at the far end of it.
        var granted = new BarBox(0, 48, 1920, 1080);

        BarBox box = BarPlacement.Settled(granted, ScreenEdge.Top, 34);

        Assert.Equal(48, box.Top);
        Assert.Equal(48 + 34, box.Bottom);
        Assert.Equal(0, box.Left);
        Assert.Equal(1920, box.Right);
    }

    [Fact]
    public void AGrantedStripKeepsTheWidthTheSystemGaveIt()
    {
        // Another AppBar down one side narrows the strip, and the bar takes the width it is left
        // with rather than the width it asked for.
        var granted = new BarBox(120, 900, 1800, 1080);

        BarBox box = BarPlacement.Settled(granted, ScreenEdge.Bottom, 34);

        Assert.Equal(120, box.Left);
        Assert.Equal(1800, box.Right);
        Assert.Equal(1680, box.Width);
    }
}
