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
    private readonly Dictionary<IntPtr, string> _paths = new();
    private readonly Dictionary<IntPtr, string> _names = new();

    /// <summary>Full path of the owning executable, or an empty string when it is not known.</summary>
    public string Path(IntPtr hwnd)
    {
        if (_paths.TryGetValue(hwnd, out string path)) return path;

        path = WindowService.GetExecutablePath(hwnd);
        _paths[hwnd] = path;
        _names[hwnd] = TabGrouping.KeyFor(path);
        return path;
    }

    /// <summary>
    /// The executable's file name in lower case, for example <c>chrome.exe</c>, or an empty
    /// string when it is not known.
    /// </summary>
    public string Name(IntPtr hwnd)
    {
        if (_names.TryGetValue(hwnd, out string name)) return name;

        Path(hwnd);
        return _names[hwnd];
    }

    /// <summary>Drops everything remembered about windows that are no longer open.</summary>
    public void Forget(ICollection<IntPtr> live)
    {
        foreach (IntPtr key in _paths.Keys.ToList())
        {
            if (live.Contains(key)) continue;

            _paths.Remove(key);
            _names.Remove(key);
        }
    }

    public void Clear()
    {
        _paths.Clear();
        _names.Clear();
    }
}
