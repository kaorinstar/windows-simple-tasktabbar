using System.Runtime.InteropServices;
using WindowsSimpleTaskTabBar.Core.StartMenu;
using WindowsSimpleTaskTabBar.Interop;

namespace WindowsSimpleTaskTabBar.Services;

/// <summary>
/// Adds the application to the Start menu, or takes it out again, for a copy that was run from a
/// ZIP or as a bare executable. An installed copy already has the entry.
/// </summary>
/// <remarks>
/// Only ever the current user's Programs folder, so nothing here needs administrator rights.
/// Nothing runs until the user chooses the tray entry.
/// </remarks>
internal static class StartMenuShortcut
{
    private static string ProgramsDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>True when a shortcut is already in either place the application is put.</summary>
    public static bool Exists(string appName)
    {
        foreach (string path in StartMenuEntry.Locations(ProgramsDirectory, appName))
        {
            if (File.Exists(path)) return true;
        }
        return false;
    }

    /// <summary>Writes a shortcut that starts <paramref name="exePath"/>.</summary>
    public static void Create(string appName, string exePath)
    {
        string path = StartMenuEntry.ShortcutPath(ProgramsDirectory, appName);
        Directory.CreateDirectory(ProgramsDirectory);

        var link = new NativeMethods.ShellLink();
        try
        {
            var shellLink = (NativeMethods.IShellLinkW)link;
            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
            shellLink.SetIconLocation(exePath, 0);
            ((NativeMethods.IPersistFile)link).Save(path, false);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// Deletes the shortcut wherever it is, and the folder an older installer made for it once
    /// that folder is empty. A folder with anything else in it is left alone.
    /// </summary>
    public static void Remove(string appName)
    {
        foreach (string path in StartMenuEntry.Locations(ProgramsDirectory, appName))
        {
            if (File.Exists(path)) File.Delete(path);
        }

        string legacyFolder = Path.Combine(ProgramsDirectory, appName);
        if (Directory.Exists(legacyFolder) && Directory.GetFileSystemEntries(legacyFolder).Length == 0)
        {
            Directory.Delete(legacyFolder);
        }
    }
}
