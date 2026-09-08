namespace WindowsSimpleTaskTabBar.Core.Localization;

/// <summary>
/// What every piece of interface text says, in each language.
/// </summary>
/// <remarks>
/// A plain table rather than the .NET resource system. Satellite assemblies add a folder and a
/// DLL for every language, and the application is distributed as a single executable, so the
/// tables are compiled in with the rest of the source. Standing in Core also means they can be
/// tested on any operating system.
///
/// English is the source language. A key missing from a translation falls back to the English
/// text rather than showing the user nothing; a unit test checks that no key is missing, so the
/// fallback is a safety net rather than a way of leaving one out.
/// </remarks>
public static class UiStrings
{
    private static readonly Dictionary<StringId, string> EnglishText = new Dictionary<StringId, string>
    {
        { StringId.MenuSettings, "Settings..." },
        { StringId.MenuRefresh, "Refresh" },
        { StringId.MenuExit, "Exit" },

        { StringId.TabMenuClose, "Close" },
        { StringId.TabMenuCloseOthers, "Close other tabs" },
        { StringId.TabMenuCloseLeft, "Close tabs to the left" },
        { StringId.TabMenuCloseRight, "Close tabs to the right" },
        { StringId.TabMenuMinimize, "Minimize" },

        { StringId.BarNoWindows, "No windows to show" },

        { StringId.SettingsTitle, "{0} settings" },
        { StringId.SettingsChangesApply, "Changes apply straight away." },
        { StringId.SettingsClose, "Close" },

        { StringId.HeightGroup, "Bar height" },
        { StringId.HeightStandard, "Standard ({0} px)" },
        { StringId.HeightCompact, "Compact ({0} px) - gives the height back to your windows" },

        { StringId.ColourGroup, "Colours" },
        { StringId.ColourFollowWindows, "Follow Windows" },
        { StringId.ColourLight, "Light" },
        { StringId.ColourDark, "Dark" },
        {
            StringId.ColourNote,
            "Light and Dark stay as you set them when Windows changes its own setting."
        },

        { StringId.LanguageGroup, "Language" },
        { StringId.LanguageAutomatic, "Automatic (follow Windows)" },

        { StringId.GroupsGroup, "Tab groups" },
        { StringId.GroupsEnable, "Group tabs by application" },
        {
            StringId.GroupsNote,
            "Windows of one application sit together and share a colour."
            + " Dragging a tab past another group moves its whole group."
        },
        { StringId.GroupsCaption, "Groups:" },
        { StringId.GroupsApplicationsCaption, "Applications in the selected group:" },
        { StringId.GroupsNew, "New group" },
        { StringId.GroupsRemove, "Remove" },
        { StringId.GroupsName, "Name:" },
        { StringId.GroupsColour, "Colour:" },
        { StringId.GroupsDefaultName, "Group {0}" },

        { StringId.AccentAutomatic, "Automatic" },
        { StringId.AccentBlue, "Blue" },
        { StringId.AccentRed, "Red" },
        { StringId.AccentYellow, "Yellow" },
        { StringId.AccentGreen, "Green" },
        { StringId.AccentPink, "Pink" },
        { StringId.AccentPurple, "Purple" },
        { StringId.AccentTeal, "Teal" },
        { StringId.AccentGrey, "Grey" },
    };

