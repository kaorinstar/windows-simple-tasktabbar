using System.Globalization;

namespace WindowsSimpleTaskTabBar.Core.Update;

/// <summary>
/// Decides whether it is time to look for a new release, and formats the time of the last look
/// for the settings file.
/// This type has no UI and no Windows API dependency, so it can be unit tested on any platform.
/// </summary>
/// <remarks>
/// The current time is passed in rather than read here, so the decision stays a pure function and
/// can be tested without waiting a day for it.
/// </remarks>
public static class UpdateCheckSchedule
{
    /// <summary>How long a check lasts before another one is due.</summary>
    /// <remarks>
    /// A day. A release appears every few weeks, so checking more often would only add requests,
    /// and checking less often would leave a fix sitting unnoticed.
    /// </remarks>
    public const int IntervalHours = 24;

    /// <summary>The format written to the settings file: ISO 8601, in UTC, to the second.</summary>
    /// <remarks>
    /// A <c>DateTime</c> property would be shorter, but <c>DataContractJsonSerializer</c> writes
    /// one as <c>/Date(1757246400000+0900)/</c>, which carries a time zone, cannot be read by a
    /// person and cannot sensibly be typed by one either. The settings file is meant to be
    /// editable by hand.
    /// </remarks>
    private const string StampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>
    /// Whether a check should run now, given when the last one reached GitHub.
    /// </summary>
    /// <remarks>
    /// A time that has not been written yet, or one that cannot be read, counts as never checked.
    /// So does a time later than now: a settings file copied from another machine, or a clock
    /// that was once set forward and then corrected, would otherwise stop the check for good.
    /// </remarks>
    public static bool IsDue(string lastCheckUtc, DateTime utcNow)
    {
        DateTime last = ParseStamp(lastCheckUtc);
        if (last == DateTime.MinValue) return true;
        if (last > utcNow) return true;

        return utcNow - last >= TimeSpan.FromHours(IntervalHours);
    }

    /// <summary>Formats a time for the settings file.</summary>
    public static string Stamp(DateTime utcNow)
    {
        return utcNow.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a time back, or <see cref="DateTime.MinValue"/> when the text is not one.
    /// </summary>
    public static DateTime ParseStamp(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DateTime.MinValue;

        // Invariant, so a machine whose regional settings write dates differently still reads
        // back what an earlier version of itself wrote.
        return DateTime.TryParseExact(text.Trim(), StampFormat, CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                   out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
    }
}
