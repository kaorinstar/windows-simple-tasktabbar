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
    /// face, and the text is then drawn in whatever it picked. The check comes first so that the
    /// fallback is a font this application chose.
    /// </remarks>
    public static Font Create(string family, float size, GraphicsUnit unit)
    {
        if (IsInstalled(family)) return new Font(family, size, unit);

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

    /// <summary>Whether a font family is installed on this machine.</summary>
    /// <remarks>
    /// A stripped-down Windows installation can be missing any of the families named for a
    /// language. Only the exception GDI+ raises for a family it cannot find is caught.
    /// </remarks>
    private static bool IsInstalled(string family)
    {
        if (string.IsNullOrEmpty(family)) return false;

        try
        {
            using (new FontFamily(family)) { }
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
