namespace WindowsSimpleTaskTabBar.Core.Localization;

/// <summary>
/// The languages the interface is available in, and how one is chosen.
/// </summary>
/// <remarks>
/// Adding a language costs one table in <see cref="UiStrings"/> and one row in
/// <see cref="All"/>. Nothing else is edited: the settings dialog lists whatever stands here,
/// and <see cref="Canonical"/> decides which row a Windows culture belongs to.
///
/// English is the source language. Every other table is a translation of it, and English is
/// what an unmatched culture falls back to. Only English and Japanese have been checked by
/// someone who reads them; corrections to the rest are welcome as issues or pull requests.
/// </remarks>
public static class Languages
{
    /// <summary>The source language, and the fallback for anything that does not match.</summary>
    public const string English = "en";

    /// <summary>
    /// The setting that means "take the language from Windows" rather than name one.
    /// </summary>
    /// <remarks>
    /// An empty string rather than a word, so a settings file written before this setting
    /// existed reads as automatic without needing to be recognised as an old file.
    /// </remarks>
    public const string Automatic = "";

    /// <summary>The font for a language that has no font of its own.</summary>
    private const string DefaultFontFamily = "Segoe UI";

    /// <summary>
    /// The languages that have a table, in the order the settings dialog lists them.
    /// </summary>
    /// <remarks>
    /// English first, as the source language, then the eleven of issue #35 in the order that
    /// issue lists them.
    ///
    /// Chinese, Japanese and Korean name a font of their own. Those three share code points, so
    /// one font cannot serve all three: a Japanese font draws Chinese with Japanese letter
    /// shapes, which a Chinese reader sees as wrong rather than as a missing character. The rest
    /// use the font Windows itself is written in.
    /// </remarks>
    private static readonly LanguageInfo[] Known =
    {
        new LanguageInfo(English, "English", DefaultFontFamily),
        new LanguageInfo("ja", "日本語", "Yu Gothic UI"),
        new LanguageInfo("zh-CN", "简体中文", "Microsoft YaHei UI"),
        new LanguageInfo("zh-TW", "繁體中文", "Microsoft JhengHei UI"),
        new LanguageInfo("ru", "Русский", DefaultFontFamily),
        new LanguageInfo("de", "Deutsch", DefaultFontFamily),
        new LanguageInfo("fr", "Français", DefaultFontFamily),
        new LanguageInfo("es", "Español", DefaultFontFamily),
        new LanguageInfo("pt-BR", "Português (Brasil)", DefaultFontFamily),
        new LanguageInfo("ko", "한국어", "Malgun Gothic"),
        new LanguageInfo("pl", "Polski", DefaultFontFamily),
        new LanguageInfo("it", "Italiano", DefaultFontFamily),
    };

    /// <summary>The languages that have a table.</summary>
    public static IReadOnlyList<LanguageInfo> All => Known;

    /// <summary>The language with this code, or null when there is none.</summary>
    public static LanguageInfo Find(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;

        foreach (LanguageInfo language in Known)
        {
            if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
                return language;
        }

        return null;
    }

    /// <summary>
    /// The font family to draw a language with, falling back to the default family when the
    /// language is not one that is available.
    /// </summary>
    /// <remarks>
    /// The family named here can be missing from a stripped-down Windows installation. Whoever
    /// creates the font is responsible for falling back to a font that exists.
    /// </remarks>
    public static string FontFamilyFor(string code)
    {
        return Find(code)?.FontFamily ?? DefaultFontFamily;
    }

    /// <summary>
    /// The language code a Windows culture belongs to, whether or not that language has a table
    /// yet.
    /// </summary>
    /// <remarks>
    /// Chinese and Portuguese are the two that cannot be answered by the two-letter code alone:
    ///
    /// - <c>zh-Hant</c>, <c>zh-TW</c>, <c>zh-HK</c> and <c>zh-MO</c> are Traditional Chinese;
    ///   every other Chinese culture, <c>zh-Hans</c> and <c>zh-SG</c> among them, is Simplified.
    /// - Only the Brazilian table exists for Portuguese, so <c>pt-PT</c> maps to it as well.
    ///
    /// Everything else is matched on its two-letter code, so <c>de-AT</c> reads the German table
    /// and a culture with no table of its own reads English.
    /// </remarks>
    public static string Canonical(string cultureName)
    {
        string name = (cultureName ?? string.Empty).Trim();
        if (name.Length == 0) return English;

        string[] parts = name.Split('-');
        string language = parts[0].ToLowerInvariant();

        if (language == "zh")
        {
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i].ToLowerInvariant();
                if (part == "hant" || part == "tw" || part == "hk" || part == "mo")
                    return "zh-TW";
            }

            return "zh-CN";
        }

        if (language == "pt") return "pt-BR";

        return language;
    }

    /// <summary>
    /// The language to draw the interface in: the one the user chose, the one Windows is set to,
    /// or English.
    /// </summary>
    /// <param name="preferred">
    /// The language in the settings, or <see cref="Automatic"/> to follow Windows. A code that
    /// has no table is treated as automatic, so a settings file naming a language that a later
    /// version dropped still starts.
    /// </param>
    /// <param name="cultureName">
    /// The name of the culture Windows is set to, for example <c>ja-JP</c>.
    /// </param>
    public static string Resolve(string preferred, string cultureName)
    {
        LanguageInfo chosen = Find(preferred);
        if (chosen != null) return chosen.Code;

        LanguageInfo fromWindows = Find(Canonical(cultureName));
        return fromWindows != null ? fromWindows.Code : English;
    }
}
