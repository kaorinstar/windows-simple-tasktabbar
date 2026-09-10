namespace WindowsSimpleTaskTabBar.Core.Preview;

/// <summary>Where a window preview goes, in screen pixels.</summary>
/// <remarks>
/// Four numbers rather than a <c>Rectangle</c>, so that Core stays free of System.Drawing and
/// testable on any platform, the way <c>BarPalette</c> holds colours as plain numbers.
/// </remarks>
public readonly struct PreviewBox
{
    public PreviewBox(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}

/// <summary>
/// How big a window preview is, and where it sits.
/// </summary>
/// <remarks>
/// Arithmetic over pixels with no window handle and no Windows API, so the two decisions that
/// are easy to get wrong - a picture that comes out stretched, and one that runs off the side of
/// the screen - can be tested without a screen.
/// </remarks>
public static class PreviewPlacement
{
    /// <summary>
    /// The size to draw a window at: as large as the room allows, in the window's own shape.
    /// </summary>
    /// <remarks>
    /// Never larger than the window itself. A small window scaled up is a blurred picture of
    /// something the user could have seen sharply, and the point of the preview is to be
    /// recognised at a glance.
    ///
    /// A width or a height of zero means there is nothing to show, which is what an unreadable
    /// source size comes back as. The caller shows no preview rather than an empty box.
    /// </remarks>
    /// <param name="sourceWidth">Width of the window being previewed.</param>
    /// <param name="sourceHeight">Height of the window being previewed.</param>
    /// <param name="maxWidth">Widest the preview may be.</param>
    /// <param name="maxHeight">Tallest the preview may be, which is the room above the bar.</param>
    public static void Fit(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight,
                           out int width, out int height)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || maxWidth <= 0 || maxHeight <= 0)
        {
            width = 0;
            height = 0;
            return;
        }

        if (sourceWidth <= maxWidth && sourceHeight <= maxHeight)
        {
            width = sourceWidth;
            height = sourceHeight;
            return;
        }

        // Which side runs out of room first, as a comparison of maxWidth / sourceWidth against
        // maxHeight / sourceHeight with both divisions multiplied out. Long, because two screen
        // dimensions multiplied together pass what an int holds on a large display.
        long limitedByWidth = (long)maxWidth * sourceHeight;
        long limitedByHeight = (long)maxHeight * sourceWidth;

        if (limitedByWidth <= limitedByHeight)
        {
            width = maxWidth;
            height = (int)((long)sourceHeight * maxWidth / sourceWidth);
        }
        else
        {
            height = maxHeight;
            width = (int)((long)sourceWidth * maxHeight / sourceHeight);
        }

        // A window far wider than it is tall rounds its short side down to nothing.
        if (width < 1) width = 1;
        if (height < 1) height = 1;
    }

    /// <summary>
    /// Where to put a preview of that size: centred over its tab, above the bar, and on screen.
    /// </summary>
    /// <remarks>
    /// Nothing brings the preview back down from the top of the screen, because nothing can put
    /// it there: <see cref="Fit"/> is given the room above the bar as its maximum height, so a
    /// preview is never taller than the space it is about to occupy.
    /// </remarks>
    /// <param name="tabLeft">Left edge of the tab, in screen pixels.</param>
    /// <param name="barTop">Top edge of the bar, in screen pixels.</param>
    /// <param name="screenLeft">Left edge of the screen the bar is on.</param>
    /// <param name="screenRight">Right edge of that screen.</param>
    /// <param name="gap">Space left between the preview and the bar.</param>
    public static PreviewBox Place(int width, int height, int tabLeft, int tabWidth,
                                   int barTop, int screenLeft, int screenRight, int gap)
    {
        int x = tabLeft + (tabWidth - width) / 2;

        // Back onto the screen. The left edge is applied second, so a preview wider than the
        // screen keeps its left side rather than its right: a window's title bar and menus are
        // there, and they are what identifies it.
        int rightMost = screenRight - width;
        if (x > rightMost) x = rightMost;
        if (x < screenLeft) x = screenLeft;

        return new PreviewBox(x, barTop - gap - height, width, height);
    }
}
