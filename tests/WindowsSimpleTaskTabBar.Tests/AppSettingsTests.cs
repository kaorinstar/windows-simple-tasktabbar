using WindowsSimpleTaskTabBar.Core.Settings;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DefaultsToTheStandardBarHeight()
    {
        var settings = new AppSettings();

        Assert.Equal(BarHeightMode.Standard, settings.BarHeight);
        Assert.Equal(AppSettings.CurrentSchema, settings.Schema);
    }

    [Fact]
    public void StandardIsTallerThanCompact()
    {
        Assert.True(AppSettings.HeightInPixels(BarHeightMode.Standard)
                    > AppSettings.HeightInPixels(BarHeightMode.Compact));
    }

    [Fact]
    public void AValueOutsideTheKnownSetFallsBackToTheDefault()
    {
        // A settings file can be edited by hand, so an unknown number has to be survivable.
        var settings = new AppSettings { BarHeight = (BarHeightMode)99 };

        Assert.Equal(BarHeightMode.Standard, settings.Normalized().BarHeight);
    }

    [Fact]
    public void NormalizingKeepsAValidChoice()
    {
        var settings = new AppSettings { BarHeight = BarHeightMode.Compact };

        Assert.Equal(BarHeightMode.Compact, settings.Normalized().BarHeight);
    }

    [Fact]
    public void NormalizingStampsTheCurrentSchema()
    {
        var settings = new AppSettings { Schema = 0 };

        Assert.Equal(AppSettings.CurrentSchema, settings.Normalized().Schema);
    }
}
