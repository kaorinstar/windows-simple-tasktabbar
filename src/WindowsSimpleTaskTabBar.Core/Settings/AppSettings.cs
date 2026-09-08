using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Localization;

namespace WindowsSimpleTaskTabBar.Core.Settings;

/// <summary>
/// How tall the bar is drawn.
/// </summary>
public enum BarHeightMode
{
    /// <summary>The original height, with room for a 16 pixel icon and readable text.</summary>
    Standard = 0,

    /// <summary>A shorter bar, for giving the height back to the windows below it.</summary>
    Compact = 1,
}

/// <summary>
/// Which colours the bar draws with.
/// </summary>
public enum ColourMode
{
    /// <summary>The palette Windows is set to, followed as it changes.</summary>
    FollowWindows = 0,

    /// <summary>The light palette, whatever Windows is set to.</summary>
    Light = 1,

    /// <summary>The dark palette, whatever Windows is set to.</summary>
    Dark = 2,
}

/// <summary>
/// Every user setting, in one place.
/// This type has no UI and no Windows API dependency, so it can be unit tested on any platform.
/// </summary>
/// <remarks>
/// Plain public properties with a parameterless constructor: the settings file is written by
/// serializing this type directly, so it must stay a simple data holder.
/// </remarks>
public class AppSettings
{
    /// <summary>
    /// The schema this instance was written with. Present from the first release so that a later
    /// version can tell an old file apart from a new one instead of guessing.
    /// </summary>
    public const int CurrentSchema = 4;

    /// <summary>How many accents a group can be marked with.</summary>
    public const int AccentCount = 8;

    /// <summary>The most groups the user may define, and the most executables one may hold.</summary>
    private const int MaxGroups = 64;
    private const int MaxExecutablesPerGroup = 256;

    public int Schema { get; set; } = CurrentSchema;

    public BarHeightMode BarHeight { get; set; } = BarHeightMode.Standard;

    /// <summary>
    /// Which palette the bar draws with, and whether it follows Windows.
    /// </summary>
    /// <remarks>
    /// Follows Windows by default, which is what the bar did before this setting existed, so an
    /// update changes nothing for someone who does not open the settings.
    /// </remarks>
    public ColourMode Colours { get; set; } = ColourMode.FollowWindows;

    /// <summary>
    /// Whether windows of one application are brought together in the row and marked as a group.
    /// </summary>
    /// <remarks>
    /// Off by default. Turning it on reorders the tabs the user is looking at, which is not
    /// something to do to somebody who has only updated the application.
    /// </remarks>
    public bool GroupByApplication { get; set; }

    /// <summary>
    /// Which language the interface is drawn in, as a language code, or
    /// <see cref="Languages.Automatic"/> to take it from Windows.
    /// </summary>
    /// <remarks>
    /// Automatic by default, and a file written before this setting existed reads as automatic
    /// too, so an update leaves the language where the user's Windows puts it.
    /// </remarks>
    public string Language { get; set; } = Languages.Automatic;

    /// <summary>
    /// The groups the user has defined by hand, which override the automatic one group per
    /// application.
    /// </summary>
    public List<AppGroup> Groups { get; set; } = new List<AppGroup>();

    /// <summary>Bar height in logical pixels, before any DPI scaling.</summary>
    public static int HeightInPixels(BarHeightMode mode)
    {
        return mode == BarHeightMode.Compact ? 24 : 34;
    }

    /// <summary>
    /// Returns a copy with every value brought back into range.
    /// A settings file can be edited by hand or written by a different version, so nothing read
    /// from it is trusted.
    /// </summary>
    public AppSettings Normalized()
    {
        return new AppSettings
        {
            Schema = CurrentSchema,
            BarHeight = IsKnown(BarHeight) ? BarHeight : BarHeightMode.Standard,
            Colours = IsKnown(Colours) ? Colours : ColourMode.FollowWindows,
            GroupByApplication = GroupByApplication,
            Language = Languages.Find(Language)?.Code ?? Languages.Automatic,
            Groups = NormalizedGroups(Groups),
        };
    }

