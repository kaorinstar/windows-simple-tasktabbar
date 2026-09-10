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

    /// <summary>
    /// How many full-width characters of a title a tab keeps at its narrowest. One character is
    /// about as wide as the font is tall, so this is measured in font sizes.
    /// </summary>
    private const int TitleCharacters = 4;

    /// <summary>
    /// Extra room for the ellipsis that marks a clipped title. It is drawn out of the same space
    /// as the text, so without this allowance the last character is what gets dropped for it.
    /// </summary>
    private const int EllipsisCharacters = 1;

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

    /// <summary>Width of one of the arrows that scroll the row.</summary>
    public int ScrollButtonWidth { get; private set; }

    public int TabGap { get; private set; }

    /// <summary>
    /// Narrowest a tab is allowed to be. Tabs stop shrinking here and the row scrolls instead, so
    /// a tab always keeps its icon and the first four characters of its title, followed by an
    /// ellipsis. Icons alone are not enough: a row of windows from one application shows the same
    /// icon over and over, and only the text tells them apart.
    /// </summary>
    public int TabMinWidth { get; private set; }

    public int TabMaxWidth { get; private set; }

    /// <summary>Space between the ends of the bar and the first and last tabs.</summary>
    public int OuterMargin { get; private set; }

    /// <summary>Thickness of the accent a grouped tab carries along its top edge.</summary>
    public int GroupBandHeight { get; private set; }

    /// <summary>Thickness of the rule drawn where one group ends and the next begins.</summary>
    public int GroupDividerWidth { get; private set; }

    /// <summary>Thickness of the outline that marks the active tab.</summary>
    /// <remarks>
    /// An outline rather than a size: the active tab is drawn exactly as large as every other
    /// one. Drawing it taller was tried and removed, because the group accent sits along the top
    /// edge of a tab and a taller tab carried its accent out of line with the tabs beside it.
    /// </remarks>
    public int ActiveOutlineWidth { get; private set; }

    /// <param name="barHeightLogical">Bar height in logical pixels, before DPI scaling.</param>
    /// <param name="scale">DPI scale, where 1.0 is 96 DPI.</param>
    public static BarMetrics For(int barHeightLogical, float scale)
    {
        if (barHeightLogical < 1) barHeightLogical = 1;
        if (scale <= 0f) scale = 1f;

        // Sizes that follow the height. The floors keep text and icons legible on a short bar;
        // without them a compact bar at a low DPI would ask for a 3 pixel font.
        var metrics = new BarMetrics
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
            ScrollButtonWidth = FromHeight(barHeightLogical, scale, 24, 16),
            GroupBandHeight = FromHeight(barHeightLogical, scale, 3, 2),

            // Sizes that do not follow the height: they are about the row, not the bar's thickness.
            TabGap = Scaled(2, scale, 1),
            TabMaxWidth = Scaled(220, scale, 1),
            OuterMargin = Scaled(4, scale, 1),
            GroupDividerWidth = Scaled(1, scale, 1),
            ActiveOutlineWidth = Scaled(1, scale, 1),
        };

        // Room for the icon, the padding either side, and the text with its ellipsis. The text is
        // drawn with TextFormatFlags.NoPadding, so all of this reaches the characters themselves.
        metrics.TabMinWidth = metrics.Padding * 2 + metrics.IconSize + metrics.SmallGap
                              + metrics.FontPixels * (TitleCharacters + EllipsisCharacters);

        return metrics;
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
