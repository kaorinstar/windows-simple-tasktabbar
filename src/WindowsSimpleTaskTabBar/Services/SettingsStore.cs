using System.IO;
using System.Runtime.Serialization.Json;
using WindowsSimpleTaskTabBar.Core.Settings;

namespace WindowsSimpleTaskTabBar.Services;

/// <summary>
/// Reads and writes the settings file.
/// </summary>
/// <remarks>
/// Serialization uses <see cref="DataContractJsonSerializer"/>, which ships with both target
/// frameworks. A NuGet package is deliberately avoided: the application is distributed as a
/// single executable, with Core compiled in rather than referenced as a DLL.
/// </remarks>
internal static class SettingsStore
{
    private const string FolderName = "WindowsSimpleTaskTabBar";
    private const string FileName = "settings.json";

    public static string FilePath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, FolderName, FileName);
        }
    }

    /// <summary>
    /// Loads the settings, falling back to defaults.
    /// </summary>
    /// <remarks>
    /// A missing or damaged file must never stop the bar from starting, and must never raise a
    /// dialog: the user did not ask for one, and the bar is usually started at logon where
    /// nobody is watching.
    /// </remarks>
    public static AppSettings Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return new AppSettings();

            using FileStream stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(AppSettings));
            var loaded = serializer.ReadObject(stream) as AppSettings;

            return loaded == null ? new AppSettings() : loaded.Normalized();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes the settings. Returns false when the file could not be written, which the caller
    /// is free to ignore: failing to save a preference is not worth interrupting the user for.
    /// </summary>
    public static bool Save(AppSettings settings)
    {
        if (settings == null) return false;

        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Written to a temporary file first, so an interrupted write cannot leave a
            // half-finished file that the next start would have to recover from.
            string temp = path + ".tmp";
            using (FileStream stream = File.Create(temp))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings.Normalized());
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
