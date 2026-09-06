namespace WindowsSimpleTaskTabBar.Core.Settings;

/// <summary>
/// How tall the bar is drawn.
/// </summary>
public enum BarHeightMode
{
    /// <summary>The original height, with room for a 16 pixel icon and readable text.</summary>
    Standard = 0,

    /// <summary>A shorter bar, for giving the height back to the windows below it.</summary>
    Compact = 1,
}

/// <summary>
/// Every user setting, in one place.
/// This type has no UI and no Windows API dependency, so it can be unit tested on any platform.
/// </summary>
/// <remarks>
/// Plain public properties with a parameterless constructor: the settings file is written by
/// serializing this type directly, so it must stay a simple data holder.
/// </remarks>
public class AppSettings
{
    /// <summary>
    /// The schema this instance was written with. Present from the first release so that a later
    /// version can tell an old file apart from a new one instead of guessing.
    /// </summary>
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public BarHeightMode BarHeight { get; set; } = BarHeightMode.Standard;

    /// <summary>Bar height in logical pixels, before any DPI scaling.</summary>
    public static int HeightInPixels(BarHeightMode mode)
    {
        return mode == BarHeightMode.Compact ? 24 : 34;
    }

    /// <summary>
    /// Returns a copy with every value brought back into range.
    /// A settings file can be edited by hand or written by a different version, so nothing read
    /// from it is trusted.
    /// </summary>
    public AppSettings Normalized()
    {
        return new AppSettings
        {
            Schema = CurrentSchema,
            BarHeight = IsKnown(BarHeight) ? BarHeight : BarHeightMode.Standard,
        };
    }

    private static bool IsKnown(BarHeightMode mode)
    {
        return mode == BarHeightMode.Standard || mode == BarHeightMode.Compact;
    }
}
