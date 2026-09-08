namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// Creates the fonts the interface is drawn with, in the family the active language asks for.
/// </summary>
/// <remarks>
/// Chinese, Japanese and Korean share code points, so no single font serves all three: a
/// Japanese font draws Chinese text with Japanese letter shapes, which a Chinese reader sees as
/// wrong rather than as a missing character. <c>LanguageInfo.FontFamily</c> names the family for
/// each language, and everything that creates a font goes through here so the fallback is
/// written once.
/// </remarks>
internal static class UiFonts
{
    /// <summary>
    /// A font of the given family, or of the font Windows draws its own dialogs in when that
    /// family is not installed.
    /// </summary>
    /// <remarks>
    /// Asking for a family that is not installed does not fail: GDI+ quietly substitutes another
    /// face, and the font then reports the name of whatever it picked. That is what the check
    /// below reads, so the fallback is a font this application chose rather than one it was
    /// handed.
    ///
    /// The family is not looked up through <c>FontFamily</c> first. A family created from a name
    /// is a handle into a collection GDI+ keeps, and disposing it can leave a font made from the
    /// same name pointing at freed memory, which shows up later as "parameter is not valid" from
    /// something as ordinary as reading the font's height.
    /// </remarks>
    public static Font Create(string family, float size, GraphicsUnit unit)
    {
        var font = new Font(family, size, unit);
        if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase)) return font;

        font.Dispose();

        using Font dialog = SystemFonts.MessageBoxFont;
        return new Font(dialog.Name, size, unit);
    }

    /// <summary>
    /// A font for a dialog: the language's family, at the size Windows uses for its own dialogs,
    /// so the text follows the size the user set for Windows.
    /// </summary>
    public static Font ForDialog(string family)
    {
        using Font dialog = SystemFonts.MessageBoxFont;
        return Create(family, dialog.SizeInPoints, GraphicsUnit.Point);
    }
}
