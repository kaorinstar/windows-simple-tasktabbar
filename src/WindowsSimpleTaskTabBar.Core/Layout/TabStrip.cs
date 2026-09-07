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
    /// Which slot a dragged tab drops into. The tab takes a slot as soon as its leading edge
    /// passes the middle of that slot, which is what makes the other tabs appear to step aside
    /// while the pointer is still moving.
    /// </summary>
    /// <param name="offset">
    /// Left edge of the dragged tab, measured from the start of the row rather than from the
    /// left of the bar, so a scrolled row needs no special case.
    /// </param>
    /// <param name="tabWidth">Width of a single tab.</param>
    /// <param name="gap">Space between two tabs.</param>
    /// <param name="tabCount">Number of tabs, the dragged one included.</param>
    public static int DropIndex(int offset, int tabWidth, int gap, int tabCount)
    {
        if (tabCount <= 1) return 0;
        if (gap < 0) gap = 0;

        int step = tabWidth + gap;
        if (step <= 0) return 0;

        if (offset < 0) offset = 0;

        int index = (offset + step / 2) / step;
        if (index > tabCount - 1) index = tabCount - 1;
        return index;
    }

    /// <summary>
    /// Moves one item to another position, keeping the others in their relative order. Out of
    /// range positions and a move to where the item already is do nothing.
    /// </summary>
    /// <summary>
    /// Whether the pointer has travelled far enough since the row last shifted for it to shift
    /// again.
    /// </summary>
    /// <remarks>
    /// Without this, dragging past a group flickers. <see cref="DropIndex"/> reads the pointer
    /// alone, so the row's two arrangements - this group before that one, or after it - are
    /// separated by a single pixel of pointer travel. Worse than a shaking hand: a tab that has
    /// just jumped a group is, from its new place, being asked to jump back, and it does so on
    /// every mouse move without the pointer going anywhere at all.
    ///
    /// <paramref name="slots"/> is how far the row shifted last time, not how far it is about to.
    /// The room has to match the jump that was made: undoing a jump of two tabs asks for two tabs
    /// of travel back, the same distance that made it. A shift of one slot is the ordinary swap
    /// with a neighbour, which needs no help - the tab itself takes the slot under the pointer,
    /// leaving half a tab of room - and one tab's width is what that asks for, which is less than
    /// the distance to the next slot. So ordinary reordering is not held back at all.
    /// </remarks>
    public static bool MovedFarEnough(int pointerX, int lastMoveX, int tabWidth, int slots)
    {
        if (tabWidth < 1) tabWidth = 1;
        if (slots < 1) slots = 1;

        int travelled = pointerX - lastMoveX;
        if (travelled < 0) travelled = -travelled;

        return travelled >= tabWidth * slots;
    }

    public static void Move<T>(IList<T> items, int from, int to)
    {
        if (items == null) return;
        if (from < 0 || from >= items.Count) return;
        if (to < 0 || to >= items.Count) return;
        if (from == to) return;

        T item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
    }

    /// <summary>
    /// Takes <paramref name="count"/> neighbouring items from <paramref name="start"/> and puts
    /// them back with the first of them at <paramref name="to"/>, keeping their order.
    /// </summary>
    /// <remarks>
    /// <paramref name="to"/> is an index into the list with the range already taken out, which
    /// is the only reading that lets a range be moved to either side without two rules. Moving
    /// one item is the same as <see cref="Move"/>.
    /// </remarks>
    public static void MoveRange<T>(IList<T> items, int start, int count, int to)
    {
        if (items == null) return;
        if (count <= 0) return;
        if (start < 0 || start + count > items.Count) return;

        int remaining = items.Count - count;
        if (to < 0 || to > remaining) return;
        if (to == start) return;

        var moved = new List<T>(count);
        for (int i = 0; i < count; i++) moved.Add(items[start + i]);

        for (int i = 0; i < count; i++) items.RemoveAt(start);
        for (int i = 0; i < count; i++) items.Insert(to + i, moved[i]);
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
