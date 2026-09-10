using WindowsSimpleTaskTabBar.Core.Filtering;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class WindowExclusionTests
{
    // ---------------------------------------------------------------
    // The shell's own windows, which the user cannot bring back
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Button")]
    [InlineData("DV2ControlHost")]
    [InlineData("MsgrIMEWindowClass")]
    [InlineData("SysShadow")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("Xaml_WindowedPopupClass")]
    public void TheShellsOwnWindowsAreNeverShown(string className)
    {
        // The taskbar, the desktop and the windows Windows draws its own interface in. A bar
        // that listed them would list the desktop behind it and the taskbar under it.
        Assert.True(WindowExclusion.IsShellWindow(className));
    }

    [Theory]
    [InlineData("shell_traywnd")]
    [InlineData("PROGMAN")]
    public void AShellClassIsRecognisedWhateverItsCase(string className)
    {
        Assert.True(WindowExclusion.IsShellWindow(className));
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("ApplicationFrameWindow")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOrdinaryWindowIsNotAShellWindow(string className)
    {
        Assert.False(WindowExclusion.IsShellWindow(className));
    }

    [Fact]
    public void TheUsersListCannotBringBackAShellWindow()
    {
        // The point of keeping the two lists apart: what the user edits is the list of
        // applications, and nothing there is consulted about a shell window at all.
        Assert.True(WindowExclusion.IsShellWindow("Shell_TrayWnd"));
        Assert.False(WindowExclusion.IsExcludedApplication("explorer.exe", new List<string>()));
    }

    // ---------------------------------------------------------------
    // The applications the user excludes
    // ---------------------------------------------------------------

    [Fact]
    public void AnApplicationOnTheListIsExcluded()
    {
        Assert.True(WindowExclusion.IsExcludedApplication(
            "notepad.exe", new List<string> { "notepad.exe" }));
    }

    [Fact]
    public void AnApplicationThatIsNotOnTheListIsKept()
    {
        Assert.False(WindowExclusion.IsExcludedApplication(
            "chrome.exe", new List<string> { "notepad.exe" }));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Program Files\Notepad\NOTEPAD.EXE")]
    [InlineData("notepad.exe")]
    public void ThePathAWindowGivesIsMatchedByItsFileName(string executable)
    {
        // The bar reads a full path from the process and the settings hold a bare file name.
        // Both go through the same normalization, so the two compare equal.
        Assert.True(WindowExclusion.IsExcludedApplication(
            executable, new List<string> { "notepad.exe" }));
    }

    [Fact]
    public void APathTypedIntoTheSettingsFileIsMatchedToo()
    {
        // A settings file can be edited by hand, and a path is the obvious thing to paste in.
        Assert.True(WindowExclusion.IsExcludedApplication(
            "notepad.exe", new List<string> { @"C:\Windows\System32\notepad.exe" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AWindowWhoseProcessCouldNotBeReadIsKept(string executable)
    {
        // An empty answer means the query failed, not that the window belongs to nothing. A bar
        // that dropped every window it could not identify would lose windows nobody named.
        Assert.False(WindowExclusion.IsExcludedApplication(
            executable, new List<string> { "notepad.exe", string.Empty }));
    }

    [Fact]
    public void AnAbsentListExcludesNothing()
    {
        // The settings property is unset in a file written before this setting existed.
        Assert.False(WindowExclusion.IsExcludedApplication("notepad.exe", null));
    }

    [Fact]
    public void AnEmptyListExcludesNothing()
    {
        Assert.False(WindowExclusion.IsExcludedApplication("notepad.exe", new List<string>()));
    }

    [Fact]
    public void OneOfSeveralNamesIsEnough()
    {
        var excluded = new List<string> { "teams.exe", "slack.exe", "notepad.exe" };

        Assert.True(WindowExclusion.IsExcludedApplication("slack.exe", excluded));
    }

    [Fact]
    public void TwoProgramsSharingAFileNameAreExcludedTogether()
    {
        // The known cost of matching on the file name rather than the path, recorded here so
        // that it is a decision rather than a surprise. See docs/architecture.md.
        Assert.True(WindowExclusion.IsExcludedApplication(
            @"D:\Elsewhere\notepad.exe", new List<string> { @"C:\Windows\notepad.exe" }));
    }
}
