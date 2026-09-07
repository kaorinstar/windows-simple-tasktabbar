namespace WindowsSimpleTaskTabBar.Core.Settings;

/// <summary>
/// One group the user has defined by hand: a name, an accent, and the executables that belong
/// to it. It overrides the automatic one-group-per-application.
/// </summary>
/// <remarks>
/// A list of these rather than a dictionary keyed by executable, because
/// <c>DataContractJsonSerializer</c> writes a dictionary as an array of key and value pairs,
/// which is not something anyone would want to find in a file they opened to edit by hand.
///
/// Plain public properties with a parameterless constructor, for the same reason as
/// <see cref="AppSettings"/>: this type is serialized directly.
/// </remarks>
public class AppGroup
{
    /// <summary>What the group is called. Also what identifies it, so no two may share one.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The accent to mark the group with, or -1 to have one picked from the name and from the
    /// accents the other groups already hold. See <c>TabGrouping.AccentFor</c>.
    /// </summary>
    public int Accent { get; set; } = -1;

    /// <summary>Executable file names in lower case, for example <c>chrome.exe</c>.</summary>
    public List<string> Executables { get; set; } = new List<string>();
}
