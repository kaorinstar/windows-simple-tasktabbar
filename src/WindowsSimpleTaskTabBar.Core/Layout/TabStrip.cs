namespace WindowsSimpleTaskTabBar.Core.Layout;

/// <summary>
/// The result of measuring a row of tabs against the space available to it.
/// </summary>
public sealed class TabStripLayout
{
    /// <summary>Width of a single tab.</summary>
    public int TabWidth { get; set; }

    /// <summary>Width of the whole row: every tab, plus the gaps between them.</summary>
    public int ContentWidth { get; set; }

    /// <summary>
    /// How far the row can be scrolled. Zero when the whole row fits.
    /// </summary>
    public int MaxScroll { get; set; }

    public bool CanScroll => MaxScroll > 0;
}

/// <summary>
/// Fitting a row of tabs into the bar.
/// </summary>
/// <remarks>
/// Tabs give up space in two stages as their number grows: first they share the width evenly
/// down to a readable minimum, and once that floor is reached the row starts to scroll. A tab
/// therefore always keeps its icon and the first few characters of its title. Nothing is ever
/// dropped.
///
/// This has no UI dependency, so it can be unit tested on any platform.
/// </remarks>
public static class TabStrip
{
    /// <param name="availableWidth">Width the row may occupy.</param>
    /// <param name="tabCount">Number of tabs.</param>
    /// <param name="gap">Space between two tabs.</param>
    /// <param name="minWidth">Narrowest a tab is allowed to be.</param>
    /// <param name="maxWidth">Widest a tab is allowed to be.</param>
    public static TabStripLayout Measure(int availableWidth, int tabCount, int gap,
                                         int minWidth, int maxWidth)
    {
        var layout = new TabStripLayout();
        if (tabCount <= 0) return layout;

        if (minWidth > maxWidth) minWidth = maxWidth;
        if (gap < 0) gap = 0;

        // The width each tab would get if they shared the space evenly.
        // n tabs have n-1 gaps between them, not n.
        int share = (availableWidth - (tabCount - 1) * gap) / tabCount;

        if (share >= maxWidth)
        {
            layout.TabWidth = maxWidth;
        }
        else if (share >= minWidth)
        {
            layout.TabWidth = share;
        }
        else
        {
            // Too many tabs to keep them all readable. Hold the floor and let the row scroll.
            layout.TabWidth = minWidth;
        }

        if (layout.TabWidth < 1) layout.TabWidth = 1;

        layout.ContentWidth = tabCount * layout.TabWidth + (tabCount - 1) * gap;
        layout.MaxScroll = layout.ContentWidth > availableWidth
            ? layout.ContentWidth - availableWidth
            : 0;

        return layout;
    }

    /// <summary>
    /// Brings a scroll position back into range. Called after the tab count or the bar width
    /// changes, either of which can leave a stored position pointing past the end.
    /// </summary>
    public static int ClampScroll(int scroll, int maxScroll)
    {
        if (maxScroll < 0) maxScroll = 0;
        if (scroll < 0) return 0;
        return scroll > maxScroll ? maxScroll : scroll;
    }

    /// <summary>
    /// Returns the scroll position that brings one tab fully into view, moving as little as
    /// possible. A tab already in view leaves the position alone.
    /// </summary>
    public static int ScrollToShow(int index, int tabWidth, int gap, int availableWidth,
                                   int scroll, int maxScroll)
    {
        if (index < 0 || tabWidth <= 0) return ClampScroll(scroll, maxScroll);

        int left = index * (tabWidth + gap);
        int right = left + tabWidth;

        if (left < scroll) scroll = left;
        else if (right > scroll + availableWidth) scroll = right - availableWidth;

        return ClampScroll(scroll, maxScroll);
    }
}
