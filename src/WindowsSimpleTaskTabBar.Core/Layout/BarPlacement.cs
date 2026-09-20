using WindowsSimpleTaskTabBar.Core.Settings;

namespace WindowsSimpleTaskTabBar.Core.Layout;

/// <summary>
/// One edge of a screen.
/// </summary>
/// <remarks>
/// The numbers are the ones the Windows API uses for an AppBar edge (<c>ABE_LEFT</c> to
/// <c>ABE_BOTTOM</c>), so the value read from the taskbar needs no translation table. Nothing
/// here calls Windows: this is arithmetic that has to run on any platform for the tests.
///
/// The taskbar can be on any of the four. The bar itself uses <see cref="Top"/> and
/// <see cref="Bottom"/> alone, because a row of tabs is horizontal; standing it on a side edge
/// is a separate piece of work (#17).
/// </remarks>
public enum ScreenEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>Where the bar sits, in screen pixels.</summary>
/// <remarks>
/// Four numbers rather than a <c>Rectangle</c>, so Core stays free of System.Drawing and
/// testable on any platform, the way <c>PreviewBox</c> holds a preview's position.
///
/// Left, top, right and bottom rather than a position and a size, because that is the shape the
/// AppBar messages read and write and the conversion belongs in one place.
/// </remarks>
public readonly struct BarBox
{
    public BarBox(int left, int top, int right, int bottom)
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
/// Which edge the bar sits on, and the rectangle it occupies there.
/// </summary>
/// <remarks>
/// Arithmetic over pixels with no window handle and no Windows API, so the decision that is
/// easiest to get wrong - a bar that lands under the taskbar, or keeps its thickness on the
/// wrong side - can be tested without a screen.
/// </remarks>
public static class BarPlacement
{
    /// <summary>
    /// The edge the bar sits on: the one the setting names, or the taskbar's own when the
    /// setting is to follow it.
    /// </summary>
    /// <remarks>
    /// A taskbar on the left or the right edge leaves the bar at the bottom. The bar is a
    /// horizontal row of tabs, so it has no side edge to follow the taskbar onto, and the bottom
    /// is where it has always been. The AppBar registration still keeps the two from overlapping.
    /// </remarks>
    /// <param name="setting">What the user chose.</param>
    /// <param name="taskbarEdge">
    /// Which edge the Windows taskbar is on. The caller passes <see cref="ScreenEdge.Bottom"/>
    /// when Windows does not answer, which is where the taskbar is unless it has been moved.
    /// </param>
    public static ScreenEdge Resolve(BarEdgeMode setting, ScreenEdge taskbarEdge)
    {
        switch (setting)
        {
            case BarEdgeMode.Top:
                return ScreenEdge.Top;

            case BarEdgeMode.Bottom:
                return ScreenEdge.Bottom;

            default:
                return taskbarEdge == ScreenEdge.Top ? ScreenEdge.Top : ScreenEdge.Bottom;
        }
    }

    /// <summary>
    /// The strip the bar asks the system for: the full width of the screen, against
    /// <paramref name="edge"/>, <paramref name="thickness"/> pixels deep.
    /// </summary>
    /// <remarks>
    /// What is asked for, not what is granted. The system moves this rectangle clear of the
    /// taskbar and of any other AppBar, and <see cref="Settled"/> turns the answer back into a
    /// bar of the right thickness.
    /// </remarks>
    public static BarBox Requested(int screenLeft, int screenTop, int screenRight,
                                   int screenBottom, ScreenEdge edge, int thickness)
    {
        if (thickness < 1) thickness = 1;

        return edge == ScreenEdge.Top
            ? new BarBox(screenLeft, screenTop, screenRight, screenTop + thickness)
            : new BarBox(screenLeft, screenBottom - thickness, screenRight, screenBottom);
    }

    /// <summary>
    /// The bar's rectangle, from the one the system moved clear of everything else.
    /// </summary>
    /// <remarks>
    /// The system answers with a rectangle that is free, which can be deeper than the bar needs.
    /// The thickness is put back against the edge the bar sits on, so the bar stays against the
    /// taskbar rather than floating away from it: the far side is the one that moves.
    /// </remarks>
    public static BarBox Settled(BarBox granted, ScreenEdge edge, int thickness)
    {
        if (thickness < 1) thickness = 1;

        return edge == ScreenEdge.Top
            ? new BarBox(granted.Left, granted.Top, granted.Right, granted.Top + thickness)
            : new BarBox(granted.Left, granted.Bottom - thickness, granted.Right, granted.Bottom);
    }
}