    /// <summary>
    /// Brings this instance's own values into range, rather than returning a copy.
    /// </summary>
    /// <remarks>
    /// The settings object is shared: the bar reads it, the settings dialog edits it, and the
    /// store writes it. Normalizing only on the way out would leave the bar drawing a name or a
    /// grouping that the next start would not read back. Every property is listed here beside
    /// <see cref="Normalized"/>, so the two cannot fall out of step.
    /// </remarks>
    public void Normalize()
    {
        AppSettings tidy = Normalized();

        Schema = tidy.Schema;
        BarHeight = tidy.BarHeight;
        Colours = tidy.Colours;
        GroupByApplication = tidy.GroupByApplication;
        Language = tidy.Language;
        Groups = tidy.Groups;
    }

    private static bool IsKnown(BarHeightMode mode)
    {
        return mode == BarHeightMode.Standard || mode == BarHeightMode.Compact;
    }

    private static bool IsKnown(ColourMode mode)
    {
        return mode == ColourMode.FollowWindows
               || mode == ColourMode.Light
               || mode == ColourMode.Dark;
    }

    /// <summary>
    /// The groups with every value brought into range: every one named, no two sharing a name,
    /// and no executable in more than one of them.
    /// </summary>
    /// <remarks>
    /// <paramref name="groups"/> can be null even though the property has an initializer.
    /// <c>DataContractJsonSerializer</c> does not run the constructor, so a settings file
    /// written before this setting existed leaves the property unset rather than empty. Every
    /// path into the application goes through here, so nothing downstream has to test for it.
    ///
    /// A group's name identifies it, so two groups may not share one: <c>GroupIdFor</c> would
    /// answer the same name for two sets of applications and draw them as one group.
    /// </remarks>
    private static List<AppGroup> NormalizedGroups(List<AppGroup> groups)
    {
        var result = new List<AppGroup>();
        if (groups == null) return result;

        var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AppGroup group in groups)
        {
            if (group == null) continue;
            if (result.Count >= MaxGroups) break;

            var executables = new List<string>();
            foreach (string executable in group.Executables ?? new List<string>())
            {
                if (executables.Count >= MaxExecutablesPerGroup) break;

                // The same normalization the bar applies to what it reads from a window, so the
                // two are comparable however the name was typed into the settings file.
                string key = TabGrouping.KeyFor(executable);
                if (key.Length == 0) continue;

                // First group named wins. Without this one executable could sit in two groups,
                // and which one it landed in would depend on the order they happened to be read.
                if (!takenExecutables.Add(key)) continue;

                executables.Add(key);
            }

            // A group with nothing in it is kept. It shows nothing on the bar, but it is what
            // the user is looking at just after creating one, and a group that disappeared
            // between being made and being filled would be hard to make sense of.
            string name = UniqueName(group.Name, executables, takenNames);
            takenNames.Add(name);

            result.Add(new AppGroup
            {
                Name = name,
                Accent = group.Accent >= 0 && group.Accent < AccentCount ? group.Accent : -1,
                Executables = executables,
            });
        }

        return result;
    }

    /// <summary>
    /// A name for a group that no other group has: the one it was given, or the first
    /// executable without its extension, with a number added if that is taken too.
    /// </summary>
    private static string UniqueName(string name, List<string> executables, HashSet<string> taken)
    {
        string candidate = (name ?? string.Empty).Trim();
        if (candidate.Length == 0 && executables.Count > 0)
        {
            string first = executables[0];
            int dot = first.LastIndexOf('.');
            candidate = dot > 0 ? first.Substring(0, dot) : first;
        }

        if (candidate.Length == 0) candidate = "Group";

        if (!taken.Contains(candidate)) return candidate;

        for (int n = 2; ; n++)
        {
            string numbered = candidate + " (" + n + ")";
            if (!taken.Contains(numbered)) return numbered;
        }
    }
}
