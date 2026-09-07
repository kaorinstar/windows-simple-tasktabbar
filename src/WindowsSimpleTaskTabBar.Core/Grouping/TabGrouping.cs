using WindowsSimpleTaskTabBar.Core.Settings;

namespace WindowsSimpleTaskTabBar.Core.Grouping;

/// <summary>
/// Decides which group a window belongs to and what order the row is shown in.
/// </summary>
/// <remarks>
/// Pure calculation over strings: no window handle, no colour, no Windows API. The bar hands it
/// one group name per tab in the current order and gets back the order to draw them in.
/// </remarks>
public static class TabGrouping
{
    /// <summary>
    /// The grouping key for an executable: its file name in lower case. An empty or unreadable
    /// path gives an empty key, which never joins a group.
    /// </summary>
    /// <remarks>
    /// The file name rather than the full path, because that is how people recognise an
    /// application. Two copies of the same program installed in different folders are the same
    /// application to the person looking at the bar.
    /// </remarks>
    public static string KeyFor(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return string.Empty;

        int cut = executablePath.LastIndexOfAny(new[] { '\\', '/' });
        string name = cut >= 0 ? executablePath.Substring(cut + 1) : executablePath;
        return name.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// The group an executable belongs to: the name of the first rule that lists it, or the
    /// executable itself when no rule does.
    /// </summary>
    public static string GroupIdFor(string key, IList<AppGroup> rules)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (rules == null) return key;

        foreach (AppGroup rule in rules)
        {
            if (rule?.Executables == null || string.IsNullOrEmpty(rule.Name)) continue;

            foreach (string executable in rule.Executables)
            {
                if (string.Equals(executable, key, StringComparison.OrdinalIgnoreCase))
                    return rule.Name;
            }
        }

        return key;
    }

    /// <summary>
    /// The order to draw the row in, as indexes into <paramref name="groupIds"/>. Tabs that
    /// share a group are brought together; a group takes the place of its first tab, and the
    /// tabs inside it keep the order they arrived in.
    /// </summary>
    /// <remarks>
    /// This has to give back an arranged row unchanged. The bar arranges the row four times a
    /// second, so anything that moved a tab on a second pass would move it again on the third,
    /// and the row would never come to rest.
    ///
    /// An empty group id means the executable could not be read. Those windows are each left on
    /// their own rather than collected into one group of unknowns: windows with nothing in
    /// common would be shown as though they belonged together.
    /// </remarks>
    public static List<int> Arrange(IList<string> groupIds)
    {
        var order = new List<int>();
        if (groupIds == null || groupIds.Count == 0) return order;

        var buckets = new List<List<int>>();
        var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < groupIds.Count; i++)
        {
            string id = groupIds[i] ?? string.Empty;

            // An unknown executable gets a bucket of its own, which nothing else can join.
            if (id.Length == 0)
            {
                buckets.Add(new List<int> { i });
                continue;
            }

            if (byId.TryGetValue(id, out int bucket))
            {
                buckets[bucket].Add(i);
            }
            else
            {
                byId[id] = buckets.Count;
                buckets.Add(new List<int> { i });
            }
        }

