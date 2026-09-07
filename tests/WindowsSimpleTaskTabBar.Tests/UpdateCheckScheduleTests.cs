using System.Globalization;
using System.Threading;
using WindowsSimpleTaskTabBar.Core.Update;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class UpdateCheckScheduleTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yesterday")]
    [InlineData("2026-09-07")]
    public void ACheckIsDueWhenNoTimeCanBeReadFromTheSettings(string stored)
    {
        // A settings file written before this setting existed, and one edited by hand into
        // something unreadable, are the same case: nothing is known, so look.
        Assert.True(UpdateCheckSchedule.IsDue(stored, Now));
    }

    [Fact]
    public void ACheckIsNotDueWithinTheDay()
    {
        string stored = UpdateCheckSchedule.Stamp(Now.AddHours(-23));

        Assert.False(UpdateCheckSchedule.IsDue(stored, Now));
    }

    [Fact]
    public void ACheckIsDueOnceTheDayHasPassed()
    {
        string stored = UpdateCheckSchedule.Stamp(Now.AddHours(-UpdateCheckSchedule.IntervalHours));

        Assert.True(UpdateCheckSchedule.IsDue(stored, Now));
    }

    [Fact]
    public void ACheckIsDueWhenTheStoredTimeIsInTheFuture()
    {
        // A settings file copied from another machine, or a clock that was set forward once and
        // then corrected, would otherwise stop the check for good.
        string stored = UpdateCheckSchedule.Stamp(Now.AddDays(30));

        Assert.True(UpdateCheckSchedule.IsDue(stored, Now));
    }

    [Fact]
    public void ATimeSurvivesBeingWrittenAndReadBack()
    {
        Assert.Equal(Now, UpdateCheckSchedule.ParseStamp(UpdateCheckSchedule.Stamp(Now)));
    }

    [Fact]
    public void ALocalTimeIsWrittenAsTheUniversalOneItStandsFor()
    {
        DateTime local = Now.ToLocalTime();

        Assert.Equal(UpdateCheckSchedule.Stamp(Now), UpdateCheckSchedule.Stamp(local));
    }

    [Fact]
    public void ATimeIsWrittenTheSameWayWhateverTheMachineIsSetTo()
    {
        // A machine whose regional settings write dates differently has to read back what an
        // earlier run of itself wrote.
        CultureInfo original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ja-JP");
            string japanese = UpdateCheckSchedule.Stamp(Now);

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(japanese, UpdateCheckSchedule.Stamp(Now));
            Assert.Equal(Now, UpdateCheckSchedule.ParseStamp(japanese));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void AnUnreadableTimeIsNoTimeAtAll()
    {
        Assert.Equal(DateTime.MinValue, UpdateCheckSchedule.ParseStamp("not a time"));
    }
}
