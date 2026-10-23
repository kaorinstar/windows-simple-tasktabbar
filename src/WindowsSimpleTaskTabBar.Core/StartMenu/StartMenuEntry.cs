namespace WindowsSimpleTaskTabBar.Core.StartMenu;

/// <summary>
/// Where the Start menu shortcut for the application lives, so that the tray menu and the
/// installer agree on it.
/// </summary>
/// <remarks>
/// The shortcut sits directly in the user's Programs folder, which is where the Start menu finds
/// an application by name. Releases before the one that added the tray entry put it in a folder
/// of the same name instead, and an installed copy of one of those still has it there, so both
/// places count as "already in the Start menu". Without that, the tray entry would add a second
/// shortcut beside the installer's.
/// </remarks>
public static class StartMenuEntry
{
    public const string Extension = ".lnk";

    /// <summary>The file the tray entry creates and removes.</summary>
    public static string ShortcutPath(string programsDirectory, string appName)
    {
        return Path.Combine(programsDirectory, appName + Extension);
    }

    /// <summary>The file an installer from before the tray entry created, inside its own folder.</summary>
    public static string LegacyShortcutPath(string programsDirectory, string appName)
    {
        return Path.Combine(programsDirectory, appName, appName + Extension);
    }

    /// <summary>Every place a shortcut for the application can already be, current one first.</summary>
    public static IReadOnlyList<string> Locations(string programsDirectory, string appName)
    {
        return new[]
        {
            ShortcutPath(programsDirectory, appName),
            LegacyShortcutPath(programsDirectory, appName),
        };
    }
}
