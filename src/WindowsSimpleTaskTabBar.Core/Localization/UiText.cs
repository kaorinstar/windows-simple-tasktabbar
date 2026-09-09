using System.Globalization;

namespace WindowsSimpleTaskTabBar.Core.Localization;

/// <summary>
/// The interface text in one language, as the rest of the application reads it.
/// </summary>
/// <remarks>
/// Held as an object and passed to whatever draws text, rather than kept in a static that every
/// caller reads. Changing the language then means creating another one and rebuilding what shows
/// it, and a test can hold two languages at once.
/// </remarks>
public sealed class UiText
{
    /// <param name="language">
    /// A language code that has a table. Anything else falls back to English, so a caller never
    /// has to test what it is about to pass.
    /// </param>
    public UiText(string language)
    {
        LanguageInfo known = Languages.Find(language);
        Language = known != null ? known.Code : Languages.English;
    }

    /// <summary>The language this text is in.</summary>
    public string Language { get; }

    /// <summary>The font family this language is drawn with.</summary>
    public string FontFamily => Languages.FontFamilyFor(Language);

    /// <summary>What one piece of text says.</summary>
    public string this[StringId id] => UiStrings.Get(Language, id);

    /// <summary>
    /// What one piece of text says, with the numbers or names it leaves room for filled in.
    /// </summary>
    public string Format(StringId id, params object[] values)
    {
        return string.Format(CultureInfo.CurrentCulture, UiStrings.Get(Language, id), values);
    }

    /// <summary>What one accent colour is called.</summary>
    public string AccentName(int accent)
    {
        return UiStrings.AccentName(Language, accent);
    }
}
