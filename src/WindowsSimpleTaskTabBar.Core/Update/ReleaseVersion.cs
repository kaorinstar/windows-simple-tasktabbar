using System.Globalization;

namespace WindowsSimpleTaskTabBar.Core.Update;

/// <summary>
/// Reads a version out of a release tag or an assembly version string, and answers whether one
/// release is newer than another.
/// This type has no UI and no Windows API dependency, so it can be unit tested on any platform.
/// </summary>
/// <remarks>
/// One parser serves both sides of the comparison, because the two are written differently and
/// neither is under this code's control. A release tag is <c>v0.3.0</c>. The running version comes
/// from <c>AssemblyInformationalVersion</c>, which <c>release.yml</c> stamps as <c>v0.3.0</c> on a
/// published build and which is the plain <c>0.3.0</c> from <c>Directory.Build.props</c> on every
/// other build. Accepting only one of those shapes would compare a released build against a
/// branch build and get the wrong answer.
/// </remarks>
public static class ReleaseVersion
{
    /// <summary>The longest tag worth looking at, so a broken answer cannot reach the screen.</summary>
    private const int MaxLength = 64;

    /// <summary>
    /// The version a tag or a version string names, or null when it names none.
    /// </summary>
    /// <remarks>
    /// A pre-release or build suffix is refused rather than trimmed off. <c>/releases/latest</c>
    /// already skips a pre-release, so anything carrying one is an answer this code did not
    /// expect, and guessing at it is how a beta gets offered to everybody.
    /// </remarks>
    public static Version Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string trimmed = text.Trim();
        if (trimmed.Length > MaxLength) return null;

        if (trimmed[0] == 'v' || trimmed[0] == 'V') trimmed = trimmed.Substring(1);
        if (trimmed.Length == 0) return null;

        // "1.2.3-beta", "1.2.3+build". Version.TryParse refuses both, but saying so here keeps
        // the reason in one place.
        if (trimmed.IndexOf('-') >= 0 || trimmed.IndexOf('+') >= 0) return null;

        string[] parts = trimmed.Split('.');
        if (parts.Length < 2 || parts.Length > 4) return null;

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                return null;

            numbers[i] = value;
        }

        // Always four parts. An unspecified part of a System.Version is -1 rather than 0, so
        // 1.2 would compare as older than 1.2.0 and a tag written with two parts would never be
        // offered as an update.
        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>
    /// Whether <paramref name="latestTag"/> names a release this build does not already have.
    /// </summary>
    /// <remarks>
    /// Anything unreadable on either side answers false. Everything on this path is silent, so a
    /// doubt has to mean no notice rather than a notice the user cannot act on.
    /// </remarks>
    public static bool IsNewer(string latestTag, string runningVersion)
    {
        Version latest = Parse(latestTag);
        Version running = Parse(runningVersion);

        return latest != null && running != null && latest > running;
    }

    /// <summary>
    /// Whether two tags name the same release, however each of them was written.
    /// </summary>
    /// <remarks>
    /// Used to tell whether the user has already been shown this release. Comparing the strings
    /// would announce v0.4.0 a second time to anyone whose settings file happened to hold 0.4.0.
    /// </remarks>
    public static bool IsSameRelease(string tagA, string tagB)
    {
        Version a = Parse(tagA);
        Version b = Parse(tagB);

        return a != null && b != null && a == b;
    }
}
