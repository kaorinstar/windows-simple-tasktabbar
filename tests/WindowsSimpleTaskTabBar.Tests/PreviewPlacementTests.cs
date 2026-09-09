using WindowsSimpleTaskTabBar.Core.Preview;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class PreviewPlacementTests
{
    // ---------------------------------------------------------------
    // How big
    // ---------------------------------------------------------------

    [Fact]
    public void AWindowThatAlreadyFitsIsDrawnAtItsOwnSize()
    {
        // Never enlarged: a small window scaled up is a blurred picture of something the user
        // could have seen sharply.
        PreviewPlacement.Fit(200, 150, 280, 280, out int width, out int height);

        Assert.Equal(200, width);
        Assert.Equal(150, height);
    }

    [Fact]
    public void AWideWindowGivesUpItsWidthFirst()
    {
        PreviewPlacement.Fit(1920, 1080, 280, 280, out int width, out int height);

        Assert.Equal(280, width);
        Assert.Equal(157, height);   // 1080 * 280 / 1920
    }

    [Fact]
    public void ATallWindowGivesUpItsHeightFirst()
    {
        PreviewPlacement.Fit(600, 1200, 280, 280, out int width, out int height);

        Assert.Equal(140, width);    // 600 * 280 / 1200
        Assert.Equal(280, height);
    }

    [Fact]
    public void ThePictureFitsInsideTheRoomItIsGiven()
    {
        // The room above the bar is the height, and it is usually the shorter of the two.
        PreviewPlacement.Fit(1920, 1080, 280, 100, out int width, out int height);

        Assert.True(width <= 280);
        Assert.True(height <= 100);
        Assert.Equal(100, height);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1024, 768)]
    [InlineData(500, 1400)]
    [InlineData(3000, 200)]
    public void TheShapeOfTheWindowSurvivesTheScaling(int sourceWidth, int sourceHeight)
    {
        PreviewPlacement.Fit(sourceWidth, sourceHeight, 280, 280, out int width, out int height);

        // Both sides scaled by the same amount, to within the pixel that whole numbers cost.
        // Comparing the ratios instead would call a one pixel error on a short side a large
        // one: 18 pixels where 18.7 was asked for is 4 per cent of the ratio and a pixel of
        // the picture.
        double scale = System.Math.Min(280.0 / sourceWidth, 280.0 / sourceHeight);

        Assert.InRange(width, (int)(sourceWidth * scale) - 1, (int)(sourceWidth * scale) + 1);
        Assert.InRange(height, (int)(sourceHeight * scale) - 1, (int)(sourceHeight * scale) + 1);
    }

    [Fact]
    public void AWindowFarWiderThanItIsTallStillHasAHeight()
    {
        // The short side rounds towards nothing; a preview zero pixels tall is not a preview.
        PreviewPlacement.Fit(4000, 3, 280, 280, out int width, out int height);

        Assert.True(width >= 1);
        Assert.True(height >= 1);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    public void ASizeThatCouldNotBeReadShowsNothing(int sourceWidth, int sourceHeight)
    {
        PreviewPlacement.Fit(sourceWidth, sourceHeight, 280, 280, out int width, out int height);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void NoRoomAboveTheBarShowsNothing()
    {
        // A bar on a screen with nothing above it. Better to show no preview than a sliver.
        PreviewPlacement.Fit(1920, 1080, 280, 0, out int width, out int height);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    // ---------------------------------------------------------------
    // Where
    // ---------------------------------------------------------------

    [Fact]
    public void ThePreviewIsCentredOnItsTab()
    {
        PreviewBox box = PreviewPlacement.Place(width: 200, height: 120, tabLeft: 500,
            tabWidth: 100, barTop: 1000, screenLeft: 0, screenRight: 1920, gap: 6);

        // The tab's middle is 550, so a 200 wide preview starts at 450.
        Assert.Equal(450, box.X);
        Assert.Equal(200, box.Width);
        Assert.Equal(120, box.Height);
    }

    [Fact]
    public void ThePreviewSitsAboveTheBarWithTheGapBetween()
    {
        PreviewBox box = PreviewPlacement.Place(width: 200, height: 120, tabLeft: 500,
            tabWidth: 100, barTop: 1000, screenLeft: 0, screenRight: 1920, gap: 6);

        Assert.Equal(1000 - 6 - 120, box.Y);
    }

    [Fact]
    public void APreviewOverTheFirstTabIsBroughtOntoTheScreen()
    {
        PreviewBox box = PreviewPlacement.Place(width: 200, height: 120, tabLeft: 4,
            tabWidth: 100, barTop: 1000, screenLeft: 0, screenRight: 1920, gap: 6);

        Assert.Equal(0, box.X);
    }

    [Fact]
    public void APreviewOverTheLastTabIsBroughtOntoTheScreen()
    {
        PreviewBox box = PreviewPlacement.Place(width: 200, height: 120, tabLeft: 1850,
            tabWidth: 60, barTop: 1000, screenLeft: 0, screenRight: 1920, gap: 6);

        Assert.Equal(1920 - 200, box.X);
        Assert.True(box.X + box.Width <= 1920);
    }

    [Fact]
    public void ASecondScreenIsMeasuredFromItsOwnLeftEdge()
    {
        // A screen to the right of the primary one starts at 1920, not at 0.
        PreviewBox box = PreviewPlacement.Place(width: 200, height: 120, tabLeft: 1930,
            tabWidth: 60, barTop: 1000, screenLeft: 1920, screenRight: 3840, gap: 6);

        Assert.Equal(1920, box.X);
    }

    [Fact]
    public void APreviewWiderThanTheScreenKeepsItsLeftEdge()
    {
        // Nothing sensible fits, so the side that carries the title bar is the one kept.
        PreviewBox box = PreviewPlacement.Place(width: 900, height: 120, tabLeft: 100,
            tabWidth: 100, barTop: 600, screenLeft: 0, screenRight: 800, gap: 6);

        Assert.Equal(0, box.X);
    }
}
