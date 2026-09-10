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
        Assert.Equal(98, m.TabMinWidth);
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
    public void TheNarrowestTabStillHoldsAnIconAndSomeTitle()
    {
        foreach (int height in new[] { 34, 24 })
        {
            BarMetrics m = BarMetrics.For(height, 1.0f);

            // The icon, the padding either side, and the gap between icon and text.
            int chrome = m.Padding * 2 + m.IconSize + m.SmallGap;

            // Whatever is left over is the title and the ellipsis after it. One full-width
            // character is about as wide as the font is tall, so this asks for four characters
            // plus a character's worth of ellipsis.
            Assert.True(m.TabMinWidth - chrome >= m.FontPixels * 5);
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
    [InlineData(34, 1.0f)]
    [InlineData(24, 1.0f)]
    [InlineData(24, 2.0f)]
    public void AGroupIsStillMarkedOnACompactBar(int height, float scale)
    {
        BarMetrics m = BarMetrics.For(height, scale);

        // Thick enough to see, thin enough to leave the icon and the title alone.
        Assert.True(m.GroupBandHeight >= 2);
        Assert.True(m.GroupBandHeight < m.IconSize);
        Assert.True(m.GroupDividerWidth >= 1);

        // The rule between two groups has to fit in the space the layout already leaves.
        Assert.True(m.GroupDividerWidth <= m.TabGap);
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

    [Theory]
    [InlineData(34, 1.0f)]
    [InlineData(24, 1.0f)]
    [InlineData(24, 2.0f)]
    public void TheActiveTabIsStillOutlinedOnACompactBar(int height, float scale)
    {
        BarMetrics m = BarMetrics.For(height, scale);

        // Thick enough to draw, thin enough to leave the fill it surrounds.
        Assert.True(m.ActiveOutlineWidth >= 1);
        Assert.True(m.ActiveOutlineWidth < m.CornerRadius);
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

        Assert.True(m.ActiveOutlineWidth >= 1);
    }
}
