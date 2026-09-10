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
    private static readonly Dictionary<StringId, string> EnglishText = new()
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

        { StringId.PreviewGroup, "Window preview" },
        { StringId.PreviewEnable, "Show the window when the pointer rests on a tab" },
        {
            StringId.PreviewNote,
            "Useful when several windows of one application look alike. A minimized window has"
            + " no picture to show and keeps its title instead."
        },

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

    private static readonly Dictionary<StringId, string> JapaneseText = new()
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

        { StringId.PreviewGroup, "ウィンドウのプレビュー" },
        { StringId.PreviewEnable, "タブにポインターを重ねたときにウィンドウを表示する" },
        {
            StringId.PreviewNote,
            "同じアプリのウィンドウが複数あるときに役立ちます。"
            + "最小化したウィンドウには表示できる画面がないため、題名だけを表示します。"
        },

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

    /// <summary>Simplified Chinese, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> SimplifiedChineseText = new()
    {
        { StringId.MenuSettings, "设置..." },
        { StringId.MenuRefresh, "刷新" },
        { StringId.MenuExit, "退出" },

        { StringId.TabMenuClose, "关闭" },
        { StringId.TabMenuCloseOthers, "关闭其他标签页" },
        { StringId.TabMenuCloseLeft, "关闭左侧标签页" },
        { StringId.TabMenuCloseRight, "关闭右侧标签页" },
        { StringId.TabMenuMinimize, "最小化" },

        { StringId.BarNoWindows, "没有可显示的窗口" },

        { StringId.SettingsTitle, "{0} 设置" },
        { StringId.SettingsChangesApply, "更改会立即生效。" },
        { StringId.SettingsClose, "关闭" },

        { StringId.HeightGroup, "工具栏高度" },
        { StringId.HeightStandard, "标准 ({0} px)" },
        { StringId.HeightCompact, "紧凑 ({0} px) - 把这部分高度还给窗口" },

        { StringId.ColourGroup, "颜色" },
        { StringId.ColourFollowWindows, "跟随 Windows" },
        { StringId.ColourLight, "浅色" },
        { StringId.ColourDark, "深色" },
        { StringId.ColourNote, "选择浅色或深色后，Windows 改变自身设置时也保持不变。" },

        { StringId.LanguageGroup, "语言" },
        { StringId.LanguageAutomatic, "自动 (跟随 Windows)" },

        { StringId.GroupsGroup, "标签页分组" },
        { StringId.GroupsEnable, "按应用分组标签页" },
        { StringId.GroupsNote, "同一个应用的窗口相邻排列，并标上同一种颜色。把标签页拖到另一个分组之外时，整个分组一起移动。" },
        { StringId.GroupsCaption, "分组:" },
        { StringId.GroupsApplicationsCaption, "所选分组中的应用:" },
        { StringId.GroupsNew, "新建分组" },
        { StringId.GroupsRemove, "删除" },
        { StringId.GroupsName, "名称:" },
        { StringId.GroupsColour, "颜色:" },
        { StringId.GroupsDefaultName, "分组 {0}" },

        { StringId.PreviewGroup, "窗口预览" },
        { StringId.PreviewEnable, "指针停在标签页上时显示窗口" },
        { StringId.PreviewNote, "同一个应用有多个外观相似的窗口时很有用。最小化的窗口没有可显示的画面，改为显示标题。" },

        { StringId.AccentAutomatic, "自动" },
        { StringId.AccentBlue, "蓝色" },
        { StringId.AccentRed, "红色" },
        { StringId.AccentYellow, "黄色" },
        { StringId.AccentGreen, "绿色" },
        { StringId.AccentPink, "粉色" },
        { StringId.AccentPurple, "紫色" },
        { StringId.AccentTeal, "青色" },
        { StringId.AccentGrey, "灰色" },
    };

    /// <summary>Traditional Chinese, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> TraditionalChineseText = new()
    {
        { StringId.MenuSettings, "設定..." },
        { StringId.MenuRefresh, "重新整理" },
        { StringId.MenuExit, "結束" },

        { StringId.TabMenuClose, "關閉" },
        { StringId.TabMenuCloseOthers, "關閉其他索引標籤" },
        { StringId.TabMenuCloseLeft, "關閉左側索引標籤" },
        { StringId.TabMenuCloseRight, "關閉右側索引標籤" },
        { StringId.TabMenuMinimize, "最小化" },

        { StringId.BarNoWindows, "沒有可顯示的視窗" },

        { StringId.SettingsTitle, "{0} 設定" },
        { StringId.SettingsChangesApply, "變更會立即生效。" },
        { StringId.SettingsClose, "關閉" },

        { StringId.HeightGroup, "工具列高度" },
        { StringId.HeightStandard, "標準 ({0} px)" },
        { StringId.HeightCompact, "精簡 ({0} px) - 把這部分高度還給視窗" },

        { StringId.ColourGroup, "色彩" },
        { StringId.ColourFollowWindows, "跟隨 Windows" },
        { StringId.ColourLight, "淺色" },
        { StringId.ColourDark, "深色" },
        { StringId.ColourNote, "選擇淺色或深色後，Windows 變更本身設定時也維持不變。" },

        { StringId.LanguageGroup, "語言" },
        { StringId.LanguageAutomatic, "自動 (跟隨 Windows)" },

        { StringId.GroupsGroup, "索引標籤群組" },
        { StringId.GroupsEnable, "依應用程式將索引標籤分組" },
        { StringId.GroupsNote, "同一個應用程式的視窗會相鄰排列，並標上同一種色彩。把索引標籤拖到另一個群組之外時，整個群組會一起移動。" },
        { StringId.GroupsCaption, "群組:" },
        { StringId.GroupsApplicationsCaption, "所選群組中的應用程式:" },
        { StringId.GroupsNew, "新增群組" },
        { StringId.GroupsRemove, "移除" },
        { StringId.GroupsName, "名稱:" },
        { StringId.GroupsColour, "色彩:" },
        { StringId.GroupsDefaultName, "群組 {0}" },

        { StringId.PreviewGroup, "視窗預覽" },
        { StringId.PreviewEnable, "指標停在索引標籤上時顯示視窗" },
        { StringId.PreviewNote, "同一個應用程式有多個外觀相似的視窗時很有用。最小化的視窗沒有可顯示的畫面，改為顯示標題。" },

        { StringId.AccentAutomatic, "自動" },
        { StringId.AccentBlue, "藍色" },
        { StringId.AccentRed, "紅色" },
        { StringId.AccentYellow, "黃色" },
        { StringId.AccentGreen, "綠色" },
        { StringId.AccentPink, "粉紅色" },
        { StringId.AccentPurple, "紫色" },
        { StringId.AccentTeal, "藍綠色" },
        { StringId.AccentGrey, "灰色" },
    };

    /// <summary>Russian, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> RussianText = new()
    {
        { StringId.MenuSettings, "Параметры..." },
        { StringId.MenuRefresh, "Обновить" },
        { StringId.MenuExit, "Выход" },

        { StringId.TabMenuClose, "Закрыть" },
        { StringId.TabMenuCloseOthers, "Закрыть другие вкладки" },
        { StringId.TabMenuCloseLeft, "Закрыть вкладки слева" },
        { StringId.TabMenuCloseRight, "Закрыть вкладки справа" },
        { StringId.TabMenuMinimize, "Свернуть" },

        { StringId.BarNoWindows, "Нет окон для показа" },

        { StringId.SettingsTitle, "Параметры {0}" },
        { StringId.SettingsChangesApply, "Изменения применяются сразу." },
        { StringId.SettingsClose, "Закрыть" },

        { StringId.HeightGroup, "Высота панели" },
        { StringId.HeightStandard, "Обычная ({0} px)" },
        { StringId.HeightCompact, "Компактная ({0} px) - возвращает высоту вашим окнам" },

        { StringId.ColourGroup, "Цвета" },
        { StringId.ColourFollowWindows, "Как в Windows" },
        { StringId.ColourLight, "Светлые" },
        { StringId.ColourDark, "Тёмные" },
        {
            StringId.ColourNote,
            "Светлые и тёмные остаются такими, как вы их выбрали, даже когда Windows меняет свою"
            + " настройку."
        },

        { StringId.LanguageGroup, "Язык" },
        { StringId.LanguageAutomatic, "Автоматически (как в Windows)" },

        { StringId.GroupsGroup, "Группы вкладок" },
        { StringId.GroupsEnable, "Группировать вкладки по приложению" },
        {
            StringId.GroupsNote,
            "Окна одного приложения стоят рядом и отмечены общим цветом. Если перетащить вкладку"
            + " за соседнюю группу, переместится вся её группа."
        },
        { StringId.GroupsCaption, "Группы:" },
        { StringId.GroupsApplicationsCaption, "Приложения в выбранной группе:" },
        { StringId.GroupsNew, "Создать группу" },
        { StringId.GroupsRemove, "Удалить" },
        { StringId.GroupsName, "Имя:" },
        { StringId.GroupsColour, "Цвет:" },
        { StringId.GroupsDefaultName, "Группа {0}" },

        { StringId.PreviewGroup, "Предпросмотр окна" },
        { StringId.PreviewEnable, "Показывать окно, когда указатель задерживается на вкладке" },
        {
            StringId.PreviewNote,
            "Помогает, когда несколько окон одного приложения выглядят одинаково. У свёрнутого"
            + " окна нет изображения, поэтому показывается его заголовок."
        },

        { StringId.AccentAutomatic, "Автоматически" },
        { StringId.AccentBlue, "Синий" },
        { StringId.AccentRed, "Красный" },
        { StringId.AccentYellow, "Жёлтый" },
        { StringId.AccentGreen, "Зелёный" },
        { StringId.AccentPink, "Розовый" },
        { StringId.AccentPurple, "Фиолетовый" },
        { StringId.AccentTeal, "Бирюзовый" },
        { StringId.AccentGrey, "Серый" },
    };

    /// <summary>German, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> GermanText = new()
    {
        { StringId.MenuSettings, "Einstellungen..." },
        { StringId.MenuRefresh, "Aktualisieren" },
        { StringId.MenuExit, "Beenden" },

        { StringId.TabMenuClose, "Schließen" },
        { StringId.TabMenuCloseOthers, "Andere Tabs schließen" },
        { StringId.TabMenuCloseLeft, "Tabs links schließen" },
        { StringId.TabMenuCloseRight, "Tabs rechts schließen" },
        { StringId.TabMenuMinimize, "Minimieren" },

        { StringId.BarNoWindows, "Keine Fenster vorhanden" },

        { StringId.SettingsTitle, "{0}-Einstellungen" },
        { StringId.SettingsChangesApply, "Änderungen gelten sofort." },
        { StringId.SettingsClose, "Schließen" },

        { StringId.HeightGroup, "Höhe der Leiste" },
        { StringId.HeightStandard, "Standard ({0} px)" },
        { StringId.HeightCompact, "Kompakt ({0} px) - gibt die Höhe an Ihre Fenster zurück" },

        { StringId.ColourGroup, "Farben" },
        { StringId.ColourFollowWindows, "Wie in Windows" },
        { StringId.ColourLight, "Hell" },
        { StringId.ColourDark, "Dunkel" },
        {
            StringId.ColourNote,
            "Hell und Dunkel bleiben so, wie Sie sie eingestellt haben, auch wenn Windows seine"
            + " eigene Einstellung ändert."
        },

        { StringId.LanguageGroup, "Sprache" },
        { StringId.LanguageAutomatic, "Automatisch (wie in Windows)" },

        { StringId.GroupsGroup, "Tabgruppen" },
        { StringId.GroupsEnable, "Tabs nach Anwendung gruppieren" },
        {
            StringId.GroupsNote,
            "Fenster einer Anwendung stehen nebeneinander und teilen sich eine Farbe. Wird ein"
            + " Tab über eine andere Gruppe hinaus gezogen, wandert die ganze Gruppe mit."
        },
        { StringId.GroupsCaption, "Gruppen:" },
        { StringId.GroupsApplicationsCaption, "Anwendungen in der gewählten Gruppe:" },
        { StringId.GroupsNew, "Neue Gruppe" },
        { StringId.GroupsRemove, "Entfernen" },
        { StringId.GroupsName, "Name:" },
        { StringId.GroupsColour, "Farbe:" },
        { StringId.GroupsDefaultName, "Gruppe {0}" },

        { StringId.PreviewGroup, "Fenstervorschau" },
        { StringId.PreviewEnable, "Das Fenster anzeigen, wenn der Zeiger auf einer Registerkarte ruht" },
        {
            StringId.PreviewNote,
            "Hilfreich, wenn mehrere Fenster einer Anwendung gleich aussehen. Ein minimiertes"
            + " Fenster hat kein Bild und zeigt stattdessen seinen Titel."
        },

        { StringId.AccentAutomatic, "Automatisch" },
        { StringId.AccentBlue, "Blau" },
        { StringId.AccentRed, "Rot" },
        { StringId.AccentYellow, "Gelb" },
        { StringId.AccentGreen, "Grün" },
        { StringId.AccentPink, "Rosa" },
        { StringId.AccentPurple, "Violett" },
        { StringId.AccentTeal, "Türkis" },
        { StringId.AccentGrey, "Grau" },
    };

    /// <summary>French, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> FrenchText = new()
    {
        { StringId.MenuSettings, "Paramètres..." },
        { StringId.MenuRefresh, "Actualiser" },
        { StringId.MenuExit, "Quitter" },

        { StringId.TabMenuClose, "Fermer" },
        { StringId.TabMenuCloseOthers, "Fermer les autres onglets" },
        { StringId.TabMenuCloseLeft, "Fermer les onglets à gauche" },
        { StringId.TabMenuCloseRight, "Fermer les onglets à droite" },
        { StringId.TabMenuMinimize, "Réduire" },

        { StringId.BarNoWindows, "Aucune fenêtre à afficher" },

        { StringId.SettingsTitle, "Paramètres de {0}" },
        { StringId.SettingsChangesApply, "Les modifications s'appliquent immédiatement." },
        { StringId.SettingsClose, "Fermer" },

        { StringId.HeightGroup, "Hauteur de la barre" },
        { StringId.HeightStandard, "Standard ({0} px)" },
        { StringId.HeightCompact, "Compacte ({0} px) - rend la hauteur à vos fenêtres" },

        { StringId.ColourGroup, "Couleurs" },
        { StringId.ColourFollowWindows, "Suivre Windows" },
        { StringId.ColourLight, "Clair" },
        { StringId.ColourDark, "Sombre" },
        {
            StringId.ColourNote,
            "Clair et Sombre restent tels que vous les avez choisis, même quand Windows change"
            + " son propre réglage."
        },

        { StringId.LanguageGroup, "Langue" },
        { StringId.LanguageAutomatic, "Automatique (suivre Windows)" },

        { StringId.GroupsGroup, "Groupes d'onglets" },
        { StringId.GroupsEnable, "Grouper les onglets par application" },
        {
            StringId.GroupsNote,
            "Les fenêtres d'une même application sont côte à côte et portent une même couleur."
            + " Faire glisser un onglet au-delà d'un autre groupe déplace tout son groupe."
        },
        { StringId.GroupsCaption, "Groupes :" },
        { StringId.GroupsApplicationsCaption, "Applications du groupe sélectionné :" },
        { StringId.GroupsNew, "Nouveau groupe" },
        { StringId.GroupsRemove, "Supprimer" },
        { StringId.GroupsName, "Nom :" },
        { StringId.GroupsColour, "Couleur :" },
        { StringId.GroupsDefaultName, "Groupe {0}" },

        { StringId.PreviewGroup, "Aperçu de la fenêtre" },
        { StringId.PreviewEnable, "Afficher la fenêtre lorsque le pointeur s'arrête sur un onglet" },
        {
            StringId.PreviewNote,
            "Utile lorsque plusieurs fenêtres d'une même application se ressemblent. Une fenêtre"
            + " réduite n'a pas d'image à montrer et affiche son titre à la place."
        },

        { StringId.AccentAutomatic, "Automatique" },
        { StringId.AccentBlue, "Bleu" },
        { StringId.AccentRed, "Rouge" },
        { StringId.AccentYellow, "Jaune" },
        { StringId.AccentGreen, "Vert" },
        { StringId.AccentPink, "Rose" },
        { StringId.AccentPurple, "Violet" },
        { StringId.AccentTeal, "Turquoise" },
        { StringId.AccentGrey, "Gris" },
    };

    /// <summary>Spanish, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> SpanishText = new()
    {
        { StringId.MenuSettings, "Configuración..." },
        { StringId.MenuRefresh, "Actualizar" },
        { StringId.MenuExit, "Salir" },

        { StringId.TabMenuClose, "Cerrar" },
        { StringId.TabMenuCloseOthers, "Cerrar las demás pestañas" },
        { StringId.TabMenuCloseLeft, "Cerrar las pestañas de la izquierda" },
        { StringId.TabMenuCloseRight, "Cerrar las pestañas de la derecha" },
        { StringId.TabMenuMinimize, "Minimizar" },

        { StringId.BarNoWindows, "No hay ventanas que mostrar" },

        { StringId.SettingsTitle, "Configuración de {0}" },
        { StringId.SettingsChangesApply, "Los cambios se aplican al instante." },
        { StringId.SettingsClose, "Cerrar" },

        { StringId.HeightGroup, "Altura de la barra" },
        { StringId.HeightStandard, "Estándar ({0} px)" },
        { StringId.HeightCompact, "Compacta ({0} px) - devuelve esa altura a tus ventanas" },

        { StringId.ColourGroup, "Colores" },
        { StringId.ColourFollowWindows, "Seguir a Windows" },
        { StringId.ColourLight, "Claro" },
        { StringId.ColourDark, "Oscuro" },
        {
            StringId.ColourNote,
            "Claro y Oscuro se mantienen como los elijas, aunque Windows cambie su propia opción."
        },

        { StringId.LanguageGroup, "Idioma" },
        { StringId.LanguageAutomatic, "Automático (seguir a Windows)" },

        { StringId.GroupsGroup, "Grupos de pestañas" },
        { StringId.GroupsEnable, "Agrupar las pestañas por aplicación" },
        {
            StringId.GroupsNote,
            "Las ventanas de una misma aplicación quedan juntas y llevan un mismo color. Al"
            + " arrastrar una pestaña más allá de otro grupo se mueve el grupo entero."
        },
        { StringId.GroupsCaption, "Grupos:" },
        { StringId.GroupsApplicationsCaption, "Aplicaciones del grupo seleccionado:" },
        { StringId.GroupsNew, "Nuevo grupo" },
        { StringId.GroupsRemove, "Quitar" },
        { StringId.GroupsName, "Nombre:" },
        { StringId.GroupsColour, "Color:" },
        { StringId.GroupsDefaultName, "Grupo {0}" },

        { StringId.PreviewGroup, "Vista previa de la ventana" },
        { StringId.PreviewEnable, "Mostrar la ventana cuando el puntero se detiene en una pestaña" },
        {
            StringId.PreviewNote,
            "Útil cuando varias ventanas de una misma aplicación se parecen. Una ventana"
            + " minimizada no tiene imagen que mostrar y muestra su título en su lugar."
        },

        { StringId.AccentAutomatic, "Automático" },
        { StringId.AccentBlue, "Azul" },
        { StringId.AccentRed, "Rojo" },
        { StringId.AccentYellow, "Amarillo" },
        { StringId.AccentGreen, "Verde" },
        { StringId.AccentPink, "Rosa" },
        { StringId.AccentPurple, "Morado" },
        { StringId.AccentTeal, "Verde azulado" },
        { StringId.AccentGrey, "Gris" },
    };

    /// <summary>Portuguese (Brazil), a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> PortugueseText = new()
    {
        { StringId.MenuSettings, "Configurações..." },
        { StringId.MenuRefresh, "Atualizar" },
        { StringId.MenuExit, "Sair" },

        { StringId.TabMenuClose, "Fechar" },
        { StringId.TabMenuCloseOthers, "Fechar as outras abas" },
        { StringId.TabMenuCloseLeft, "Fechar as abas à esquerda" },
        { StringId.TabMenuCloseRight, "Fechar as abas à direita" },
        { StringId.TabMenuMinimize, "Minimizar" },

        { StringId.BarNoWindows, "Nenhuma janela para mostrar" },

        { StringId.SettingsTitle, "Configurações do {0}" },
        { StringId.SettingsChangesApply, "As alterações são aplicadas na hora." },
        { StringId.SettingsClose, "Fechar" },

        { StringId.HeightGroup, "Altura da barra" },
        { StringId.HeightStandard, "Padrão ({0} px)" },
        { StringId.HeightCompact, "Compacta ({0} px) - devolve essa altura às suas janelas" },

        { StringId.ColourGroup, "Cores" },
        { StringId.ColourFollowWindows, "Seguir o Windows" },
        { StringId.ColourLight, "Claro" },
        { StringId.ColourDark, "Escuro" },
        {
            StringId.ColourNote,
            "Claro e Escuro continuam como você escolheu, mesmo quando o Windows muda a própria"
            + " configuração."
        },

        { StringId.LanguageGroup, "Idioma" },
        { StringId.LanguageAutomatic, "Automático (seguir o Windows)" },

        { StringId.GroupsGroup, "Grupos de abas" },
        { StringId.GroupsEnable, "Agrupar as abas por aplicativo" },
        {
            StringId.GroupsNote,
            "As janelas de um mesmo aplicativo ficam juntas e levam uma mesma cor. Arrastar uma"
            + " aba para além de outro grupo move o grupo inteiro."
        },
        { StringId.GroupsCaption, "Grupos:" },
        { StringId.GroupsApplicationsCaption, "Aplicativos do grupo selecionado:" },
        { StringId.GroupsNew, "Novo grupo" },
        { StringId.GroupsRemove, "Remover" },
        { StringId.GroupsName, "Nome:" },
        { StringId.GroupsColour, "Cor:" },
        { StringId.GroupsDefaultName, "Grupo {0}" },

        { StringId.PreviewGroup, "Visualização da janela" },
        { StringId.PreviewEnable, "Mostrar a janela quando o ponteiro parar sobre uma aba" },
        {
            StringId.PreviewNote,
            "Útil quando várias janelas de um mesmo aplicativo se parecem. Uma janela minimizada"
            + " não tem imagem para mostrar e exibe o título no lugar."
        },

        { StringId.AccentAutomatic, "Automático" },
        { StringId.AccentBlue, "Azul" },
        { StringId.AccentRed, "Vermelho" },
        { StringId.AccentYellow, "Amarelo" },
        { StringId.AccentGreen, "Verde" },
        { StringId.AccentPink, "Rosa" },
        { StringId.AccentPurple, "Roxo" },
        { StringId.AccentTeal, "Verde-azulado" },
        { StringId.AccentGrey, "Cinza" },
    };

    /// <summary>Korean, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> KoreanText = new()
    {
        { StringId.MenuSettings, "설정..." },
        { StringId.MenuRefresh, "새로 고침" },
        { StringId.MenuExit, "끝내기" },

        { StringId.TabMenuClose, "닫기" },
        { StringId.TabMenuCloseOthers, "다른 탭 닫기" },
        { StringId.TabMenuCloseLeft, "왼쪽 탭 닫기" },
        { StringId.TabMenuCloseRight, "오른쪽 탭 닫기" },
        { StringId.TabMenuMinimize, "최소화" },

        { StringId.BarNoWindows, "표시할 창이 없습니다" },

        { StringId.SettingsTitle, "{0} 설정" },
        { StringId.SettingsChangesApply, "변경 내용은 바로 적용됩니다." },
        { StringId.SettingsClose, "닫기" },

        { StringId.HeightGroup, "막대 높이" },
        { StringId.HeightStandard, "표준 ({0} px)" },
        { StringId.HeightCompact, "좁게 ({0} px) - 그만큼 창을 넓게 사용합니다" },

        { StringId.ColourGroup, "색" },
        { StringId.ColourFollowWindows, "Windows 설정 따르기" },
        { StringId.ColourLight, "밝게" },
        { StringId.ColourDark, "어둡게" },
        { StringId.ColourNote, "밝게와 어둡게는 Windows 설정이 바뀌어도 그대로 유지됩니다." },

        { StringId.LanguageGroup, "언어" },
        { StringId.LanguageAutomatic, "자동 (Windows 설정 따르기)" },

        { StringId.GroupsGroup, "탭 그룹" },
        { StringId.GroupsEnable, "앱별로 탭 묶기" },
        { StringId.GroupsNote, "같은 앱의 창이 나란히 놓이고 같은 색이 표시됩니다. 탭을 다른 그룹 밖으로 끌면 그룹 전체가 이동합니다." },
        { StringId.GroupsCaption, "그룹:" },
        { StringId.GroupsApplicationsCaption, "선택한 그룹의 앱:" },
        { StringId.GroupsNew, "새 그룹" },
        { StringId.GroupsRemove, "삭제" },
        { StringId.GroupsName, "이름:" },
        { StringId.GroupsColour, "색:" },
        { StringId.GroupsDefaultName, "그룹 {0}" },

        { StringId.PreviewGroup, "창 미리 보기" },
        { StringId.PreviewEnable, "포인터를 탭에 올려 두면 창을 표시" },
        {
            StringId.PreviewNote,
            "같은 앱의 창이 여러 개이고 비슷해 보일 때 유용합니다."
            + " 최소화된 창은 보여 줄 화면이 없어 제목을 대신 표시합니다."
        },

        { StringId.AccentAutomatic, "자동" },
        { StringId.AccentBlue, "파랑" },
        { StringId.AccentRed, "빨강" },
        { StringId.AccentYellow, "노랑" },
        { StringId.AccentGreen, "초록" },
        { StringId.AccentPink, "분홍" },
        { StringId.AccentPurple, "보라" },
        { StringId.AccentTeal, "청록" },
        { StringId.AccentGrey, "회색" },
    };

    /// <summary>Polish, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> PolishText = new()
    {
        { StringId.MenuSettings, "Ustawienia..." },
        { StringId.MenuRefresh, "Odśwież" },
        { StringId.MenuExit, "Zakończ" },

        { StringId.TabMenuClose, "Zamknij" },
        { StringId.TabMenuCloseOthers, "Zamknij pozostałe karty" },
        { StringId.TabMenuCloseLeft, "Zamknij karty po lewej" },
        { StringId.TabMenuCloseRight, "Zamknij karty po prawej" },
        { StringId.TabMenuMinimize, "Minimalizuj" },

        { StringId.BarNoWindows, "Brak okien do pokazania" },

        { StringId.SettingsTitle, "Ustawienia {0}" },
        { StringId.SettingsChangesApply, "Zmiany działają od razu." },
        { StringId.SettingsClose, "Zamknij" },

        { StringId.HeightGroup, "Wysokość paska" },
        { StringId.HeightStandard, "Standardowa ({0} px)" },
        { StringId.HeightCompact, "Kompaktowa ({0} px) - oddaje tę wysokość Twoim oknom" },

        { StringId.ColourGroup, "Kolory" },
        { StringId.ColourFollowWindows, "Jak w systemie Windows" },
        { StringId.ColourLight, "Jasne" },
        { StringId.ColourDark, "Ciemne" },
        {
            StringId.ColourNote,
            "Jasne i ciemne pozostają takie, jak je ustawisz, nawet gdy Windows zmieni własne"
            + " ustawienie."
        },

        { StringId.LanguageGroup, "Język" },
        { StringId.LanguageAutomatic, "Automatycznie (jak w systemie Windows)" },

        { StringId.GroupsGroup, "Grupy kart" },
        { StringId.GroupsEnable, "Grupuj karty według aplikacji" },
        {
            StringId.GroupsNote,
            "Okna jednej aplikacji stoją obok siebie i mają wspólny kolor. Przeciągnięcie karty"
            + " poza sąsiednią grupę przenosi całą jej grupę."
        },
        { StringId.GroupsCaption, "Grupy:" },
        { StringId.GroupsApplicationsCaption, "Aplikacje w wybranej grupie:" },
        { StringId.GroupsNew, "Nowa grupa" },
        { StringId.GroupsRemove, "Usuń" },
        { StringId.GroupsName, "Nazwa:" },
        { StringId.GroupsColour, "Kolor:" },
        { StringId.GroupsDefaultName, "Grupa {0}" },

        { StringId.PreviewGroup, "Podgląd okna" },
        { StringId.PreviewEnable, "Pokaż okno, gdy wskaźnik zatrzyma się na karcie" },
        {
            StringId.PreviewNote,
            "Przydatne, gdy kilka okien tej samej aplikacji wygląda podobnie. Zminimalizowane"
            + " okno nie ma obrazu do pokazania i wyświetla swój tytuł."
        },

        { StringId.AccentAutomatic, "Automatycznie" },
        { StringId.AccentBlue, "Niebieski" },
        { StringId.AccentRed, "Czerwony" },
        { StringId.AccentYellow, "Żółty" },
        { StringId.AccentGreen, "Zielony" },
        { StringId.AccentPink, "Różowy" },
        { StringId.AccentPurple, "Fioletowy" },
        { StringId.AccentTeal, "Turkusowy" },
        { StringId.AccentGrey, "Szary" },
    };

    /// <summary>Italian, a translation of the English table above.</summary>
    private static readonly Dictionary<StringId, string> ItalianText = new()
    {
        { StringId.MenuSettings, "Impostazioni..." },
        { StringId.MenuRefresh, "Aggiorna" },
        { StringId.MenuExit, "Esci" },

        { StringId.TabMenuClose, "Chiudi" },
        { StringId.TabMenuCloseOthers, "Chiudi le altre schede" },
        { StringId.TabMenuCloseLeft, "Chiudi le schede a sinistra" },
        { StringId.TabMenuCloseRight, "Chiudi le schede a destra" },
        { StringId.TabMenuMinimize, "Riduci a icona" },

        { StringId.BarNoWindows, "Nessuna finestra da mostrare" },

        { StringId.SettingsTitle, "Impostazioni di {0}" },
        { StringId.SettingsChangesApply, "Le modifiche vengono applicate subito." },
        { StringId.SettingsClose, "Chiudi" },

        { StringId.HeightGroup, "Altezza della barra" },
        { StringId.HeightStandard, "Standard ({0} px)" },
        {
            StringId.HeightCompact,
            "Compatta ({0} px) - restituisce quell'altezza alle tue finestre"
        },

        { StringId.ColourGroup, "Colori" },
        { StringId.ColourFollowWindows, "Segui Windows" },
        { StringId.ColourLight, "Chiaro" },
        { StringId.ColourDark, "Scuro" },
        {
            StringId.ColourNote,
            "Chiaro e Scuro restano come li hai impostati, anche quando Windows cambia la"
            + " propria impostazione."
        },

        { StringId.LanguageGroup, "Lingua" },
        { StringId.LanguageAutomatic, "Automatica (segui Windows)" },

        { StringId.GroupsGroup, "Gruppi di schede" },
        { StringId.GroupsEnable, "Raggruppa le schede per applicazione" },
        {
            StringId.GroupsNote,
            "Le finestre di una stessa applicazione stanno vicine e portano uno stesso colore."
            + " Trascinando una scheda oltre un altro gruppo si sposta l'intero gruppo."
        },
        { StringId.GroupsCaption, "Gruppi:" },
        { StringId.GroupsApplicationsCaption, "Applicazioni del gruppo selezionato:" },
        { StringId.GroupsNew, "Nuovo gruppo" },
        { StringId.GroupsRemove, "Rimuovi" },
        { StringId.GroupsName, "Nome:" },
        { StringId.GroupsColour, "Colore:" },
        { StringId.GroupsDefaultName, "Gruppo {0}" },

        { StringId.PreviewGroup, "Anteprima della finestra" },
        { StringId.PreviewEnable, "Mostra la finestra quando il puntatore si ferma su una scheda" },
        {
            StringId.PreviewNote,
            "Utile quando più finestre della stessa applicazione si somigliano. Una finestra"
            + " ridotta a icona non ha un'immagine da mostrare e mostra il suo titolo."
        },

        { StringId.AccentAutomatic, "Automatica" },
        { StringId.AccentBlue, "Blu" },
        { StringId.AccentRed, "Rosso" },
        { StringId.AccentYellow, "Giallo" },
        { StringId.AccentGreen, "Verde" },
        { StringId.AccentPink, "Rosa" },
        { StringId.AccentPurple, "Viola" },
        { StringId.AccentTeal, "Turchese" },
        { StringId.AccentGrey, "Grigio" },
    };

    /// <summary>One table per language, keyed by language code.</summary>
    private static readonly Dictionary<string, Dictionary<StringId, string>> Tables =
        new Dictionary<string, Dictionary<StringId, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { Languages.English, EnglishText },
            { "ja", JapaneseText },
            { "zh-CN", SimplifiedChineseText },
            { "zh-TW", TraditionalChineseText },
            { "ru", RussianText },
            { "de", GermanText },
            { "fr", FrenchText },
            { "es", SpanishText },
            { "pt-BR", PortugueseText },
            { "ko", KoreanText },
            { "pl", PolishText },
            { "it", ItalianText },
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
