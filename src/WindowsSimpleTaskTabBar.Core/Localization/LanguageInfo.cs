namespace WindowsSimpleTaskTabBar.Core.Localization;

/// <summary>
/// One language the interface is available in.
/// </summary>
/// <remarks>
/// The name is the language's own name rather than its English one: someone looking for their
/// language in the settings reads the list in that language, not in English.
///
/// The font belongs here because Chinese, Japanese and Korean share code points. A Japanese font
/// draws Chinese text with Japanese letter shapes, which a Chinese reader sees as wrong rather
/// than as a missing character, so the font is chosen from the language rather than fixed.
/// </remarks>
public sealed class LanguageInfo
{
    public LanguageInfo(string code, string nativeName, string fontFamily)
    {
        Code = code;
        NativeName = nativeName;
        FontFamily = fontFamily;
    }

    /// <summary>The language code, as used in the settings file, for example <c>ja</c>.</summary>
    public string Code { get; }

    /// <summary>What the language is called in that language.</summary>
    public string NativeName { get; }

    /// <summary>The font family the interface is drawn with in this language.</summary>
    public string FontFamily { get; }
}
