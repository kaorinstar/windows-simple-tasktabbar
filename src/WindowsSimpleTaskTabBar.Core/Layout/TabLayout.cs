namespace WindowsSimpleTaskTabBar.Core.Layout;

/// <summary>
/// Calculations related to tab placement.
/// This logic has no UI dependency, so it can be unit tested on any platform.
/// </summary>
public static class TabLayout
{
    /// <summary>
    /// Calculates the width of a single tab.
    /// </summary>
    /// <param name="availableWidth">Width available for tabs, excluding outer margins.</param>
    /// <param name="tabCount">Number of tabs.</param>
    /// <param name="gap">Space between two tabs.</param>
    /// <param name="minWidth">Minimum tab width.</param>
    /// <param name="maxWidth">Maximum tab width.</param>
    /// <returns>Width of a single tab. Returns 0 when there are no tabs.</returns>
    public static int CalculateTabWidth(int availableWidth, int tabCount, int gap, int minWidth, int maxWidth)
    {
        if (tabCount <= 0) return 0;
        if (minWidth > maxWidth) minWidth = maxWidth;

        // n tabs have n-1 gaps between them, not n. Reserving one gap per tab would
        // leave a permanent sliver of unused space at the end of the row.
        int width = (availableWidth - (tabCount - 1) * gap) / tabCount;

        if (width > maxWidth) width = maxWidth;
        if (width < minWidth) width = minWidth;

        return width;
    }

    /// <summary>
    /// Counts how many tabs fit into the given width.
    /// Tabs that do not fit are not drawn.
    /// </summary>
    public static int CountVisibleTabs(int availableWidth, int tabWidth, int gap)
    {
        if (tabWidth <= 0) return 0;

        int count = 0;
        int used = 0;

        while (used + tabWidth <= availableWidth)
        {
            count++;
            used += tabWidth + gap;
        }

        return count;
    }
}