        foreach (List<int> bucket in buckets) order.AddRange(bucket);
        return order;
    }

    /// <summary>
    /// Whether each tab of an arranged row should be marked with its group's accent.
    /// </summary>
    /// <remarks>
    /// A group of one window is left plain. The accent says "these belong together", which a
    /// single tab has nothing to say to; marking every tab of a row where no two windows share
    /// an application would colour the whole bar and tell the user nothing.
    /// </remarks>
    public static bool[] Marks(IList<string> arrangedGroupIds)
    {
        if (arrangedGroupIds == null || arrangedGroupIds.Count == 0) return new bool[0];

        var marks = new bool[arrangedGroupIds.Count];

        int start = 0;
        while (start < arrangedGroupIds.Count)
        {
            int end = EndOfRun(arrangedGroupIds, start);
            bool marked = end > start && !string.IsNullOrEmpty(arrangedGroupIds[start]);

            for (int i = start; i <= end; i++) marks[i] = marked;
            start = end + 1;
        }

        return marks;
    }

    /// <summary>
    /// What one step of a drag moves: <see cref="Count"/> tabs from <see cref="Start"/>, landing
    /// with the first of them at <see cref="To"/>. A count of one is a single tab, more than one
    /// is a whole group.
    /// </summary>
    public sealed class DragMove
    {
        public DragMove() { }

        public DragMove(int start, int count, int to)
        {
            Start = start;
            Count = count;
            To = to;
        }

        public int Start { get; set; }
        public int Count { get; set; }
        public int To { get; set; }

        /// <summary>Whether this would leave the row as it is.</summary>
        public bool IsNothing => Count <= 0 || Start == To;
    }

    /// <summary>
    /// What a drag should move: one tab while it stays inside its own group, and the whole group
    /// once it passes beyond it.
    /// </summary>
    /// <remarks>
    /// A single tab cannot be dragged out of its group, because grouping is worked out again on
    /// the next refresh and there would be nowhere to record that it had left: within 250 ms it
    /// would be back. A whole group has no such problem. <see cref="Arrange"/> orders groups by
    /// where each one's first window sits, so moving a group's tabs together as a block is
    /// exactly the order Arrange gives back, and the move stands.
    ///
    /// A tab with no group of its own - the only window of its application, or one whose
    /// executable could not be read - is a block of one, so it travels alone.
    /// </remarks>
    public static DragMove PlanDrag(int target, int from, IList<string> arrangedGroupIds)
    {
        var nothing = new DragMove(from, 0, from);

        if (arrangedGroupIds == null || arrangedGroupIds.Count == 0) return nothing;
        if (from < 0 || from >= arrangedGroupIds.Count) return nothing;
        if (target < 0) target = 0;
        if (target > arrangedGroupIds.Count - 1) target = arrangedGroupIds.Count - 1;

        int start = StartOfRun(arrangedGroupIds, from);
        int end = EndOfRun(arrangedGroupIds, start);

        // Still over its own group: the tab moves by itself, as it always has.
        if (target >= start && target <= end) return new DragMove(from, 1, target);

        int count = end - start + 1;

        if (target < start)
        {
            // The block lands in front of the group the pointer is over. That group sits to the
            // left of the block, so taking the block out does not move it.
            return new DragMove(start, count, StartOfRun(arrangedGroupIds, target));
        }

        // The block lands behind the group the pointer is over. That group sits to the right of
        // the block, so taking the block out brings it count places nearer the front.
        int passedEnd = EndOfRun(arrangedGroupIds, StartOfRun(arrangedGroupIds, target));
        return new DragMove(start, count, passedEnd - count + 1);
    }

    /// <summary>
    /// The accent number of a group: the one its rule names, or one derived from the group's own
    /// name when the rule leaves it automatic.
    /// </summary>
    public static int AccentFor(string groupId, IList<AppGroup> rules, int paletteSize)
    {
        if (paletteSize <= 0) return -1;

        if (rules != null && !string.IsNullOrEmpty(groupId))
        {
            foreach (AppGroup rule in rules)
            {
                if (rule == null) continue;
                if (!string.Equals(rule.Name, groupId, StringComparison.OrdinalIgnoreCase)) continue;

                if (rule.Accent >= 0 && rule.Accent < paletteSize) return rule.Accent;
                break;
            }
        }

        return (int)(Hash(groupId) % (uint)paletteSize);
    }

    /// <summary>
    /// FNV-1a over the lower-cased characters.
    /// </summary>
    /// <remarks>
    /// Written out rather than calling <c>string.GetHashCode</c>, which is randomized per
    /// process on .NET Core and later. An application would be a different colour every time
    /// the bar started, and a different colour on each of the two target frameworks.
    /// </remarks>
    private static uint Hash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        uint hash = offset;
        if (value == null) return hash;

        foreach (char c in value)
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= prime;
        }

        return hash;
    }

    /// <summary>The first index of the run of equal group ids that <paramref name="index"/> is in.</summary>
    private static int StartOfRun(IList<string> groupIds, int index)
    {
        int start = index;
        while (start > 0 && SameGroup(groupIds, start - 1, start)) start--;
        return start;
    }

    /// <summary>The last index of the run of equal group ids that starts at <paramref name="start"/>.</summary>
    private static int EndOfRun(IList<string> groupIds, int start)
    {
        int end = start;
        while (end + 1 < groupIds.Count && SameGroup(groupIds, end, end + 1)) end++;
        return end;
    }

    /// <summary>
    /// Whether two neighbouring tabs are in one group. An empty id is its own group and matches
    /// nothing, not even another empty one.
    /// </summary>
    private static bool SameGroup(IList<string> groupIds, int left, int right)
    {
        string a = groupIds[left] ?? string.Empty;
        string b = groupIds[right] ?? string.Empty;

        if (a.Length == 0 || b.Length == 0) return false;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
