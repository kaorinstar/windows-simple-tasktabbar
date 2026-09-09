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

        /// <summary>
        /// Whether this carries the block past a group rather than moving it inside its own.
        /// </summary>
        public bool CrossesGroups { get; set; }

        /// <summary>
        /// How far the row shifts, in slots. One is a swap with the neighbour; more than that is
        /// a jump over a whole group, and the tabs it passes move that far in one step.
        /// </summary>
        public int Distance => Count <= 0 ? 0 : (To > Start ? To - Start : Start - To);

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
    /// <param name="movingGroup">
    /// Whether this drag has already carried a whole group somewhere. Once it has, it moves that
    /// group and nothing else. A group move leaves the pointer over the group it just carried
    /// across, and reading that as a move inside the group would pull the held tab to whichever
    /// slot the pointer had reached - quietly reordering tabs the user never took hold of. So
    /// the drag becomes a group drag and stays one; reordering inside a group is what a drag
    /// that never leaves it is for.
    /// </param>
    public static DragMove PlanDrag(int target, int from, IList<string> arrangedGroupIds,
        bool movingGroup)
    {
        var nothing = new DragMove(from, 0, from);

        if (arrangedGroupIds == null || arrangedGroupIds.Count == 0) return nothing;
        if (from < 0 || from >= arrangedGroupIds.Count) return nothing;
        if (target < 0) target = 0;
        if (target > arrangedGroupIds.Count - 1) target = arrangedGroupIds.Count - 1;

        int start = StartOfRun(arrangedGroupIds, from);
        int end = EndOfRun(arrangedGroupIds, start);

        // Still over its own group: the tab moves by itself, unless this drag is already carrying
        // the group, in which case the group's own order is left as the user had it.
        if (target >= start && target <= end)
            return movingGroup ? nothing : new DragMove(from, 1, target);

        int count = end - start + 1;

        if (target < start)
        {
            // The block lands in front of the group the pointer is over. That group sits to the
            // left of the block, so taking the block out does not move it.
            return new DragMove(start, count, StartOfRun(arrangedGroupIds, target))
            {
                CrossesGroups = true,
            };
        }

        // The block lands behind the group the pointer is over. That group sits to the right of
        // the block, so taking the block out brings it count places nearer the front.
        int passedEnd = EndOfRun(arrangedGroupIds, StartOfRun(arrangedGroupIds, target));
        return new DragMove(start, count, passedEnd - count + 1) { CrossesGroups = true };
    }

    /// <summary>
    /// The accent number of a group: the one its rule names, or one derived from the group's own
    /// name when the rule leaves it automatic.
    /// </summary>
    /// <remarks>
    /// A name on its own cannot avoid a collision. Two groups with different names agree modulo
    /// eight often enough to be met rather than to be unlucky: with three groups the chance is
    /// about one in three, and past eight groups a repeat is certain. So the accents the user
    /// chose by hand are taken first, and then the groups left automatic are walked in the order
    /// they sit in the settings, each keeping the accent its name gives when that one is still
    /// free and taking the next free one when it is not.
    ///
    /// The order the settings hold them in is the one thing available that does not change by
    /// itself, which is what makes the answer the same on every run. Adding a group can still
    /// move the accent of a group listed after it. Choosing the accent by hand is the way to hold
    /// one still.
    ///
    /// Only the groups the user defined take part. A group that is one application - a
    /// <paramref name="groupId"/> no rule names - keeps the accent its name gives, so it can
    /// still meet a defined group on the same colour. Making those take part would mean ordering
    /// them by which windows happen to be open, and a colour would then change as windows were
    /// opened and closed.
    /// </remarks>
    public static int AccentFor(string groupId, IList<AppGroup> rules, int paletteSize)
    {
        if (paletteSize <= 0) return -1;
        if (rules == null || string.IsNullOrEmpty(groupId)) return Hashed(groupId, paletteSize);

        var taken = new bool[paletteSize];
        bool defined = false;

        // Chosen accents are settled before any name is hashed, so an automatic group avoids one
        // the user asked for whichever order the two are listed in.
        foreach (AppGroup rule in rules)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Name)) continue;

            if (string.Equals(rule.Name, groupId, StringComparison.OrdinalIgnoreCase)) defined = true;
            if (rule.Accent >= 0 && rule.Accent < paletteSize) taken[rule.Accent] = true;
        }

        if (!defined) return Hashed(groupId, paletteSize);

        foreach (AppGroup rule in rules)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Name)) continue;

            bool wanted = string.Equals(rule.Name, groupId, StringComparison.OrdinalIgnoreCase);

            if (rule.Accent >= 0 && rule.Accent < paletteSize)
            {
                if (wanted) return rule.Accent;
                continue;
            }

            int accent = FirstFree(Hashed(rule.Name, paletteSize), taken);
            if (accent >= 0) taken[accent] = true;
            else accent = Hashed(rule.Name, paletteSize);   // every accent is spoken for

            if (wanted) return accent;
        }

        return Hashed(groupId, paletteSize);
    }

    /// <summary>
    /// The accent of every tab of an arranged row, and -1 for the tabs of a group left unmarked.
    /// </summary>
    /// <remarks>
    /// <see cref="AccentFor"/> keeps the groups the user defined apart from one another, which is
    /// as far as a name can go. The groups that are one application are not in the settings at
    /// all, so nothing there can hold them apart, and with eight accents a row of half a dozen
    /// applications repeats one often.
    ///
    /// Where that repeat does harm is between neighbours. The band is carried across the gap
    /// inside a group, so two groups side by side in one colour read as a single group, which is
    /// the one thing the band is there to say. Two groups in the same colour with something
    /// between them are only two groups in the same colour.
    ///
    /// So the row is walked from the front, and where a group's accent matches the group before
    /// it, one of the two moves on. The one that moves is the one whose accent was derived: an
    /// accent the user chose stays where they put it, and the group beside it gives way instead.
    /// When both were chosen by hand, both are left as they are. A group of one window is not
    /// marked and breaks the band, so the group after it has nothing to differ from.
    ///
    /// This is the row's own order, so a group can change colour when it is dragged to a new
    /// neighbour or when a window opens beside it. That is the price of the guarantee, and it is
    /// paid on the tabs the user is looking at rather than across the whole bar.
    /// </remarks>
    public static int[] AccentsFor(IList<string> arrangedGroupIds, IList<AppGroup> rules,
        int paletteSize)
    {
        if (arrangedGroupIds == null || arrangedGroupIds.Count == 0) return new int[0];

        bool[] marks = Marks(arrangedGroupIds);

        // One entry per group of the row, in the order the row is drawn. An unmarked group is
        // carried along with an accent of -1, so that it still separates the two beside it.
        var starts = new List<int>();
        var ends = new List<int>();
        var accent = new List<int>();
        var chosen = new List<bool>();

        int at = 0;
        while (at < arrangedGroupIds.Count)
        {
            int end = EndOfRun(arrangedGroupIds, at);

            starts.Add(at);
            ends.Add(end);
            accent.Add(marks[at] ? AccentFor(arrangedGroupIds[at], rules, paletteSize) : -1);
            chosen.Add(marks[at] && IsChosen(arrangedGroupIds[at], rules, paletteSize));

            at = end + 1;
        }

        // Two accents are needed for one to give way, and three for the one that gives way to
        // clear both of its neighbours.
        if (paletteSize > 2)
        {
            for (int i = 1; i < accent.Count; i++)
            {
                if (accent[i] < 0 || accent[i] != accent[i - 1]) continue;

                if (!chosen[i])
                {
                    accent[i] = NextApartFrom(accent[i], accent[i - 1], -1, paletteSize);
                }
                else if (!chosen[i - 1])
                {
                    // The user chose this one, so the group in front of it moves instead - clear
                    // of this accent and of whatever sits on its own other side.
                    accent[i - 1] = NextApartFrom(
                        accent[i - 1], accent[i], i >= 2 ? accent[i - 2] : -1, paletteSize);
                }

                // Neither branch taken means both accents were chosen by hand, which is what the
                // user asked for and is left as it is.
            }
        }

        var accents = new int[arrangedGroupIds.Count];
        for (int run = 0; run < starts.Count; run++)
        {
            for (int i = starts[run]; i <= ends[run]; i++) accents[i] = accent[run];
        }

        return accents;
    }

    /// <summary>
    /// The first accent after <paramref name="start"/> that is neither of the two given. Either
    /// may be -1, which no accent matches.
    /// </summary>
    private static int NextApartFrom(int start, int first, int second, int paletteSize)
    {
        for (int step = 1; step <= paletteSize; step++)
        {
            int candidate = (start + step) % paletteSize;
            if (candidate != first && candidate != second) return candidate;
        }

        return start;
    }

    /// <summary>Whether a rule names this group and gives it an accent the user picked.</summary>
    private static bool IsChosen(string groupId, IList<AppGroup> rules, int paletteSize)
    {
        if (rules == null || string.IsNullOrEmpty(groupId)) return false;

        foreach (AppGroup rule in rules)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Name)) continue;
            if (!string.Equals(rule.Name, groupId, StringComparison.OrdinalIgnoreCase)) continue;

            return rule.Accent >= 0 && rule.Accent < paletteSize;
        }

        return false;
    }

    /// <summary>The accent a name gives on its own, before any collision is considered.</summary>
    private static int Hashed(string name, int paletteSize)
    {
        return (int)(Hash(name) % (uint)paletteSize);
    }

    /// <summary>
    /// The first accent not yet taken, starting at <paramref name="start"/> and wrapping round,
    /// or -1 when every one of them is taken.
    /// </summary>
    private static int FirstFree(int start, bool[] taken)
    {
        for (int step = 0; step < taken.Length; step++)
        {
            int accent = (start + step) % taken.Length;
            if (!taken[accent]) return accent;
        }

        return -1;
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
