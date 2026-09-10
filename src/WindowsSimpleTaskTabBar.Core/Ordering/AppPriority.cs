using WindowsSimpleTaskTabBar.Core.Grouping;

namespace WindowsSimpleTaskTabBar.Core.Ordering;

/// <summary>
/// Decides where a tab goes from the order the user put their applications in.
/// </summary>
/// <remarks>
/// Pure calculation over strings: no window handle and no Windows API, so it is unit tested on
/// any platform. The bar hands it one executable name per tab, in the order the row is drawn,
/// and gets back a position or an order.
///
/// The list is a priority order, not a sort key the row is held to. It decides where a tab is
/// <em>inserted</em>, and the whole row is put in it only when the user changes the list or the
/// bar starts. Enforcing it on every pass would undo a drag within 250 ms, which is the reason
/// <c>docs/architecture.md</c> already gives for not letting a single tab leave its group.
/// </remarks>
public static class AppPriority
{
    /// <summary>
    /// Where an executable sits in the priority list, counting from zero. An executable the list
    /// does not name ranks <see cref="int.MaxValue"/>, which is last.
    /// </summary>
    /// <remarks>
    /// Both sides go through <see cref="TabGrouping.KeyFor"/>, so a full path in a hand-edited
    /// settings file still matches the window it names, and the priority list and the grouping
    /// rules compare names the same way. A second normalization would let the two disagree.
    /// </remarks>
    public static int Rank(string executable, IList<string> priority)
    {
        if (priority == null || priority.Count == 0) return int.MaxValue;

        string key = TabGrouping.KeyFor(executable);
        if (key.Length == 0) return int.MaxValue;

        for (int i = 0; i < priority.Count; i++)
        {
            if (string.Equals(TabGrouping.KeyFor(priority[i]), key, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Where a newly opened window belongs in a row, as an index into
    /// <paramref name="rowKeys"/>: in front of the first tab that ranks below it, and at the end
    /// when none does.
    /// </summary>
    /// <remarks>
    /// In front of the first tab ranking below it, rather than behind the last tab ranking above
    /// it, so that a window joins the ones of its own rank at the back of them. Two windows of
    /// one application therefore stay in the order they were opened in, and an application the
    /// list does not name is appended, which is what the bar did before this setting existed.
    ///
    /// The row is read as it stands, dragging included. This answers where to put one new tab;
    /// it does not tidy the tabs that are already there.
    /// </remarks>
    public static int InsertionIndex(IList<string> rowKeys, string executable, IList<string> priority)
    {
        if (rowKeys == null) return 0;

        int rank = Rank(executable, priority);
        if (rank == int.MaxValue) return rowKeys.Count;

        for (int i = 0; i < rowKeys.Count; i++)
        {
            if (Rank(rowKeys[i], priority) > rank) return i;
        }

        return rowKeys.Count;
    }

    /// <summary>
    /// The order to draw a whole row in, as indexes into <paramref name="rowKeys"/>: by rank,
    /// with tabs of equal rank left in the order they are already in.
    /// </summary>
    /// <remarks>
    /// For the two moments the row is put in priority order outright - the bar starting, and the
    /// user changing the list - so that the setting shows its effect at once rather than only on
    /// the next window opened.
    ///
    /// Insertion sort, because it is stable and <c>List.Sort</c> is not. Equal rank has to keep
    /// the order it arrived in: every application the list does not name is of equal rank, so an
    /// unstable sort would shuffle the tabs of everything the user did not mention. A row is
    /// tens of tabs at the very most, and this runs twice a session rather than four times a
    /// second.
    /// </remarks>
    public static List<int> Sort(IList<string> rowKeys, IList<string> priority)
    {
        var order = new List<int>();
        if (rowKeys == null || rowKeys.Count == 0) return order;

        var ranks = new int[rowKeys.Count];
        for (int i = 0; i < rowKeys.Count; i++)
        {
            ranks[i] = Rank(rowKeys[i], priority);
            order.Add(i);
        }

        for (int i = 1; i < order.Count; i++)
        {
            int moving = order[i];
            int at = i - 1;

            while (at >= 0 && ranks[order[at]] > ranks[moving])
            {
                order[at + 1] = order[at];
                at--;
            }

            order[at + 1] = moving;
        }

        return order;
    }
}
