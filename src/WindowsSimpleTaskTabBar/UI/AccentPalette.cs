using WindowsSimpleTaskTabBar.Core.Settings;
using WindowsSimpleTaskTabBar.Core.Theme;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The square of colour shown beside each accent's name, for the one place a user has to choose
/// between them.
/// </summary>
/// <remarks>
/// The colours themselves belong to <see cref="BarPalette"/>, which holds one copy for the bar
/// and for this. What each accent is called is interface text and stands with the rest of it, in
/// <c>Core.Localization.UiStrings</c>.
/// </remarks>
internal static class AccentPalette
{
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

    /// <summary>The square of colour drawn beside an accent's name.</summary>
    public static Color Swatch(int accent)
    {
        if (accent < 0 || accent >= Swatches.Accents.Count) return Color.Transparent;

        return Color.FromArgb(255, Color.FromArgb(Swatches.Accents[accent]));
    }
}
