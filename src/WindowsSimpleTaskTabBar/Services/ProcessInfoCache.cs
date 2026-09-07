using WindowsSimpleTaskTabBar.Core.Grouping;

namespace WindowsSimpleTaskTabBar.Services;

/// <summary>
/// Remembers which executable owns a window, so the 250 ms refresh does not ask Windows again.
/// </summary>
/// <remarks>
/// An instance rather than static state, so it has a named owner in <c>MainForm</c>. It holds
/// strings only, so there is nothing here to release.
///
/// Nothing expires. A window cannot change the process that owns it, so the one reason to
/// forget an entry is that the window is gone, which <see cref="Forget"/> does from the same
/// live set the icon cache is pruned against. A handle Windows later reuses was pruned when its
/// first window closed, so it is looked up again rather than answered from the old entry.
///
/// A failed lookup is remembered as an empty string. Without that, a window this application
/// may not query would be asked four times a second for as long as it is open.
/// </remarks>
internal sealed class ProcessInfoCache
{
    // The path and the name it was taken from, in one entry. Two dictionaries would have to be
    // kept to the same set of keys, and reading one for a handle only the other knew about
    // would throw.
    private sealed class Entry
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
    }

    private readonly Dictionary<IntPtr, Entry> _entries = new();

    /// <summary>Full path of the owning executable, or an empty string when it is not known.</summary>
    public string Path(IntPtr hwnd)
    {
        return Lookup(hwnd).Path;
    }

    /// <summary>
    /// The executable's file name in lower case, for example <c>chrome.exe</c>, or an empty
    /// string when it is not known.
    /// </summary>
    public string Name(IntPtr hwnd)
    {
        return Lookup(hwnd).Name;
    }

    private Entry Lookup(IntPtr hwnd)
    {
        if (_entries.TryGetValue(hwnd, out Entry entry)) return entry;

        string path = WindowService.GetExecutablePath(hwnd);
        entry = new Entry { Path = path, Name = TabGrouping.KeyFor(path) };
        _entries[hwnd] = entry;
        return entry;
    }

    /// <summary>Drops everything remembered about windows that are no longer open.</summary>
    public void Forget(ICollection<IntPtr> live)
    {
        foreach (IntPtr key in _entries.Keys.ToList())
        {
            if (!live.Contains(key)) _entries.Remove(key);
        }
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
