using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Core.Theme;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class BarPaletteTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LightIsLightWhateverWindowsIsSetTo(bool windowsIsLight)
    {
        Assert.True(BarPalette.For(ColourMode.Light, windowsIsLight).IsLight);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DarkIsDarkWhateverWindowsIsSetTo(bool windowsIsLight)
    {
        Assert.False(BarPalette.For(ColourMode.Dark, windowsIsLight).IsLight);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FollowWindowsTakesTheWindowsSetting(bool windowsIsLight)
    {
        BarPalette palette = BarPalette.For(ColourMode.FollowWindows, windowsIsLight);

        Assert.Equal(windowsIsLight, palette.IsLight);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnknownModeFollowsWindows(bool windowsIsLight)
    {
        // A settings file can be edited by hand, so an unknown number has to be survivable.
        Assert.Equal(windowsIsLight, BarPalette.For((ColourMode)99, windowsIsLight).IsLight);
    }

    [Fact]
    public void TheLightPaletteKeepsTheColoursTheBarWasDrawnWith()
    {
        // The values the bar used before the palette became a setting.
        BarPalette p = BarPalette.For(ColourMode.Light, false);

        Assert.Equal(0xF2F3F5, p.Background);
        Assert.Equal(0xE2E4E8, p.Tab);
        Assert.Equal(0xEBEDF0, p.TabHover);
        Assert.Equal(0xFFFFFF, p.TabActive);
        Assert.Equal(0x464A50, p.Text);
        Assert.Equal(0x181A1E, p.TextActive);
        Assert.Equal(0xD2D5DA, p.Line);
    }

    [Fact]
    public void TheDarkPaletteKeepsTheColoursTheBarWasDrawnWith()
    {
        BarPalette p = BarPalette.For(ColourMode.Dark, true);

        Assert.Equal(0x202124, p.Background);
        Assert.Equal(0x303236, p.Tab);
        Assert.Equal(0x3A3D42, p.TabHover);
        Assert.Equal(0x4E5258, p.TabActive);
        Assert.Equal(0xB2B6BC, p.Text);
        Assert.Equal(0xF5F6F8, p.TextActive);
        Assert.Equal(0x18191C, p.Line);
    }

    [Fact]
    public void BothPalettesSupplyEveryAccent()
    {
        // The bar indexes this list with a group's accent number, which AppSettings has already
        // brought into 0 to AccentCount - 1. A short list would throw on a group at the end.
        Assert.Equal(AppSettings.AccentCount, BarPalette.For(ColourMode.Light, true).Accents.Count);
        Assert.Equal(AppSettings.AccentCount, BarPalette.For(ColourMode.Dark, true).Accents.Count);
    }

    [Fact]
    public void TheDarkPaletteIsNotTheLightOne()
    {
        BarPalette light = BarPalette.For(ColourMode.Light, false);
        BarPalette dark = BarPalette.For(ColourMode.Dark, true);

        Assert.NotEqual(light.Background, dark.Background);
        Assert.NotEqual(light.Tab, dark.Tab);
        Assert.NotEqual(light.Text, dark.Text);

        // The accents are shaded for the tabs they sit on, so they move with the palette too.
        for (int i = 0; i < AppSettings.AccentCount; i++)
            Assert.NotEqual(light.Accents[i], dark.Accents[i]);
    }

    [Fact]
    public void EveryColourIsAPlainRgbNumber()
    {
        // The UI adds the alpha, so a value carrying its own would be drawn wrong.
        foreach (ColourMode mode in new[] { ColourMode.Light, ColourMode.Dark })
        {
            BarPalette p = BarPalette.For(mode, true);

            foreach (int colour in new[]
                     {
                         p.Background, p.Tab, p.TabHover, p.TabActive,
                         p.Text, p.TextActive, p.Line,
                     })
            {
                Assert.InRange(colour, 0, 0xFFFFFF);
            }

            foreach (int accent in p.Accents) Assert.InRange(accent, 0, 0xFFFFFF);
        }
    }
}
