namespace WindowsSimpleTaskTabBar.Core.Layout;

/// <summary>
/// Every pixel size the bar draws with, derived in one place from its height and the DPI scale.
/// </summary>
/// <remarks>
/// The bar's height is a setting, so the icon size, the text size, the padding and the rest have
/// to move with it. Deriving them here keeps that relationship in one place instead of scattering
/// constants through the painting code, and makes the arithmetic testable on any platform.
/// All values are in device pixels, already scaled.
/// </remarks>
public sealed class BarMetrics
{
    /// <summary>The height the proportions below are written against.</summary>
    private const int ReferenceHeight = 34;

    private BarMetrics() { }

    public int BarHeight { get; private set; }

    /// <summary>Gap between the top of the bar and the top of a tab.</summary>
    public int TopOffset { get; private set; }

    public int IconSize { get; private set; }

    /// <summary>Font size in pixels, not points.</summary>
    public int FontPixels { get; private set; }

    /// <summary>Space between a tab's edge and its contents.</summary>
    public int Padding { get; private set; }

    public int CornerRadius { get; private set; }

    /// <summary>Small separation inside a tab: icon to text, close button to the tab's edge.</summary>
    public int SmallGap { get; private set; }

    public int CloseButtonSize { get; private set; }

    /// <summary>A tab narrower than this has no room for a close button.</summary>
    public int CloseButtonMinTabWidth { get; private set; }

    public int TabGap { get; private set; }
    public int TabMinWidth { get; private set; }
    public int TabMaxWidth { get; private set; }

    /// <summary>Space between the ends of the bar and the first and last tabs.</summary>
    public int OuterMargin { get; private set; }

    /// <param name="barHeightLogical">Bar height in logical pixels, before DPI scaling.</param>
    /// <param name="scale">DPI scale, where 1.0 is 96 DPI.</param>
    public static BarMetrics For(int barHeightLogical, float scale)
    {
        if (barHeightLogical < 1) barHeightLogical = 1;
        if (scale <= 0f) scale = 1f;

        // Sizes that follow the height. The floors keep text and icons legible on a short bar;
        // without them a compact bar at a low DPI would ask for a 3 pixel font.
        return new BarMetrics
        {
            BarHeight = Scaled(barHeightLogical, scale, 1),
            TopOffset = FromHeight(barHeightLogical, scale, 3, 2),
            IconSize = FromHeight(barHeightLogical, scale, 16, 12),
            FontPixels = FromHeight(barHeightLogical, scale, 12, 10),
            Padding = FromHeight(barHeightLogical, scale, 8, 4),
            CornerRadius = FromHeight(barHeightLogical, scale, 6, 3),
            SmallGap = FromHeight(barHeightLogical, scale, 6, 3),
            CloseButtonSize = FromHeight(barHeightLogical, scale, 16, 12),
            CloseButtonMinTabWidth = FromHeight(barHeightLogical, scale, 90, 40),
            TabMinWidth = FromHeight(barHeightLogical, scale, 46, 28),

            // Sizes that do not follow the height: they are about the row, not the bar's thickness.
            TabGap = Scaled(2, scale, 1),
            TabMaxWidth = Scaled(220, scale, 1),
            OuterMargin = Scaled(4, scale, 1),
        };
    }

    /// <summary>
    /// Scales <paramref name="atReferenceHeight"/> in proportion to the bar's height, then by the
    /// DPI scale, never going below <paramref name="floor"/> logical pixels.
    /// </summary>
    private static int FromHeight(int barHeightLogical, float scale, int atReferenceHeight, int floor)
    {
        int logical = (int)System.Math.Round(
            atReferenceHeight * (double)barHeightLogical / ReferenceHeight);

        if (logical < floor) logical = floor;
        return Scaled(logical, scale, 1);
    }

    private static int Scaled(int logical, float scale, int minimum)
    {
        int value = (int)System.Math.Round(logical * (double)scale);
        return value < minimum ? minimum : value;
    }
}
