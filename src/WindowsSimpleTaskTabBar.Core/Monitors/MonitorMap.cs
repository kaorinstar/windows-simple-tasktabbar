namespace WindowsSimpleTaskTabBar.Core.Monitors;

/// <summary>One monitor's rectangle, in screen pixels.</summary>
/// <remarks>
/// Four numbers rather than a <c>Rectangle</c>, so Core stays free of System.Drawing and
/// testable on any platform, the way <c>BarBox</c> holds the bar's rectangle and
/// <c>PreviewBox</c> a preview's.
///
/// Screen pixels, not logical ones. Two monitors at different scale factors still sit in one
/// coordinate space, and it is that space a window rectangle is measured in as well, so the
/// comparison below needs no scaling anywhere.
/// </remarks>
public readonly struct MonitorBox
{
    public MonitorBox(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }

    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Which monitor a window belongs to.
/// </summary>
/// <remarks>
/// Arithmetic over rectangles with no window handle and no Windows API, so the decision that
/// decides which bar a tab appears on can be tested without a screen.
///
/// The rule is the one Windows itself applies for <c>MONITOR_DEFAULTTONEAREST</c>: the monitor
/// the window covers most of, and the nearest monitor when it covers none of any. Doing the
/// arithmetic here rather than calling <c>MonitorFromWindow</c> is what makes it testable, and
/// it also answers in the one form the caller can use - a position in the list of monitors the
/// bars were built from - rather than a monitor handle that would then have to be matched back
/// to a bar.
/// </remarks>
public static class MonitorMap
{
    /// <summary>
    /// The monitor a window sits on: its index in <paramref name="monitors"/>, or -1 when
    /// there are no monitors to choose between.
    /// </summary>
    /// <remarks>
    /// The window rectangle is the one it is drawn at, except for a minimized window, where it
    /// is the rectangle the window restores onto. Windows parks a minimized window at roughly
    /// (-32000, -32000), which is on no monitor and nearest to whichever one reaches furthest
    /// towards the top left, so asking where it is drawn would put its tab on a bar it has
    /// nothing to do with. The caller reads that rectangle; the choice between them is in
    /// <c>WindowService.RestoredBounds</c>.
    ///
    /// Equal claims go to the earlier monitor in the list, which the caller orders with the
    /// primary monitor first. A window split exactly down the middle of two therefore lands on
    /// the primary one rather than moving between bars as the numbers drift.
    /// </remarks>
    public static int Owner(int left, int top, int right, int bottom,
                            IReadOnlyList<MonitorBox> monitors)
    {
        if (monitors == null || monitors.Count == 0) return -1;

        int best = -1;
        long bestArea = 0;

        for (int i = 0; i < monitors.Count; i++)
        {
            long area = OverlapArea(left, top, right, bottom, monitors[i]);
            if (area <= bestArea) continue;

            bestArea = area;
            best = i;
        }

        return best >= 0 ? best : Nearest(left, top, right, bottom, monitors);
    }

    /// <summary>How much of the window lies on one monitor, in square pixels.</summary>
    /// <remarks>
    /// <c>long</c> because the two sides are pixel counts of a desktop that can be several
    /// monitors wide, and their product passes what an <c>int</c> holds well before that.
    /// </remarks>
    private static long OverlapArea(int left, int top, int right, int bottom, MonitorBox monitor)
    {
        long width = Math.Min(right, monitor.Right) - Math.Max(left, monitor.Left);
        long height = Math.Min(bottom, monitor.Bottom) - Math.Max(top, monitor.Top);

        return width > 0 && height > 0 ? width * height : 0;
    }

    /// <summary>
    /// The monitor whose centre is closest to the window's, for a window that is on none of
    /// them.
    /// </summary>
    /// <remarks>
    /// Centre to centre, squared, so nothing here takes a square root: the comparison is the
    /// only thing the distance is used for and squaring keeps the order.
    /// </remarks>
    private static int Nearest(int left, int top, int right, int bottom,
                               IReadOnlyList<MonitorBox> monitors)
    {
        long x = Midpoint(left, right);
        long y = Midpoint(top, bottom);

        int best = 0;
        long bestDistance = long.MaxValue;

        for (int i = 0; i < monitors.Count; i++)
        {
            long dx = x - Midpoint(monitors[i].Left, monitors[i].Right);
            long dy = y - Midpoint(monitors[i].Top, monitors[i].Bottom);
            long distance = dx * dx + dy * dy;

            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// Halfway between two edges, doubled, so that two rectangles an odd number of pixels wide
    /// are compared without either one being rounded.
    /// </summary>
    private static long Midpoint(int near, int far)
    {
        return (long)near + far;
    }
}
