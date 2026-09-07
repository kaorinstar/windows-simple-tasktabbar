using WindowsSimpleTaskTabBar.Core.Settings;

namespace WindowsSimpleTaskTabBar.Core.Theme;

/// <summary>
/// Every colour the bar draws with, chosen from the colour setting and, when that setting says
/// to follow Windows, from the Windows light and dark setting.
/// </summary>
/// <remarks>
/// Colours are plain 0xRRGGBB numbers rather than <c>System.Drawing.Color</c>, so that Core stays
/// free of System.Drawing and testable on any platform. The UI layer turns them into colours.
///
/// Reading which mode Windows is in needs the registry, so it stays in the UI layer as well.
/// This type is told the answer instead of asking for it, which is what makes the choice
/// testable.
/// </remarks>
public sealed class BarPalette
{
    private BarPalette() { }

    /// <summary>Which of the two palettes this is. Everything else follows from it.</summary>
    public bool IsLight { get; private set; }

    /// <summary>Behind the tabs, and the strip the bar reserves.</summary>
    public int Background { get; private set; }

    /// <summary>A tab that is neither active nor under the pointer.</summary>
    public int Tab { get; private set; }

    /// <summary>The tab under the pointer.</summary>
    public int TabHover { get; private set; }

    /// <summary>The tab of the window in the foreground.</summary>
    public int TabActive { get; private set; }

    /// <summary>Title text on an inactive tab.</summary>
    public int Text { get; private set; }

    /// <summary>Title text on the active tab.</summary>
    public int TextActive { get; private set; }

    /// <summary>
    /// The line along the bar's edge, the rule between two groups, and the fallback for a group
    /// whose accent number is out of range.
    /// </summary>
    public int Line { get; private set; }

    /// <summary>
    /// The accents a tab group can be marked with, in the shade that reads against the tabs of
    /// this palette.
    /// </summary>
    /// <remarks>
    /// The order matters as little as the names: a group's accent is picked from its own name,
    /// so what these have to be is eight colours a person can tell apart at three pixels tall,
    /// including on the lighter fill of the active tab.
    /// </remarks>
    public IReadOnlyList<int> Accents { get; private set; }

    /// <summary>
    /// The palette to draw with.
    /// </summary>
    /// <param name="mode">What the user chose.</param>
    /// <param name="windowsIsLight">
    /// Whether Windows itself is set to light. Read only when <paramref name="mode"/> follows
    /// Windows; the caller may pass anything otherwise.
    /// </param>
    /// <remarks>
    /// An unknown value for <paramref name="mode"/> follows Windows, which is the default. A
    /// settings file can be edited by hand, so nothing read from it is trusted.
    /// </remarks>
    public static BarPalette For(ColourMode mode, bool windowsIsLight)
    {
        bool light;
        if (mode == ColourMode.Light) light = true;
        else if (mode == ColourMode.Dark) light = false;
        else light = windowsIsLight;

        return light ? Light() : Dark();
    }

    private static BarPalette Light()
    {
        return new BarPalette
        {
            IsLight = true,
            Background = 0xF2F3F5,
            Tab = 0xE2E4E8,
            TabHover = 0xEBEDF0,
            TabActive = 0xFFFFFF,
            Text = 0x464A50,
            TextActive = 0x181A1E,
            Line = 0xD2D5DA,
            Accents = new[]
            {
                0x1A73E8, 0xD93025, 0xF29900, 0x188038,
                0xD01884, 0x8430CE, 0x007B83, 0x5F6368,
            },
        };
    }

    private static BarPalette Dark()
    {
        return new BarPalette
        {
            IsLight = false,
            Background = 0x202124,
            Tab = 0x303236,
            TabHover = 0x3A3D42,
            TabActive = 0x4E5258,
            Text = 0xB2B6BC,
            TextActive = 0xF5F6F8,
            Line = 0x18191C,
            Accents = new[]
            {
                0x8AB4F8, 0xF28B82, 0xFDD663, 0x81C995,
                0xFF8BCB, 0xC58AF9, 0x78D9EC, 0xBDC1C6,
            },
        };
    }
}
