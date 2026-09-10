using WindowsSimpleTaskTabBar.Core.Grouping;

namespace WindowsSimpleTaskTabBar.Core.Filtering;

/// <summary>
/// Decides which windows the bar leaves out.
/// </summary>
/// <remarks>
/// Two separate questions, answered by two methods, because they are asked at different points
/// and cost different amounts. The window class is on the window itself and is read while the
/// windows are being enumerated; the executable behind a window costs a process query, so it is
/// asked afterwards and only for the windows that got that far. See <c>docs/architecture.md</c>.
///
/// Neither question needs the Windows API or a screen, so both stand here and are unit tested.
/// </remarks>
public static class WindowExclusion
{
    /// <summary>
    /// The window classes the bar never shows, whatever the user asks for.
    /// </summary>
    /// <remarks>
    /// These are the shell's own windows: the taskbar, the desktop, the pop-ups Windows draws
    /// its own interface in. A bar that listed them would be listing itself and the desktop
    /// behind it, so this list is built in and the user cannot shorten it. What a user excludes
    /// is an application, which is a separate list.
    /// </remarks>
    private static readonly string[] ShellClasses =
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Button",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "SysShadow",
        "Windows.UI.Core.CoreWindow",
        "Xaml_WindowedPopupClass",
    };

    /// <summary>
    /// Whether a window of this class is one of the shell's own, and so never shown.
    /// </summary>
    public static bool IsShellWindow(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;

        foreach (string excluded in ShellClasses)
        {
            if (string.Equals(className, excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the user has asked for this executable's windows to be kept off the bar.
    /// </summary>
    /// <param name="executable">
    /// The executable behind the window, as a file name or as a full path. A window whose
    /// process could not be read gives an empty string here and is never excluded: a bar that
    /// dropped every window it could not identify would lose windows the user never named.
    /// </param>
    /// <param name="excluded">The user's list, as <c>AppSettings.ExcludedApplications</c> holds it.</param>
    public static bool IsExcludedApplication(string executable, IList<string> excluded)
    {
        if (excluded == null || excluded.Count == 0) return false;

        // Both sides through the same normalization, so a path from a window and a name typed
        // into the settings file compare equal.
        string key = TabGrouping.KeyFor(executable);
        if (key.Length == 0) return false;

        foreach (string name in excluded)
        {
            if (string.Equals(TabGrouping.KeyFor(name), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
