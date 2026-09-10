using WindowsSimpleTaskTabBar.Core.Localization;
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
    public void DefaultsToFollowingTheWindowsColours()
    {
        // What the bar did before the colour became a setting, so an update changes nothing.
        Assert.Equal(ColourMode.FollowWindows, new AppSettings().Colours);
    }

    [Fact]
    public void AnUnknownColourModeFallsBackToFollowingWindows()
    {
        var settings = new AppSettings { Colours = (ColourMode)99 };

        Assert.Equal(ColourMode.FollowWindows, settings.Normalized().Colours);
    }

    [Fact]
    public void NormalizingKeepsAValidColourChoice()
    {
        var settings = new AppSettings { Colours = ColourMode.Dark };
        settings.Normalize();

        Assert.Equal(ColourMode.Dark, settings.Colours);
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
    // Language
    // ---------------------------------------------------------------

    [Fact]
    public void DefaultsToTakingTheLanguageFromWindows()
    {
        Assert.Equal(Languages.Automatic, new AppSettings().Language);
    }

    [Fact]
    public void NormalizingKeepsALanguageThatIsOffered()
    {
        var settings = new AppSettings { Language = "ja" };

        Assert.Equal("ja", settings.Normalized().Language);
    }

    [Fact]
    public void NormalizingWritesALanguageTheWayTheApplicationSpellsIt()
    {
        var settings = new AppSettings { Language = "JA" };

        Assert.Equal("ja", settings.Normalized().Language);
    }

    [Fact]
    public void ALanguageThatIsNotOfferedFallsBackToWindows()
    {
        // A settings file can be edited by hand or written by a version that had more languages.
        var settings = new AppSettings { Language = "kl" };

        Assert.Equal(Languages.Automatic, settings.Normalized().Language);
    }

    [Fact]
    public void ASettingsFileWithNoLanguageInItFallsBackToWindows()
    {
        // DataContractJsonSerializer does not run the constructor, so a file written before this
        // setting existed leaves the property null rather than empty.
        var settings = new AppSettings { Language = null };

        Assert.Equal(Languages.Automatic, settings.Normalized().Language);
    }

    // ---------------------------------------------------------------
    // Grouping
    // ---------------------------------------------------------------
    private static AppGroup Group(string name, params string[] executables)
    {
        return new AppGroup { Name = name, Executables = new List<string>(executables) };
    }

    [Fact]
    public void ThePreviewIsOffUntilItIsAskedFor()
    {
        // Off for a new installation and, because the serializer does not run the constructor,
        // for a settings file written before the setting existed. Both read as false, which is
        // why this one needs no nullable property to tell "not chosen" from "turned off".
        var settings = new AppSettings();

        Assert.False(settings.ShowWindowPreview);
        Assert.False(settings.Normalized().ShowWindowPreview);
    }

    [Fact]
    public void NormalizingKeepsThePreviewSetting()
    {
        var settings = new AppSettings { ShowWindowPreview = true };

        Assert.True(settings.Normalized().ShowWindowPreview);

        settings.Normalize();
        Assert.True(settings.ShowWindowPreview);
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
    public void AGroupWithNoApplicationsYetIsKept()
    {
        // This is what the user is looking at in the moment after creating one. A group that
        // disappeared between being made and being filled would be hard to make sense of.
        var settings = new AppSettings
        {
            Groups = new List<AppGroup> { Group("Work", "code.exe"), Group("Empty") },
        };

        List<AppGroup> groups = settings.Normalized().Groups;

        Assert.Equal(2, groups.Count);
        Assert.Empty(groups[1].Executables);
    }

    [Fact]
    public void AGroupWithNoNameIsNamedAfterItsFirstApplication()
    {
        var settings = new AppSettings { Groups = new List<AppGroup> { Group("  ", "chrome.exe") } };

        Assert.Equal("chrome", Assert.Single(settings.Normalized().Groups).Name);
    }

    [Fact]
    public void AGroupWithNeitherANameNorAnApplicationStillGetsAName()
    {
        var settings = new AppSettings { Groups = new List<AppGroup> { Group("") } };

        Assert.Equal("Group", Assert.Single(settings.Normalized().Groups).Name);
    }

    [Fact]
    public void NormalizingInPlaceChangesTheSameObject()
    {
        // The bar, the settings dialog and the store all share one settings object. Normalizing
        // only on the way out would leave the bar drawing something the file does not hold.
        var settings = new AppSettings
        {
            Schema = 0,
            BarHeight = (BarHeightMode)99,
            Groups = new List<AppGroup> { Group("Work", @"C:\Tools\CODE.EXE") },
        };

        settings.Normalize();

        Assert.Equal(AppSettings.CurrentSchema, settings.Schema);
        Assert.Equal(BarHeightMode.Standard, settings.BarHeight);
        Assert.Equal(new[] { "code.exe" }, Assert.Single(settings.Groups).Executables);
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

    // ---------------------------------------------------------------
    // The update check
    // ---------------------------------------------------------------

    [Fact]
    public void TheUpdateCheckIsOnUntilItIsTurnedOff()
    {
        Assert.True(new AppSettings().Normalized().CheckForUpdates);
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheUpdateCheckExistedComesBackWithItOn()
    {
        // DataContractJsonSerializer does not run the constructor, so a file that predates this
        // setting leaves the property unset. A plain bool would arrive as false and turn the
        // check off for exactly the people who already have the application.
        var settings = new AppSettings { Schema = 3, CheckForUpdates = null };

        Assert.True(settings.Normalized().CheckForUpdates);
    }

    [Fact]
    public void TurningTheUpdateCheckOffSurvivesNormalizing()
    {
        var settings = new AppSettings { CheckForUpdates = false };
        settings.Normalize();

        Assert.False(settings.CheckForUpdates);
    }

    [Fact]
    public void AnUnreadableLastCheckTimeIsForgotten()
    {
        var settings = new AppSettings { LastUpdateCheckUtc = "yesterday" };

        Assert.Equal(string.Empty, settings.Normalized().LastUpdateCheckUtc);
    }

    [Fact]
    public void AReadableLastCheckTimeIsKept()
    {
        var settings = new AppSettings { LastUpdateCheckUtc = "2026-09-07T12:00:00Z" };

        Assert.Equal("2026-09-07T12:00:00Z", settings.Normalized().LastUpdateCheckUtc);
    }

    [Theory]
    [InlineData("v0.4.0", "v0.4.0")]
    [InlineData("  v0.4.0  ", "v0.4.0")]
    [InlineData("whatever", "")]
    [InlineData(null, "")]
    public void TheAnnouncedReleaseIsKeptOnlyWhenItIsAVersion(string stored, string expected)
    {
        // This is also what stops a hand-edited file from putting arbitrary text on screen: the
        // value reaches a notification.
        var settings = new AppSettings { LastNoticedRelease = stored };

        Assert.Equal(expected, settings.Normalized().LastNoticedRelease);
    }

    // ---------------------------------------------------------------
    // The applications the user excludes
    // ---------------------------------------------------------------

    [Fact]
    public void NothingIsExcludedByDefault()
    {
        // An update must not take anybody's windows off their bar.
        Assert.Empty(new AppSettings().ExcludedApplications);
    }

    [Fact]
    public void AnExclusionIsKeptAsABareFileNameInLowerCase()
    {
        var settings = new AppSettings
        {
            ExcludedApplications = new List<string> { @"C:\Windows\System32\NOTEPAD.EXE" },
        };

        // The same form the bar reads from a window, so a path pasted into the file by hand
        // still matches the application it names.
        Assert.Equal(new[] { "notepad.exe" }, settings.Normalized().ExcludedApplications);
    }

    [Fact]
    public void AnExclusionListedTwiceIsKeptOnce()
    {
        var settings = new AppSettings
        {
            ExcludedApplications = new List<string>
            {
                "notepad.exe", @"C:\Windows\notepad.exe", "Notepad.exe",
            },
        };

        Assert.Equal(new[] { "notepad.exe" }, settings.Normalized().ExcludedApplications);
    }

    [Fact]
    public void AnEmptyExclusionIsDropped()
    {
        var settings = new AppSettings
        {
            ExcludedApplications = new List<string> { string.Empty, "   ", null, "teams.exe" },
        };

        Assert.Equal(new[] { "teams.exe" }, settings.Normalized().ExcludedApplications);
    }

    [Fact]
    public void TheExclusionsComeBackSorted()
    {
        var settings = new AppSettings
        {
            ExcludedApplications = new List<string> { "teams.exe", "chrome.exe", "notepad.exe" },
        };

        Assert.Equal(new[] { "chrome.exe", "notepad.exe", "teams.exe" },
            settings.Normalized().ExcludedApplications);
    }

    [Fact]
    public void AnAbsentExclusionListReadsAsAnEmptyOne()
    {
        // DataContractJsonSerializer does not run the constructor, so a settings file written
        // before this setting existed leaves the property null rather than empty.
        var settings = new AppSettings { ExcludedApplications = null };
        settings.Normalize();

        Assert.Empty(settings.ExcludedApplications);
    }

    [Fact]
    public void NormalizingInPlaceKeepsTheExclusions()
    {
        // Normalize and Normalized have to stay in step: the bar reads the same object the
        // store writes, so a value kept by one and dropped by the other would show up as a
        // setting that came back after a restart.
        var settings = new AppSettings
        {
            ExcludedApplications = new List<string> { "Teams.exe" },
        };
        settings.Normalize();

        Assert.Equal(new[] { "teams.exe" }, settings.ExcludedApplications);
    }
}
