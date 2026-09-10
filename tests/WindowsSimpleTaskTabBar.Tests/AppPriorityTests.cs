using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Ordering;
using WindowsSimpleTaskTabBar.Core.Settings;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class AppPriorityTests
{
    // ---------------------------------------------------------------
    // Rank
    // ---------------------------------------------------------------

    [Fact]
    public void PositionInTheListIsTheRank()
    {
        var priority = new List<string> { "code.exe", "chrome.exe", "cursor.exe" };

        Assert.Equal(0, AppPriority.Rank("code.exe", priority));
        Assert.Equal(1, AppPriority.Rank("chrome.exe", priority));
        Assert.Equal(2, AppPriority.Rank("cursor.exe", priority));
    }

    [Fact]
    public void AnApplicationTheListDoesNotNameRanksLast()
    {
        var priority = new List<string> { "code.exe" };

        Assert.Equal(int.MaxValue, AppPriority.Rank("notepad.exe", priority));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AWindowWithNoExecutableRanksLast(string executable)
    {
        // The process could not be read. Nothing is known about it, so it cannot be placed.
        var priority = new List<string> { "code.exe" };

        Assert.Equal(int.MaxValue, AppPriority.Rank(executable, priority));
    }

    [Fact]
    public void EverythingRanksLastWhenTheListIsEmptyOrMissing()
    {
        Assert.Equal(int.MaxValue, AppPriority.Rank("code.exe", new List<string>()));
        Assert.Equal(int.MaxValue, AppPriority.Rank("code.exe", null));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Editor\CODE.EXE")]
    [InlineData("Code.exe")]
    public void APathOrADifferentCaseFindsTheSameRank(string executable)
    {
        // The same normalization grouping uses, so the priority list and the window agree
        // however either of them was written.
        var priority = new List<string> { "code.exe", "chrome.exe" };

        Assert.Equal(0, AppPriority.Rank(executable, priority));
    }

    [Fact]
    public void APathInTheListItselfFindsTheWindowItNames()
    {
        var priority = new List<string> { @"C:\Program Files\Editor\code.exe" };

        Assert.Equal(0, AppPriority.Rank("code.exe", priority));
    }

    // ---------------------------------------------------------------
    // Where a newly opened window goes
    // ---------------------------------------------------------------

    [Fact]
    public void ANewWindowGoesInFrontOfEverythingRankedBelowIt()
    {
        var priority = new List<string> { "code.exe", "chrome.exe" };
        var row = new List<string> { "chrome.exe", "notepad.exe" };

        Assert.Equal(0, AppPriority.InsertionIndex(row, "code.exe", priority));
    }

    [Fact]
    public void ANewWindowJoinsTheOnesOfItsOwnRankAtTheBackOfThem()
    {
        // Equal rank keeps the order the windows were opened in, so a second window of one
        // application lands after the first rather than in front of it.
        var priority = new List<string> { "code.exe", "chrome.exe" };
        var row = new List<string> { "code.exe", "code.exe", "chrome.exe" };

        Assert.Equal(2, AppPriority.InsertionIndex(row, "code.exe", priority));
    }

    [Fact]
    public void AnApplicationTheListDoesNotNameGoesToTheRightEnd()
    {
        var priority = new List<string> { "code.exe" };
        var row = new List<string> { "code.exe", "notepad.exe" };

        Assert.Equal(2, AppPriority.InsertionIndex(row, "paint.exe", priority));
    }

    [Fact]
    public void EveryWindowIsAppendedWhileTheListIsEmpty()
    {
        // What the bar did before this setting existed, and what it still does for anyone who
        // leaves the setting alone.
        var row = new List<string> { "chrome.exe", "notepad.exe" };

        Assert.Equal(2, AppPriority.InsertionIndex(row, "code.exe", new List<string>()));
        Assert.Equal(2, AppPriority.InsertionIndex(row, "code.exe", null));
    }

    [Fact]
    public void TheFirstWindowOfAnEmptyRowGoesAtTheFront()
    {
        var priority = new List<string> { "code.exe" };

        Assert.Equal(0, AppPriority.InsertionIndex(new List<string>(), "code.exe", priority));
    }

    [Fact]
    public void AWindowGoesInFrontOfOneWhoseExecutableCouldNotBeRead()
    {
        // An unreadable executable ranks last, like an application the list does not name.
        var priority = new List<string> { "code.exe" };
        var row = new List<string> { string.Empty, "notepad.exe" };

        Assert.Equal(0, AppPriority.InsertionIndex(row, "code.exe", priority));
    }

    // ---------------------------------------------------------------
    // Putting a whole row into the order
    // ---------------------------------------------------------------

    [Fact]
    public void SortingPutsTheRowInThePriorityOrder()
    {
        var priority = new List<string> { "code.exe", "cursor.exe" };
        var row = new List<string> { "cursor.exe", "code.exe" };

        Assert.Equal(new[] { 1, 0 }, AppPriority.Sort(row, priority));
    }

    [Fact]
    public void SortingLeavesEqualRanksInTheOrderTheyWereIn()
    {
        // Every application the list does not name is of equal rank, so an unstable sort would
        // shuffle the tabs of everything the user never mentioned.
        var priority = new List<string> { "code.exe" };
        var row = new List<string> { "notepad.exe", "paint.exe", "code.exe", "chrome.exe" };

        Assert.Equal(new[] { 2, 0, 1, 3 }, AppPriority.Sort(row, priority));
    }

    [Fact]
    public void SortingAnOrderedRowChangesNothing()
    {
        var priority = new List<string> { "code.exe", "chrome.exe" };
        var row = new List<string> { "code.exe", "chrome.exe", "notepad.exe" };

        Assert.Equal(new[] { 0, 1, 2 }, AppPriority.Sort(row, priority));
    }

    [Fact]
    public void SortingWithNoListLeavesTheRowAsItIs()
    {
        var row = new List<string> { "notepad.exe", "code.exe", "chrome.exe" };

        Assert.Equal(new[] { 0, 1, 2 }, AppPriority.Sort(row, new List<string>()));
        Assert.Equal(new[] { 0, 1, 2 }, AppPriority.Sort(row, null));
    }

    [Fact]
    public void SortingAnEmptyRowGivesAnEmptyOrder()
    {
        Assert.Empty(AppPriority.Sort(new List<string>(), new List<string> { "code.exe" }));
        Assert.Empty(AppPriority.Sort(null, new List<string> { "code.exe" }));
    }

    [Fact]
    public void SortingIsWhatBringsAGroupToTheFrontWithIt()
    {
        // The second row of the table in the issue. Grouping runs after the sort, and
        // TabGrouping.Arrange puts a group where its first window sits, so ranking code.exe
        // first carries the group it shares with cursor.exe to the front - and chrome.exe,
        // ranked between the two, ends up behind both.
        var priority = new List<string> { "code.exe", "chrome.exe", "cursor.exe" };
        var opened = new List<string> { "chrome.exe", "cursor.exe", "code.exe" };

        List<int> sorted = AppPriority.Sort(opened, priority);
        Assert.Equal(new[] { 2, 0, 1 }, sorted);

        // code.exe and cursor.exe are one group of the user's; chrome.exe is its own.
        var groups = new List<AppGroup>
        {
            new AppGroup
            {
                Name = "Editors",
                Executables = new List<string> { "code.exe", "cursor.exe" },
            },
        };

        var groupIds = new List<string>();
        foreach (int index in sorted)
            groupIds.Add(TabGrouping.GroupIdFor(opened[index], groups));

        var shown = new List<string>();
        foreach (int index in TabGrouping.Arrange(groupIds)) shown.Add(opened[sorted[index]]);

        Assert.Equal(new[] { "code.exe", "cursor.exe", "chrome.exe" }, shown);
    }
}
