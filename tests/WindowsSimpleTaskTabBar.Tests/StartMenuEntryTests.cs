using WindowsSimpleTaskTabBar.Core.StartMenu;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class StartMenuEntryTests
{
    private static readonly string Programs = Path.Combine("start", "Programs");

    [Fact]
    public void TheShortcutSitsDirectlyInTheProgramsFolder()
    {
        // Directly in the folder, so the Start menu lists it among the apps and finds it by name
        // without anyone opening a folder first.
        Assert.Equal(Path.Combine(Programs, "App.lnk"), StartMenuEntry.ShortcutPath(Programs, "App"));
    }

    [Fact]
    public void TheOldInstallersFolderIsStillRecognised()
    {
        Assert.Equal(Path.Combine(Programs, "App", "App.lnk"),
            StartMenuEntry.LegacyShortcutPath(Programs, "App"));
    }

    [Fact]
    public void BothPlacesCountAsAlreadyBeingInTheStartMenu()
    {
        var locations = StartMenuEntry.Locations(Programs, "App");

        Assert.Equal(2, locations.Count);
        Assert.Equal(StartMenuEntry.ShortcutPath(Programs, "App"), locations[0]);
        Assert.Equal(StartMenuEntry.LegacyShortcutPath(Programs, "App"), locations[1]);
    }
}
