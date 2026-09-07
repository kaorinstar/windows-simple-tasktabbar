using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Layout;
using WindowsSimpleTaskTabBar.Core.Settings;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class TabGroupingTests
{
    // The bar's own palette size, so the numbers here are the ones a user would see.
    private const int Palette = AppSettings.AccentCount;

    private static AppGroup Group(string name, params string[] executables)
    {
        return new AppGroup { Name = name, Executables = new List<string>(executables) };
    }

    /// <summary>The group ids of a row, in the order Arrange put them.</summary>
    private static List<string> Arranged(params string[] groupIds)
    {
        List<int> order = TabGrouping.Arrange(groupIds);
        return order.ConvertAll(i => groupIds[i]);
    }

    // ---------------------------------------------------------------
    // Reading an executable
    // ---------------------------------------------------------------
    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome.exe")]
    [InlineData(@"C:\Windows\System32\NOTEPAD.EXE", "notepad.exe")]
    [InlineData("chrome.exe", "chrome.exe")]
    [InlineData("/usr/bin/thing", "thing")]         // a forward slash, for the unit tests
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheKeyIsTheFileNameInLowerCase(string path, string expected)
    {
        Assert.Equal(expected, TabGrouping.KeyFor(path));
    }

    [Fact]
    public void TwoCopiesOfOneApplicationShareAKey()
    {
        // The same program installed twice is still one application to the person looking.
        Assert.Equal(TabGrouping.KeyFor(@"C:\Program Files\App\app.exe"),
                     TabGrouping.KeyFor(@"D:\Portable\App\app.exe"));
    }

    // ---------------------------------------------------------------
    // Which group an application belongs to
    // ---------------------------------------------------------------
    [Fact]
    public void WithoutARuleAnApplicationIsItsOwnGroup()
    {
        Assert.Equal("chrome.exe", TabGrouping.GroupIdFor("chrome.exe", new List<AppGroup>()));
        Assert.Equal("chrome.exe", TabGrouping.GroupIdFor("chrome.exe", null));
    }

    [Fact]
    public void ARuleCombinesSeveralApplicationsIntoOneGroup()
    {
        var rules = new List<AppGroup> { Group("Browsers", "chrome.exe", "msedge.exe") };

        Assert.Equal("Browsers", TabGrouping.GroupIdFor("chrome.exe", rules));
        Assert.Equal("Browsers", TabGrouping.GroupIdFor("msedge.exe", rules));
        Assert.Equal("notepad.exe", TabGrouping.GroupIdFor("notepad.exe", rules));
    }

    [Fact]
    public void AnExecutableNamedByTwoRulesBelongsToTheFirst()
    {
        // Normalizing the settings already removes the second claim. This is the second guard,
        // so that a list assembled anywhere else still gives one answer rather than two.
        var rules = new List<AppGroup>
        {
            Group("Work", "code.exe"),
            Group("Everything else", "code.exe"),
        };

        Assert.Equal("Work", TabGrouping.GroupIdFor("code.exe", rules));
    }

    [Fact]
    public void RuleMembershipIgnoresCase()
    {
        var rules = new List<AppGroup> { Group("Browsers", "CHROME.EXE") };

        Assert.Equal("Browsers", TabGrouping.GroupIdFor("chrome.exe", rules));
    }

    [Fact]
    public void AnApplicationThatCouldNotBeReadHasNoGroup()
    {
        var rules = new List<AppGroup> { Group("Browsers", "chrome.exe") };

        Assert.Equal("", TabGrouping.GroupIdFor("", rules));
    }

    // ---------------------------------------------------------------
    // The order of the row
    // ---------------------------------------------------------------
    [Fact]
    public void AnEmptyRowArrangesToNothing()
    {
        Assert.Empty(TabGrouping.Arrange(new List<string>()));
        Assert.Empty(TabGrouping.Arrange(null));
    }

    [Fact]
    public void WindowsOfOneApplicationAreBroughtTogether()
    {
        Assert.Equal(new[] { "a.exe", "a.exe", "b.exe", "b.exe" },
            Arranged("a.exe", "b.exe", "a.exe", "b.exe"));
    }

    [Fact]
    public void AGroupTakesThePlaceOfItsFirstWindow()
    {
        // b was opened first, so its group leads the row even though a has more windows.
        Assert.Equal(new[] { "b.exe", "a.exe", "a.exe" },
            Arranged("b.exe", "a.exe", "a.exe"));
    }

    [Fact]
    public void TheOrderInsideAGroupIsLeftAlone()
    {
        List<int> order = TabGrouping.Arrange(new[] { "a.exe", "b.exe", "a.exe" });

        // The two windows of a keep the order they arrived in: index 0 before index 2.
        Assert.Equal(new[] { 0, 2, 1 }, order);
    }

    [Fact]
    public void ArrangingAnArrangedRowChangesNothing()
    {
        // The bar arranges the row four times a second. Anything that moved a tab on the second
        // pass would move it again on the third, and the row would never come to rest.
        string[] ids = { "a.exe", "a.exe", "b.exe", "c.exe", "c.exe" };

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, TabGrouping.Arrange(ids));
    }

    [Fact]
    public void ANewWindowJoinsItsApplicationRatherThanTheEndOfTheRow()
    {
        // This is the point of the feature: a second Notepad appears beside the first.
        Assert.Equal(new[] { "notepad.exe", "notepad.exe", "chrome.exe" },
            Arranged("notepad.exe", "chrome.exe", "notepad.exe"));
    }

    [Fact]
    public void GroupIdsAreMatchedWithoutRegardToCase()
    {
        Assert.Equal(new[] { "Chrome.exe", "CHROME.EXE" },
            Arranged("Chrome.exe", "CHROME.EXE"));
    }

    [Fact]
    public void WindowsWithNoKnownApplicationStayWhereTheyAre()
    {
        Assert.Equal(new[] { "", "a.exe", "a.exe" }, Arranged("", "a.exe", "a.exe"));
    }

    [Fact]
    public void TwoWindowsWithNoKnownApplicationAreNotPutTogether()
    {
        // Windows with nothing in common must not be shown as though they belonged together.
        Assert.Equal(new[] { "", "a.exe", "" }, Arranged("", "a.exe", ""));
    }

    // ---------------------------------------------------------------
    // Which tabs are marked
    // ---------------------------------------------------------------
    [Fact]
    public void AGroupOfOneWindowIsNotMarked()
    {
        // An accent on every tab of a row where no two windows share an application would
        // colour the whole bar and say nothing.
        Assert.Equal(new[] { false, false }, TabGrouping.Marks(new[] { "a.exe", "b.exe" }));
    }

    [Fact]
    public void EveryTabOfAGroupOfTwoIsMarked()
    {
        Assert.Equal(new[] { true, true, false },
            TabGrouping.Marks(new[] { "a.exe", "a.exe", "b.exe" }));
    }

    [Fact]
    public void AWindowWithNoKnownApplicationIsNeverMarked()
    {
        Assert.Equal(new[] { false, false }, TabGrouping.Marks(new[] { "", "" }));
    }

    [Fact]
    public void AnEmptyRowMarksNothing()
    {
        Assert.Empty(TabGrouping.Marks(new List<string>()));
        Assert.Empty(TabGrouping.Marks(null));
    }

    // ---------------------------------------------------------------
    // Dragging
    // ---------------------------------------------------------------

    /// <summary>The row after a drag, so a test reads as the result rather than as three numbers.</summary>
    private static List<string> Dragged(string[] row, int target, int from)
    {
        TabGrouping.DragMove move = TabGrouping.PlanDrag(target, from, row);
        var result = new List<string>(row);

        TabStrip.MoveRange(result, move.Start, move.Count, move.To);
        return result;
    }

    [Fact]
    public void ADragInsideItsOwnGroupMovesTheOneTab()
    {
        string[] row = { "a.exe", "a.exe", "a.exe", "b.exe" };

        TabGrouping.DragMove move = TabGrouping.PlanDrag(2, 0, row);

        Assert.Equal(1, move.Count);
        Assert.Equal(0, move.Start);
        Assert.Equal(2, move.To);
    }

    [Fact]
    public void ADragPastTheNextGroupTakesTheWholeGroupWithIt()
    {
        string[] row = { "a.exe", "a.exe", "b.exe", "b.exe", "c.exe" };

        // Dragging either tab of a gives the same result: a lands after b.
        Assert.Equal(new[] { "b.exe", "b.exe", "a.exe", "a.exe", "c.exe" }, Dragged(row, 3, 0));
        Assert.Equal(new[] { "b.exe", "b.exe", "a.exe", "a.exe", "c.exe" }, Dragged(row, 3, 1));
    }

    [Fact]
    public void ADragToTheLeftPutsTheGroupInFrontOfTheOneItPassed()
    {
        string[] row = { "a.exe", "a.exe", "b.exe", "b.exe", "c.exe" };

        Assert.Equal(new[] { "b.exe", "b.exe", "a.exe", "a.exe", "c.exe" }, Dragged(row, 0, 2));
    }

    [Fact]
    public void AGroupCanPassSeveralGroupsAtOnce()
    {
        string[] row = { "a.exe", "a.exe", "b.exe", "c.exe" };

        Assert.Equal(new[] { "b.exe", "c.exe", "a.exe", "a.exe" }, Dragged(row, 3, 0));
    }

    [Fact]
    public void ATabOfItsOwnTravelsAlone()
    {
        // The whole point of the request: one window of an application is a block of one.
        string[] row = { "a.exe", "a.exe", "b.exe" };

        Assert.Equal(new[] { "b.exe", "a.exe", "a.exe" }, Dragged(row, 0, 2));
    }

    [Fact]
    public void AWindowWithNoKnownApplicationTravelsAlone()
    {
        string[] row = { "a.exe", "a.exe", "" };

        Assert.Equal(new[] { "", "a.exe", "a.exe" }, Dragged(row, 0, 2));
    }

    [Fact]
    public void AGroupDraggedOntoItselfMovesNothing()
    {
        string[] row = { "a.exe", "b.exe" };

        Assert.True(TabGrouping.PlanDrag(0, 0, row).IsNothing);
    }

    [Fact]
    public void ADragFromOutsideTheRowMovesNothing()
    {
        string[] row = { "a.exe", "b.exe" };

        Assert.True(TabGrouping.PlanDrag(1, 9, row).IsNothing);
        Assert.True(TabGrouping.PlanDrag(1, 0, new List<string>()).IsNothing);
    }

    [Fact]
    public void ArrangingAfterAGroupMoveChangesNothing()
    {
        // The move has to survive the next refresh, 250 ms later. Arrange orders groups by where
        // each one's first window sits, so a block move is already the answer it would give. If
        // this fails, a dragged group springs back to where it was.
        string[] row = { "a.exe", "a.exe", "b.exe", "b.exe", "c.exe" };
        List<string> moved = Dragged(row, 3, 0);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, TabGrouping.Arrange(moved));
    }

    // ---------------------------------------------------------------
    // Colour
    // ---------------------------------------------------------------
    [Fact]
    public void AnApplicationKeepsTheSameAccentEveryRun()
    {
        // Fixed numbers on purpose. string.GetHashCode is randomized per process on .NET Core,
        // so a hash taken from it would give a different colour on every start and a different
        // one again on the other target framework. If this test fails, that is what happened.
        Assert.Equal(5, TabGrouping.AccentFor("chrome.exe", null, Palette));
        Assert.Equal(4, TabGrouping.AccentFor("notepad.exe", null, Palette));
        Assert.Equal(2, TabGrouping.AccentFor("code.exe", null, Palette));
    }

    [Fact]
    public void AnAccentIsAlwaysInsideThePalette()
    {
        foreach (string name in new[] { "a", "bb", "ccc", "dddd", "chrome.exe", "explorer.exe" })
        {
            int accent = TabGrouping.AccentFor(name, null, Palette);

            Assert.InRange(accent, 0, Palette - 1);
        }
    }

    [Fact]
    public void AGroupWithAChosenAccentKeepsIt()
    {
        var rules = new List<AppGroup>
        {
            new() { Name = "Browsers", Accent = 3, Executables = new List<string> { "chrome.exe" } },
        };

        Assert.Equal(3, TabGrouping.AccentFor("Browsers", rules, Palette));
    }

    [Fact]
    public void AGroupThatChoseNoAccentGetsOneFromItsName()
    {
        var rules = new List<AppGroup> { Group("Browsers", "chrome.exe") };

        Assert.Equal(TabGrouping.AccentFor("Browsers", null, Palette),
                     TabGrouping.AccentFor("Browsers", rules, Palette));
    }

    [Fact]
    public void AnAccentOutsideThePaletteIsIgnored()
    {
        // The settings file can be edited by hand, and this runs before it is normalized.
        var rules = new List<AppGroup>
        {
            new() { Name = "Browsers", Accent = 99, Executables = new List<string> { "chrome.exe" } },
        };

        Assert.InRange(TabGrouping.AccentFor("Browsers", rules, Palette), 0, Palette - 1);
    }
}
