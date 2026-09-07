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

    // ---------------------------------------------------------------
    // Grouping
    // ---------------------------------------------------------------
    private static AppGroup Group(string name, params string[] executables)
    {
        return new AppGroup { Name = name, Executables = new List<string>(executables) };
    }

    [Fact]
    public void GroupingIsOffUntilItIsAskedFor()
    {
        var settings = new AppSettings();

        Assert.False(settings.GroupByApplication);
        Assert.Empty(settings.Groups);
        Assert.False(settings.Normalized().GroupByApplication);
    }

    [Fact]
    public void NormalizingKeepsTheGroups()
    {
        var settings = new AppSettings
        {
            GroupByApplication = true,
            Groups = new List<AppGroup> { Group("Browsers", "chrome.exe", "msedge.exe") },
        };

        AppSettings result = settings.Normalized();

        Assert.True(result.GroupByApplication);
        AppGroup group = Assert.Single(result.Groups);
        Assert.Equal("Browsers", group.Name);
        Assert.Equal(new[] { "chrome.exe", "msedge.exe" }, group.Executables);
    }

    [Fact]
    public void NormalizingReplacesAMissingGroupListWithAnEmptyOne()
    {
        // DataContractJsonSerializer does not run the constructor, so a settings file written
        // before this setting existed leaves the property null rather than empty. Every path
        // into the application goes through Normalized, so nothing after it sees a null.
        var settings = new AppSettings { Groups = null };

        Assert.Empty(settings.Normalized().Groups);
    }

    [Fact]
    public void NormalizingTakesExecutableNamesApartTheSameWayTheBarDoes()
    {
        var settings = new AppSettings
        {
            Groups = new List<AppGroup>
            {
                Group("Browsers", @"  C:\Program Files\Chrome\CHROME.EXE  ", "", "chrome.exe"),
            },
        };

        AppGroup group = Assert.Single(settings.Normalized().Groups);

        // Trimmed, lower-cased, stripped of its folder, and the duplicate dropped.
        Assert.Equal(new[] { "chrome.exe" }, group.Executables);
    }

    [Fact]
    public void AnExecutableIsKeptInOneGroupOnly()
    {
        var settings = new AppSettings
        {
            Groups = new List<AppGroup>
            {
                Group("Work", "code.exe", "chrome.exe"),
                Group("Play", "chrome.exe", "steam.exe"),
            },
        };

        List<AppGroup> groups = settings.Normalized().Groups;

        Assert.Equal(new[] { "code.exe", "chrome.exe" }, groups[0].Executables);
        Assert.Equal(new[] { "steam.exe" }, groups[1].Executables);
    }

    [Fact]
    public void AGroupWithNoApplicationsLeftIsDropped()
    {
        var settings = new AppSettings
        {
            Groups = new List<AppGroup> { Group("Work", "code.exe"), Group("Empty") },
        };

        Assert.Single(settings.Normalized().Groups);
    }

    [Fact]
    public void AGroupWithNoNameIsNamedAfterItsFirstApplication()
    {
        var settings = new AppSettings { Groups = new List<AppGroup> { Group("  ", "chrome.exe") } };

        Assert.Equal("chrome", Assert.Single(settings.Normalized().Groups).Name);
    }

    [Fact]
    public void TwoGroupsCannotShareAName()
    {
        // The name is what identifies a group, so sharing one would draw two sets of
        // applications as though they were a single group.
        var settings = new AppSettings
        {
            Groups = new List<AppGroup> { Group("Work", "code.exe"), Group("Work", "slack.exe") },
        };

        List<AppGroup> groups = settings.Normalized().Groups;

        Assert.Equal("Work", groups[0].Name);
        Assert.Equal("Work (2)", groups[1].Name);
    }

    [Fact]
    public void AnAccentOutsideThePaletteFallsBackToAutomatic()
    {
        var settings = new AppSettings
        {
            Groups = new List<AppGroup>
            {
                new() { Name = "Work", Accent = 99, Executables = new List<string> { "code.exe" } },
                new() { Name = "Play", Accent = 3, Executables = new List<string> { "steam.exe" } },
            },
        };

        List<AppGroup> groups = settings.Normalized().Groups;

        Assert.Equal(-1, groups[0].Accent);
        Assert.Equal(3, groups[1].Accent);
    }

    [Fact]
    public void ANullEntryInTheGroupListIsSurvivable()
    {
        var settings = new AppSettings
        {
            Groups = new List<AppGroup> { null, Group("Work", "code.exe") },
        };

        Assert.Single(settings.Normalized().Groups);
    }
}
