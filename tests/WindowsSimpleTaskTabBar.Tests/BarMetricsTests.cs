using WindowsSimpleTaskTabBar.Core.Layout;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class BarMetricsTests
{
    [Fact]
    public void StandardHeightAtNormalDpiKeepsTheOriginalSizes()
    {
        // These were the constants the bar was drawn with before the height became a setting.
        BarMetrics m = BarMetrics.For(34, 1.0f);

        Assert.Equal(34, m.BarHeight);
        Assert.Equal(3, m.TopOffset);
        Assert.Equal(16, m.IconSize);
        Assert.Equal(12, m.FontPixels);
        Assert.Equal(8, m.Padding);
        Assert.Equal(6, m.CornerRadius);
        Assert.Equal(6, m.SmallGap);
        Assert.Equal(16, m.CloseButtonSize);
        Assert.Equal(90, m.CloseButtonMinTabWidth);
        Assert.Equal(46, m.TabMinWidth);
        Assert.Equal(32, m.IconOnlyTabWidth);
        Assert.Equal(24, m.ScrollButtonWidth);
        Assert.Equal(220, m.TabMaxWidth);
        Assert.Equal(2, m.TabGap);
        Assert.Equal(4, m.OuterMargin);
    }

    [Fact]
    public void CompactHeightShrinksTheContentWithIt()
    {
        BarMetrics standard = BarMetrics.For(34, 1.0f);
        BarMetrics compact = BarMetrics.For(24, 1.0f);

        Assert.Equal(24, compact.BarHeight);
        Assert.True(compact.IconSize < standard.IconSize);
        Assert.True(compact.FontPixels < standard.FontPixels);
        Assert.True(compact.Padding < standard.Padding);
        Assert.True(compact.CloseButtonMinTabWidth < standard.CloseButtonMinTabWidth);
    }

    [Fact]
    public void AnIconOnlyTabIsNarrowerThanOneWithATitle()
    {
        foreach (int height in new[] { 34, 24 })
        {
            BarMetrics m = BarMetrics.For(height, 1.0f);

            Assert.True(m.IconOnlyTabWidth < m.TabMinWidth);
            Assert.True(m.IconOnlyTabWidth >= m.IconSize);   // the icon still has to fit
        }
    }

    [Fact]
    public void ContentStillFitsInsideACompactBar()
    {
        BarMetrics m = BarMetrics.For(24, 1.0f);

        Assert.True(m.TopOffset + m.IconSize <= m.BarHeight);
        Assert.True(m.TopOffset + m.FontPixels <= m.BarHeight);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void EverySizeScalesWithDpi(float scale)
    {
        BarMetrics baseline = BarMetrics.For(34, 1.0f);
        BarMetrics scaled = BarMetrics.For(34, scale);

        Assert.Equal((int)System.Math.Round(baseline.BarHeight * (double)scale), scaled.BarHeight);
        Assert.Equal((int)System.Math.Round(baseline.IconSize * (double)scale), scaled.IconSize);
        Assert.Equal((int)System.Math.Round(baseline.FontPixels * (double)scale), scaled.FontPixels);
    }

    [Fact]
    public void TextAndIconsStayLegibleOnAVeryShortBar()
    {
        // The floors matter most here: without them the arithmetic would ask for a 1 pixel font.
        BarMetrics m = BarMetrics.For(8, 1.0f);

        Assert.True(m.FontPixels >= 10);
        Assert.True(m.IconSize >= 12);
        Assert.True(m.TabGap >= 1);
    }

    [Fact]
    public void NonsenseInputIsBroughtBackIntoRange()
    {
        BarMetrics m = BarMetrics.For(0, 0f);

        Assert.True(m.BarHeight >= 1);
        Assert.True(m.IconSize >= 1);
        Assert.True(m.TabMaxWidth >= 1);
    }
}
