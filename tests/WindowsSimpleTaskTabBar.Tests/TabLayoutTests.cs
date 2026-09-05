using WindowsSimpleTaskTabBar.Core.Layout;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class TabLayoutTests
{
    [Fact]
    public void WidthIsZeroWhenThereAreNoTabs()
    {
        Assert.Equal(0, TabLayout.CalculateTabWidth(1000, 0, 2, 46, 220));
    }

    [Fact]
    public void WidthIsCappedAtTheMaximumWhenThereAreFewTabs()
    {
        // 1000px across 2 tabs would be 498px each, but the 220px cap applies.
        Assert.Equal(220, TabLayout.CalculateTabWidth(1000, 2, 2, 46, 220));
    }

    [Fact]
    public void WidthStopsAtTheMinimumWhenThereAreManyTabs()
    {
        // 1000px across 100 tabs would be 8px each, but the 46px floor applies.
        Assert.Equal(46, TabLayout.CalculateTabWidth(1000, 100, 2, 46, 220));
    }

    [Fact]
    public void WidthIsSharedEvenlyBetweenTheMinimumAndMaximum()
    {
        // 1000px across 10 tabs is 100px each, minus the 2px gap.
        Assert.Equal(98, TabLayout.CalculateTabWidth(1000, 10, 2, 46, 220));
    }

    [Fact]
    public void MaximumWinsWhenTheMinimumIsLarger()
    {
        Assert.Equal(50, TabLayout.CalculateTabWidth(1000, 100, 2, 200, 50));
    }

    [Theory]
    [InlineData(1000, 100, 2, 9)]
    [InlineData(1000, 46, 2, 20)]
    [InlineData(100, 220, 2, 0)]
    public void CountsHowManyTabsFit(int available, int tabWidth, int gap, int expected)
    {
        Assert.Equal(expected, TabLayout.CountVisibleTabs(available, tabWidth, gap));
    }
}
