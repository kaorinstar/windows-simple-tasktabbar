using System.Collections.Generic;
using WindowsSimpleTaskTabBar.Core.Monitors;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

/// <summary>
/// Which monitor a window belongs to, and so which bar its tab appears on.
/// </summary>
public class MonitorMapTests
{
    // Two monitors side by side, the primary first: 1920x1080 at the origin, and a second one
    // of the same size to the right of it. This is the arrangement most people have.
    private static readonly List<MonitorBox> TwoSideBySide = new()
    {
        new MonitorBox(0, 0, 1920, 1080),
        new MonitorBox(1920, 0, 3840, 1080),
    };

    [Fact]
    public void AWindowOnOneMonitorBelongsToIt()
    {
        Assert.Equal(0, MonitorMap.Owner(100, 100, 900, 700, TwoSideBySide));
        Assert.Equal(1, MonitorMap.Owner(2020, 100, 2820, 700, TwoSideBySide));
    }

    [Fact]
    public void AWindowAcrossTwoMonitorsBelongsToTheOneItCoversMostOf()
    {
        // 300 pixels on the primary, 700 on the second.
        Assert.Equal(1, MonitorMap.Owner(1620, 100, 2620, 700, TwoSideBySide));

        // The same window moved the other way.
        Assert.Equal(0, MonitorMap.Owner(1220, 100, 2220, 700, TwoSideBySide));
    }

    [Fact]
    public void AWindowSplitEvenlyBelongsToTheEarlierMonitor()
    {
        // The caller lists the primary monitor first, so a window that gives neither monitor
        // more than the other stays on the primary rather than moving between bars as the
        // numbers drift.
        Assert.Equal(0, MonitorMap.Owner(1420, 100, 2420, 700, TwoSideBySide));
    }

    [Fact]
    public void AWindowOnNoMonitorGoesToTheNearestOne()
    {
        // Above and to the right of both, but much closer to the second.
        Assert.Equal(1, MonitorMap.Owner(2800, -900, 3200, -600, TwoSideBySide));

        // The same, on the other side.
        Assert.Equal(0, MonitorMap.Owner(-500, -900, -100, -600, TwoSideBySide));
    }

    [Fact]
    public void AMinimizedWindowsParkedPositionWouldLandOnTheTopLeftMonitor()
    {
        // Windows parks a minimized window at roughly (-32000, -32000), which is on no monitor.
        // This is the answer that makes reading rcNormalPosition necessary: it is nearest to
        // whichever monitor reaches furthest towards the top left, whatever the window was on.
        Assert.Equal(0, MonitorMap.Owner(-32000, -32000, -31200, -31400, TwoSideBySide));
    }

    [Fact]
    public void AWindowStandingOnTheSeamBelongsToTheMonitorItIsActuallyOn()
    {
        // Its left edge is exactly where the primary ends, so it covers none of it: a shared
        // edge is not an overlap, and the whole window is on the second monitor.
        Assert.Equal(1, MonitorMap.Owner(1920, 100, 2400, 700, TwoSideBySide));
    }

    [Fact]
    public void MonitorsAtDifferentScaleFactorsNeedNoScaling()
    {
        // A 150% monitor reports its size in the same screen pixels a window rectangle is
        // measured in: 2560x1440 physical pixels to the right of a 1920-wide primary.
        var mixed = new List<MonitorBox>
        {
            new MonitorBox(0, 0, 1920, 1080),
            new MonitorBox(1920, 0, 4480, 1440),
        };

        Assert.Equal(1, MonitorMap.Owner(3000, 200, 3800, 900, mixed));
    }

    [Fact]
    public void AMonitorAboveTheOtherIsToldApartByItsTopAndBottom()
    {
        // Stacked rather than side by side: the horizontal numbers are the same on both, so
        // only the vertical ones can answer.
        var stacked = new List<MonitorBox>
        {
            new MonitorBox(0, 0, 1920, 1080),
            new MonitorBox(0, -1080, 1920, 0),
        };

        Assert.Equal(1, MonitorMap.Owner(100, -900, 900, -300, stacked));
        Assert.Equal(0, MonitorMap.Owner(100, 300, 900, 900, stacked));
    }

    [Fact]
    public void OneMonitorTakesEveryWindow()
    {
        var single = new List<MonitorBox> { new MonitorBox(0, 0, 1920, 1080) };

        Assert.Equal(0, MonitorMap.Owner(100, 100, 900, 700, single));
        Assert.Equal(0, MonitorMap.Owner(-32000, -32000, -31200, -31400, single));
    }

    [Fact]
    public void NoMonitorsAnswersWithNothing()
    {
        // Windows answers with no monitors while the session is locked or every display is
        // asleep. The caller draws no bar rather than one on a monitor that is not there.
        Assert.Equal(-1, MonitorMap.Owner(100, 100, 900, 700, new List<MonitorBox>()));
        Assert.Equal(-1, MonitorMap.Owner(100, 100, 900, 700, null));
    }

    [Fact]
    public void AWindowLargerThanEveryMonitorBelongsToTheOneItCoversMostOf()
    {
        // A window stretched across the whole desktop. Neither monitor is more covered than
        // the other in width, so the taller one wins on area.
        var mixed = new List<MonitorBox>
        {
            new MonitorBox(0, 0, 1920, 1080),
            new MonitorBox(1920, 0, 3840, 2160),
        };

        Assert.Equal(1, MonitorMap.Owner(0, 0, 3840, 2160, mixed));
    }
}
