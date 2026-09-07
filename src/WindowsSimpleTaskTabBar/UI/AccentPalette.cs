using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Core.Theme;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// What each accent is called, and the square of colour shown beside the name, for the one place
/// a user has to choose between them.
/// </summary>
/// <remarks>
/// The colours themselves belong to <see cref="BarPalette"/>, which holds one copy for the bar
/// and for this. Only the names are here: they are interface text, which Core has none of.
/// </remarks>
internal static class AccentPalette
{
    /// <summary>
    /// What each accent is called, in the same order as <see cref="BarPalette.Accents"/>.
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

    /// <summary>
    /// The light palette, for the squares in the settings dialog.
    /// </summary>
    /// <remarks>
    /// The light shade whatever the user set the bar to, because the dialog is drawn in the
    /// standard Windows controls, which stay light. The dark shades are pale by design, to read
    /// against a dark tab, and would wash out against this background.
    ///
    /// Built once: it is read while the list is painted, which happens on every row of an open
    /// drop-down.
    /// </remarks>
    private static readonly BarPalette Swatches = BarPalette.For(ColourMode.Light, true);

    /// <summary>What an accent is called, or an empty string when the number is not one.</summary>
    public static string Name(int accent)
    {
        return accent >= 0 && accent < AccentNames.Length ? AccentNames[accent] : string.Empty;
    }

    /// <summary>The square of colour drawn beside an accent's name.</summary>
    public static Color Swatch(int accent)
    {
        if (accent < 0 || accent >= Swatches.Accents.Count) return Color.Transparent;

        return Color.FromArgb(255, Color.FromArgb(Swatches.Accents[accent]));
    }
}
