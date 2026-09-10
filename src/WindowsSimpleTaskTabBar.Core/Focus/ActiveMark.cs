namespace WindowsSimpleTaskTabBar.Core.Focus;

/// <summary>What the bar does with the mark that says which window is in front.</summary>
public enum MarkChoice
{
    /// <summary>Mark the window that is in the foreground now.</summary>
    TakeForeground,

    /// <summary>Leave the mark where it is.</summary>
    KeepMarked,

    /// <summary>Mark no tab at all.</summary>
    MarkNothing,
}

/// <summary>
/// Which tab carries the mark for the window in front.
/// </summary>
/// <remarks>
/// The foreground window is not the whole answer. The bar activates itself when it is clicked -
/// it does not set <c>WS_EX_NOACTIVATE</c>, because a process already in the foreground is
/// exempt from the restriction on activating windows - so touching the bar at all takes the
/// foreground away from the window the user was in. A click hands it back on release, but a
/// drag ends with no window to activate, and the row would sit unmarked until the user went
/// somewhere else.
///
/// So a window of this application's own never takes the mark, and the window that held it
/// keeps it, which is what the Windows taskbar does with its own highlight while it is being
/// used. A window the bar does not list cannot hold the mark either, so a handle Windows has
/// since given to something else cannot inherit one.
///
/// Booleans rather than window handles, so the rule is decided here and can be tested on any
/// platform, while asking Windows which window is which stays in the UI layer.
/// </remarks>
public static class ActiveMark
{
    /// <param name="foregroundIsOwn">
    /// Whether the foreground window belongs to this application: the bar, its settings dialog
    /// or one of its menus.
    /// </param>
    /// <param name="foregroundIsListed">Whether the foreground window has a tab on the bar.</param>
    /// <param name="markedIsListed">Whether the window marked until now still has one.</param>
    public static MarkChoice Choose(bool foregroundIsOwn, bool foregroundIsListed,
                                    bool markedIsListed)
    {
        // Somebody else's window is in front. It is the answer when the bar lists it, and when
        // it does not - the desktop, a window the user excluded - the honest answer is that no
        // tab is the one in front.
        if (!foregroundIsOwn) return foregroundIsListed ? MarkChoice.TakeForeground
                                                        : MarkChoice.MarkNothing;

        // This application is in front, which says nothing about which window the user was in.
        // The mark stays where it was, for as long as that window is still on the bar.
        return markedIsListed ? MarkChoice.KeepMarked : MarkChoice.MarkNothing;
    }
}