    private static readonly Dictionary<StringId, string> JapaneseText = new Dictionary<StringId, string>
    {
        { StringId.MenuSettings, "設定..." },
        { StringId.MenuRefresh, "更新" },
        { StringId.MenuExit, "終了" },

        { StringId.TabMenuClose, "閉じる" },
        { StringId.TabMenuCloseOthers, "他のタブを閉じる" },
        { StringId.TabMenuCloseLeft, "左側のタブを閉じる" },
        { StringId.TabMenuCloseRight, "右側のタブを閉じる" },
        { StringId.TabMenuMinimize, "最小化" },

        { StringId.BarNoWindows, "表示するウィンドウがありません" },

        { StringId.SettingsTitle, "{0} の設定" },
        { StringId.SettingsChangesApply, "変更はすぐに反映されます。" },
        { StringId.SettingsClose, "閉じる" },

        { StringId.HeightGroup, "バーの高さ" },
        { StringId.HeightStandard, "標準 ({0} px)" },
        { StringId.HeightCompact, "コンパクト ({0} px) - その分だけウィンドウを広く使えます" },

        { StringId.ColourGroup, "配色" },
        { StringId.ColourFollowWindows, "Windows に合わせる" },
        { StringId.ColourLight, "ライト" },
        { StringId.ColourDark, "ダーク" },
        {
            StringId.ColourNote,
            "ライトとダークは、Windows 側の設定が変わってもそのままです。"
        },

        { StringId.LanguageGroup, "言語" },
        { StringId.LanguageAutomatic, "自動 (Windows に合わせる)" },

        { StringId.GroupsGroup, "タブのグループ" },
        { StringId.GroupsEnable, "アプリごとにタブをまとめる" },
        {
            StringId.GroupsNote,
            "同じアプリのウィンドウが隣り合って並び、同じ色が付きます。"
            + "タブを別のグループの外までドラッグすると、そのグループ全体が移動します。"
        },
        { StringId.GroupsCaption, "グループ:" },
        { StringId.GroupsApplicationsCaption, "選択したグループのアプリ:" },
        { StringId.GroupsNew, "新しいグループ" },
        { StringId.GroupsRemove, "削除" },
        { StringId.GroupsName, "名前:" },
        { StringId.GroupsColour, "色:" },
        { StringId.GroupsDefaultName, "グループ {0}" },

        { StringId.AccentAutomatic, "自動" },
        { StringId.AccentBlue, "青" },
        { StringId.AccentRed, "赤" },
        { StringId.AccentYellow, "黄" },
        { StringId.AccentGreen, "緑" },
        { StringId.AccentPink, "ピンク" },
        { StringId.AccentPurple, "紫" },
        { StringId.AccentTeal, "青緑" },
        { StringId.AccentGrey, "グレー" },
    };

    /// <summary>One table per language, keyed by language code.</summary>
    private static readonly Dictionary<string, Dictionary<StringId, string>> Tables =
        new Dictionary<string, Dictionary<StringId, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { Languages.English, EnglishText },
            { "ja", JapaneseText },
        };

    /// <summary>
    /// The colours a tab group can be marked with, in the order
    /// <c>BarPalette.Accents</c> holds them.
    /// </summary>
    private static readonly StringId[] Accents =
    {
        StringId.AccentBlue, StringId.AccentRed, StringId.AccentYellow, StringId.AccentGreen,
        StringId.AccentPink, StringId.AccentPurple, StringId.AccentTeal, StringId.AccentGrey,
    };

    /// <summary>Whether a language has a table of its own.</summary>
    public static bool HasTable(string language)
    {
        return !string.IsNullOrEmpty(language) && Tables.ContainsKey(language);
    }

    /// <summary>The whole table for a language, for the test that compares them.</summary>
    public static IReadOnlyDictionary<StringId, string> Table(string language)
    {
        Dictionary<StringId, string> table;
        return language != null && Tables.TryGetValue(language, out table) ? table : EnglishText;
    }

    /// <summary>
    /// What one piece of text says in one language, falling back to English.
    /// </summary>
    public static string Get(string language, StringId id)
    {
        string text;
        if (Table(language).TryGetValue(id, out text)) return text;
        if (EnglishText.TryGetValue(id, out text)) return text;

        // Only reachable if a name is added to StringId and left out of every table, which the
        // key-parity test catches. The name is shown rather than nothing, so it can be traced.
        return id.ToString();
    }

    /// <summary>
    /// What one accent colour is called, or an empty string when the number is not one.
    /// </summary>
    public static string AccentName(string language, int accent)
    {
        return accent >= 0 && accent < Accents.Length
            ? Get(language, Accents[accent])
            : string.Empty;
    }
}
