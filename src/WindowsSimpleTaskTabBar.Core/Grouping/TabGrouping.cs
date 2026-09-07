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
    /// Holds a dragged tab inside the run of tabs that share its group.
    /// </summary>
    /// <remarks>
    /// Grouping is worked out from the settings on every refresh, so there is nowhere to record
    /// a tab that was dragged out of its group: the next refresh, at most 250 ms later, would
    /// put it back. A bar that undoes what the user just did is worse than one that would not
    /// let them do it. Applications are brought together from the settings dialog instead.
    /// </remarks>
    public static int ClampToGroup(int target, int from, IList<string> arrangedGroupIds)
    {
        if (arrangedGroupIds == null || arrangedGroupIds.Count == 0) return target;
        if (from < 0 || from >= arrangedGroupIds.Count) return target;

        int start = from;
        while (start > 0 && SameGroup(arrangedGroupIds, start - 1, start)) start--;

        int end = EndOfRun(arrangedGroupIds, start);

        if (target < start) return start;
        if (target > end) return end;
        return target;
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
