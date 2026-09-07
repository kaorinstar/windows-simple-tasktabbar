namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The accents a tab group can be marked with: a colour for each theme, and a name for the one
/// place a user has to choose between them.
/// </summary>
/// <remarks>
/// Core works in accent numbers so that it stays free of System.Drawing, and the numbers are
/// turned into colours here. Both the bar and the settings dialog need them, so they sit in a
/// class of their own rather than in either.
///
/// What the eight have to be is colours a person can tell apart at three pixels tall, including
/// on the lighter fill of the active tab. The order carries no meaning: a group left automatic
/// is given one of them from its name.
/// </remarks>
internal static class AccentPalette
{
    private static readonly int[] LightRgb =
    {
        0x1A73E8, 0xD93025, 0xF29900, 0x188038,
        0xD01884, 0x8430CE, 0x007B83, 0x5F6368,
    };

    private static readonly int[] DarkRgb =
    {
        0x8AB4F8, 0xF28B82, 0xFDD663, 0x81C995,
        0xFF8BCB, 0xC58AF9, 0x78D9EC, 0xBDC1C6,
    };

    /// <summary>
    /// What each accent is called, in the same order as the colours.
    /// </summary>
    /// <remarks>
    /// The name of the colour rather than its number. "Colour 5" says nothing about what the user
    /// is about to see on the bar, and a group they had already coloured could only be recognised
    /// by counting down the list.
    ///
    /// The name describes the light shade; the dark one is the same hue lightened to read against
    /// a dark tab, so a single name covers both.
    /// </remarks>
    private static readonly string[] AccentNames =
    {
        "Blue", "Red", "Yellow", "Green",
        "Pink", "Purple", "Teal", "Grey",
    };

    /// <summary>How many accents there are. The same number Core works in.</summary>
    public static int Count => AccentNames.Length;

    /// <summary>What an accent is called, or an empty string when the number is not one.</summary>
    public static string Name(int accent)
    {
        return accent >= 0 && accent < AccentNames.Length ? AccentNames[accent] : string.Empty;
    }

    /// <summary>
    /// The colour of an accent, in the shade that reads against the tabs of the given theme.
    /// </summary>
    public static Color Colour(int accent, bool light)
    {
        int[] rgb = light ? LightRgb : DarkRgb;
        if (accent < 0 || accent >= rgb.Length) return Color.Transparent;

        return Color.FromArgb(255, Color.FromArgb(rgb[accent]));
    }

    /// <summary>
    /// Writes the whole palette into an array the caller keeps, for a theme.
    /// </summary>
    /// <remarks>
    /// The array is sized from <c>AppSettings.AccentCount</c> while the colours are counted here,
    /// so the shorter of the two decides. The two are equal; the clamp is what keeps them from
    /// having to be.
    /// </remarks>
    public static void Fill(Color[] target, bool light)
    {
        if (target == null) return;

        int n = Math.Min(target.Length, Count);
        for (int i = 0; i < n; i++) target[i] = Colour(i, light);
    }
}
