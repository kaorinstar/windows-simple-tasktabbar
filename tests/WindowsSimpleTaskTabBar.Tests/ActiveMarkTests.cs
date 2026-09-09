using WindowsSimpleTaskTabBar.Core.Focus;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class ActiveMarkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheWindowInFrontIsTheOneMarked(bool markedIsListed)
    {
        // Whatever was marked before, somebody else's window coming forward takes it.
        Assert.Equal(MarkChoice.TakeForeground,
            ActiveMark.Choose(foregroundIsOwn: false, foregroundIsListed: true, markedIsListed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWindowTheBarDoesNotListTakesTheMarkFromEveryTab(bool markedIsListed)
    {
        // The desktop, or a window the user excluded. None of the tabs is the one in front, and
        // saying so is better than leaving the mark on a window now behind another.
        Assert.Equal(MarkChoice.MarkNothing,
            ActiveMark.Choose(foregroundIsOwn: false, foregroundIsListed: false, markedIsListed));
    }

    [Fact]
    public void TouchingTheBarDoesNotMoveTheMark()
    {
        // The bar activates itself when it is clicked, so this is what a press on a tab looks
        // like from GetForegroundWindow. It matters most at the end of a drag, which leaves no
        // window to activate: without this the row would sit unmarked until the user went
        // somewhere else.
        Assert.Equal(MarkChoice.KeepMarked,
            ActiveMark.Choose(foregroundIsOwn: true, foregroundIsListed: false,
                              markedIsListed: true));
    }

    [Fact]
    public void AMarkedWindowThatHasClosedIsNotKept()
    {
        // Windows gives a closed window's handle to something else in time. A mark held past
        // the window's life could land on whatever inherited the handle.
        Assert.Equal(MarkChoice.MarkNothing,
            ActiveMark.Choose(foregroundIsOwn: true, foregroundIsListed: false,
                              markedIsListed: false));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void EveryCaseHasAnAnswer(bool own, bool foregroundIsListed, bool markedIsListed)
    {
        MarkChoice choice = ActiveMark.Choose(own, foregroundIsListed, markedIsListed);

        Assert.True(choice == MarkChoice.TakeForeground
                    || choice == MarkChoice.KeepMarked
                    || choice == MarkChoice.MarkNothing);
    }
}
