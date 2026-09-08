namespace WindowsSimpleTaskTabBar.Core.Localization;

/// <summary>
/// Every piece of text the user reads, named rather than written where it is drawn.
/// </summary>
/// <remarks>
/// An enumeration rather than string keys, so a name that does not exist fails the build instead
/// of appearing on the bar as a key. <c>UiStrings</c> holds one table per language, and a unit
/// test checks that every table answers every name here.
///
/// The window titles of other applications are not in this list. They come from the application
/// that owns the window and are shown as they are.
/// </remarks>
public enum StringId
{
    // The menu shown by the tray icon and by the bar itself.
    MenuSettings,
    MenuRefresh,
    MenuExit,

    // The menu shown on a single tab.
    TabMenuClose,
    TabMenuCloseOthers,
    TabMenuCloseLeft,
    TabMenuCloseRight,
    TabMenuMinimize,

    /// <summary>Drawn on the bar while no window is open.</summary>
    BarNoWindows,

    // The settings dialog.
    SettingsTitle,
    SettingsChangesApply,
    SettingsClose,

    HeightGroup,
    HeightStandard,
    HeightCompact,

    ColourGroup,
    ColourFollowWindows,
    ColourLight,
    ColourDark,
    ColourNote,

    LanguageGroup,
    LanguageAutomatic,

    GroupsGroup,
    GroupsEnable,
    GroupsNote,
    GroupsCaption,
    GroupsApplicationsCaption,
    GroupsNew,
    GroupsRemove,
    GroupsName,
    GroupsColour,
    GroupsDefaultName,

    // The colours a tab group can be marked with, as named in the settings dialog.
    AccentAutomatic,
    AccentBlue,
    AccentRed,
    AccentYellow,
    AccentGreen,
    AccentPink,
    AccentPurple,
    AccentTeal,
    AccentGrey,
}
