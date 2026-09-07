using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Update;

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
    public const int CurrentSchema = 3;

    /// <summary>How many accents a group can be marked with.</summary>
    public const int AccentCount = 8;

    /// <summary>The most groups the user may define, and the most executables one may hold.</summary>
    private const int MaxGroups = 64;
    private const int MaxExecutablesPerGroup = 256;

    public int Schema { get; set; } = CurrentSchema;

    public BarHeightMode BarHeight { get; set; } = BarHeightMode.Standard;

    /// <summary>
    /// Whether windows of one application are brought together in the row and marked as a group.
    /// </summary>
    /// <remarks>
    /// Off by default. Turning it on reorders the tabs the user is looking at, which is not
    /// something to do to somebody who has only updated the application.
    /// </remarks>
    public bool GroupByApplication { get; set; }

    /// <summary>
    /// The groups the user has defined by hand, which override the automatic one group per
    /// application.
    /// </summary>
    public List<AppGroup> Groups { get; set; } = new List<AppGroup>();

    /// <summary>
    /// Whether the bar looks for a newer release when it starts.
    /// </summary>
    /// <remarks>
    /// Nullable, and on when it has not been chosen. <c>DataContractJsonSerializer</c> does not
    /// run the constructor, so a settings file written before this setting existed leaves the
    /// property unset: a plain <c>bool</c> would arrive as false and quietly turn the check off
    /// for exactly the people who already use the application, while a new installation got it.
    /// Null means "not chosen" and <see cref="Normalized"/> turns it on; a <c>false</c> written
    /// into the file by hand is still honoured.
    /// </remarks>
    public bool? CheckForUpdates { get; set; } = true;

    /// <summary>
    /// When the last check reached GitHub, in UTC. Empty until one has.
    /// </summary>
    /// <remarks>
    /// A string rather than a <c>DateTime</c>; <see cref="UpdateCheckSchedule"/> says why.
    /// </remarks>
    public string LastUpdateCheckUtc { get; set; } = string.Empty;

    /// <summary>
    /// The newest release the user has already been told about.
    /// </summary>
    /// <remarks>
    /// Without it the same notice would appear at every logon until the user updated, which is
    /// how a reminder turns into a nuisance. Each release is announced once.
    /// </remarks>
    public string LastNoticedRelease { get; set; } = string.Empty;

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
            GroupByApplication = GroupByApplication,
            Groups = NormalizedGroups(Groups),
            CheckForUpdates = CheckForUpdates ?? true,
            LastUpdateCheckUtc = NormalizedStamp(LastUpdateCheckUtc),
            LastNoticedRelease = NormalizedTag(LastNoticedRelease),
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
        GroupByApplication = tidy.GroupByApplication;
        Groups = tidy.Groups;
        CheckForUpdates = tidy.CheckForUpdates;
        LastUpdateCheckUtc = tidy.LastUpdateCheckUtc;
        LastNoticedRelease = tidy.LastNoticedRelease;
    }

    /// <summary>
    /// The time of the last update check, or empty when the file does not hold one this code can
    /// read. A time it cannot read means the next check runs, which is the harmless direction.
    /// </summary>
    private static string NormalizedStamp(string text)
    {
        DateTime parsed = UpdateCheckSchedule.ParseStamp(text);
        return parsed == DateTime.MinValue ? string.Empty : UpdateCheckSchedule.Stamp(parsed);
    }

    /// <summary>
    /// The last announced release, or empty when the file does not hold a version number. This is
    /// also what stops a hand-edited file from putting arbitrary text into a notification.
    /// </summary>
    private static string NormalizedTag(string text)
    {
        return ReleaseVersion.Parse(text) == null ? string.Empty : text.Trim();
    }

    private static bool IsKnown(BarHeightMode mode)
    {
        return mode == BarHeightMode.Standard || mode == BarHeightMode.Compact;
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
