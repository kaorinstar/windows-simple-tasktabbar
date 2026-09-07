using System.Diagnostics.CodeAnalysis;
using WindowsSimpleTaskTabBar.Core.Grouping;
using WindowsSimpleTaskTabBar.Core.Localization;
using WindowsSimpleTaskTabBar.Core.Settings;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The settings dialog, opened from the tray menu.
/// </summary>
/// <remarks>
/// Built in code rather than with the designer, so the whole layout is visible in one file and
/// there is no generated partial class to keep in step. Sizes come from layout panels rather
/// than fixed coordinates, so the dialog still fits its text at a high DPI.
///
/// Changes apply as soon as they are made, so there is a Close button and no OK or Cancel: the
/// effect shows on the bar behind the dialog while the user is choosing.
/// </remarks>
internal sealed class SettingsForm : Form
{
    /// <summary>The name shown in the title bar. A product name, so it is not translated.</summary>
    private const string ProductName = "WindowsSimpleTaskTabBar";

    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private readonly Func<List<string>> _runningApplications;
    private readonly Func<UiText> _currentText;

    /// <summary>
    /// The interface text this dialog is drawn in. Asked for again after the language is
    /// changed, so it is not readonly.
    /// </summary>
    private UiText _text;

    /// <summary>
    /// The dialog's font, in the family the language asks for. Nothing else owns it, so
    /// <see cref="Dispose(bool)"/> releases it.
    /// </summary>
    private Font _uiFont;

    private bool _loading;

    // Every control below is added to a Controls collection in BuildControls, and a control is
    // disposed by whatever it was added to. CA2213 cannot see that, so the ownership is stated
    // here.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private ComboBox _language;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _standard;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _compact;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _followWindows;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _light;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _dark;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private CheckBox _groupByApplication;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private Panel _groupDetail;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private ListBox _groups;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private CheckedListBox _applications;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private TextBox _groupName;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private ComboBox _groupAccent;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private Button _addGroup;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private Button _removeGroup;

    /// <param name="settings">The live settings object, edited in place.</param>
    /// <param name="onChanged">Called after every change, to apply and save it.</param>
    /// <param name="runningApplications">
    /// The executables that have a window open, asked for again each time the list is shown
    /// rather than taken once, so a window opened while the dialog is up still appears.
    /// </param>
    /// <param name="currentText">
    /// The interface text as the bar has it. Asked for rather than passed once, because changing
    /// the language here changes what the bar answers, and this dialog draws itself again from
    /// the answer.
    /// </param>
    public SettingsForm(AppSettings settings, Action onChanged,
        Func<List<string>> runningApplications, Func<UiText> currentText)
    {
        _settings = settings;
        _onChanged = onChanged;
        _runningApplications = runningApplications;
        _currentText = currentText;
        _text = currentText();

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        Build();
    }

    /// <summary>
    /// Draws the whole dialog in the current language: the title, the font, and every control.
    /// </summary>
    private void Build()
    {
        Text = _text.Format(StringId.SettingsTitle, ProductName);
        ApplyFont();
        BuildControls();
        LoadFromSettings();
    }

    /// <summary>
    /// Gives the dialog the font of the language it is drawn in.
    /// </summary>
    /// <remarks>
    /// The size comes from the font Windows draws its own dialogs in, so the text still follows
    /// the size the user set for Windows; only the family is chosen here.
    /// </remarks>
    private void ApplyFont()
    {
        Font previous = _uiFont;

        _uiFont = UiFonts.ForDialog(_text.FontFamily);
        Font = _uiFont;

        // After the new one is in place: the controls are measured against whatever Font holds.
        previous?.Dispose();
    }

    /// <summary>
    /// Builds the dialog again in the language just chosen.
    /// </summary>
    /// <remarks>
    /// Each label is written where its control is created, so nothing here walks the controls
    /// replacing text: they are thrown away and built again by the same code that built them the
    /// first time. The dialog sizes itself from its contents, so it grows or shrinks to fit the
    /// new language rather than cutting a longer label off.
    /// </remarks>
    private void Rebuild()
    {
        Control previous = Controls.Count > 0 ? Controls[0] : null;
        Controls.Clear();
        previous?.Dispose();

        _text = _currentText();
        Build();

        // Back to the box the language was chosen in, which the rebuild has just replaced.
        _language.Focus();
    }

    /// <summary>
    /// Releases the font this dialog owns. The base call comes first, so no control is being
    /// disposed while the font it was drawn with is going away.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        _uiFont?.Dispose();
        _uiFont = null;
    }

    private void BuildControls()
    {
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        root.Controls.Add(BuildLanguageGroup());
        root.Controls.Add(BuildHeightGroup());
        root.Controls.Add(BuildColourGroup());
        root.Controls.Add(BuildGroupingGroup());

        var note = new Label
        {
            Text = _text[StringId.SettingsChangesApply],
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(14, 0, 12, 8),
        };

        var close = new Button
        {
            Text = _text[StringId.SettingsClose],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(12, 0, 12, 12),
            DialogResult = DialogResult.OK,
        };

        root.Controls.Add(note);
        root.Controls.Add(close);

        Controls.Add(root);

        AcceptButton = close;
        CancelButton = close;
    }

    /// <remarks>
    /// A drop-down rather than a row of buttons: the list grows with every language added, and
    /// each language is listed under its own name, so that someone who cannot read the language
    /// the dialog is currently in can still find theirs.
    ///
    /// The choice applies at once, like every other setting here: the bar, its menus and this
    /// dialog are all drawn again in the language just chosen.
    /// </remarks>
    private GroupBox BuildLanguageGroup()
    {
        _language = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(4, 4, 4, 4),
        };

        _language.Items.Add(_text[StringId.LanguageAutomatic]);
        foreach (LanguageInfo language in Languages.All)
            _language.Items.Add(language.NativeName);

        // Wide enough for the longest entry, measured rather than guessed: the entries are in
        // different languages and their lengths are not known here.
        int widest = 0;
        foreach (object item in _language.Items)
            widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), Font).Width);

        _language.Width = widest + Font.Height * 3;   // the name, and the arrow after it
        _language.SelectedIndexChanged += (_, __) => OnLanguageChanged();

        var choices = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        choices.Controls.Add(_language);

        var box = new GroupBox
        {
            Text = _text[StringId.LanguageGroup],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 12, 12, 6),
        };
        box.Controls.Add(choices);
        return box;
    }

    private GroupBox BuildHeightGroup()
    {
        _standard = new RadioButton
        {
            Text = _text.Format(StringId.HeightStandard,
                AppSettings.HeightInPixels(BarHeightMode.Standard)),
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 2),
        };
        _standard.CheckedChanged += (_, __) => OnHeightChanged();

        _compact = new RadioButton
        {
            Text = _text.Format(StringId.HeightCompact,
                AppSettings.HeightInPixels(BarHeightMode.Compact)),
            AutoSize = true,
            Margin = new Padding(4, 2, 4, 4),
        };
        _compact.CheckedChanged += (_, __) => OnHeightChanged();

        var choices = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        choices.Controls.Add(_standard);
        choices.Controls.Add(_compact);

        var box = new GroupBox
        {
            Text = _text[StringId.HeightGroup],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 6, 12, 6),
        };
        box.Controls.Add(choices);
        return box;
    }

    /// <remarks>
    /// Three buttons rather than a tick box for dark: "follow Windows" is not the same choice as
    /// light or dark, and a tick box would have to say so in its label.
    /// </remarks>
    private GroupBox BuildColourGroup()
    {
        _followWindows = new RadioButton
        {
            Text = _text[StringId.ColourFollowWindows],
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 2),
        };
        _followWindows.CheckedChanged += (_, __) => OnColoursChanged();

        _light = new RadioButton
        {
            Text = _text[StringId.ColourLight],
            AutoSize = true,
            Margin = new Padding(4, 2, 4, 2),
        };
        _light.CheckedChanged += (_, __) => OnColoursChanged();

        _dark = new RadioButton
        {
            Text = _text[StringId.ColourDark],
            AutoSize = true,
            Margin = new Padding(4, 2, 4, 4),
        };
        _dark.CheckedChanged += (_, __) => OnColoursChanged();

        var explanation = new Label
        {
            Text = _text[StringId.ColourNote],
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(4, 0, 4, 4),
        };

        var choices = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        choices.Controls.Add(_followWindows);
        choices.Controls.Add(_light);
        choices.Controls.Add(_dark);
        choices.Controls.Add(explanation);

        var box = new GroupBox
        {
            Text = _text[StringId.ColourGroup],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 6, 12, 6),
        };
        box.Controls.Add(choices);
        return box;
    }

    /// <remarks>
    /// The list boxes are the one thing here that cannot size itself, so their size is taken
    /// from the font rather than written in pixels. That way it follows the DPI and the user's
    /// text size without this file knowing what either of them is.
    /// </remarks>
    private GroupBox BuildGroupingGroup()
    {
        int row = Font.Height;
        int column = TextRenderer.MeasureText("chromium-browser.exe", Font).Width + row * 2;

        _groupByApplication = new CheckBox
        {
            Text = _text[StringId.GroupsEnable],
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 2),
        };
        _groupByApplication.CheckedChanged += (_, __) => OnGroupingToggled();

        var explanation = new Label
        {
            Text = _text[StringId.GroupsNote],
            AutoSize = true,
            MaximumSize = new Size(column * 2, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(22, 0, 4, 6),
        };

        _groups = new ListBox
        {
            Height = row * 5,
            Width = column,
            Margin = new Padding(4, 0, 8, 4),
            IntegralHeight = false,
        };
        _groups.SelectedIndexChanged += (_, __) => OnGroupSelected();

        _applications = new CheckedListBox
        {
            Height = row * 5,
            Width = column,
            Margin = new Padding(0, 0, 4, 4),
            CheckOnClick = true,
            IntegralHeight = false,
        };
        _applications.ItemCheck += OnApplicationChecked;

        var lists = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 0, 4, 0),
        };
        lists.Controls.Add(Caption(_text[StringId.GroupsCaption]), 0, 0);
        lists.Controls.Add(Caption(_text[StringId.GroupsApplicationsCaption]), 1, 0);
        lists.Controls.Add(_groups, 0, 1);
        lists.Controls.Add(_applications, 1, 1);

        _addGroup = new Button
        {
            Text = _text[StringId.GroupsNew],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 0, 4, 4),
        };
        _addGroup.Click += (_, __) => OnAddGroup();

        _removeGroup = new Button
        {
            Text = _text[StringId.GroupsRemove],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 12, 4),
        };
        _removeGroup.Click += (_, __) => OnRemoveGroup();

        _groupName = new TextBox
        {
            Width = TextRenderer.MeasureText("A reasonably long group name", Font).Width,
            Margin = new Padding(0, 2, 12, 4),
        };
        _groupName.Leave += (_, __) => OnGroupNameChanged();

        // Owner drawn, so each entry carries the colour it stands for beside its name. A colour is
        // what the user is choosing, and no wording of it is as clear as the colour itself.
        _groupAccent = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = row + 2,
            Margin = new Padding(0, 2, 4, 4),
        };
        _groupAccent.Items.Add(_text[StringId.AccentAutomatic]);
        for (int i = 0; i < AppSettings.AccentCount; i++)
            _groupAccent.Items.Add(_text.AccentName(i));

        int widest = 0;
        foreach (object item in _groupAccent.Items)
            widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), Font).Width);

        // The name, the swatch in front of it, and the drop-down arrow after it.
        _groupAccent.Width = widest + row * 4;
        _groupAccent.DrawItem += OnDrawAccent;
        _groupAccent.SelectedIndexChanged += (_, __) => OnAccentChanged();

        var editRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 0, 4, 4),
        };
        editRow.Controls.Add(_addGroup);
        editRow.Controls.Add(_removeGroup);
        editRow.Controls.Add(Caption(_text[StringId.GroupsName]));
        editRow.Controls.Add(_groupName);
        editRow.Controls.Add(Caption(_text[StringId.GroupsColour]));
        editRow.Controls.Add(_groupAccent);

        var detailContent = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        detailContent.Controls.Add(lists);
        detailContent.Controls.Add(editRow);

        // Enabled rather than hidden: hiding it would resize the dialog under the pointer every
        // time the box above is ticked.
        _groupDetail = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        _groupDetail.Controls.Add(detailContent);

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        content.Controls.Add(_groupByApplication);
        content.Controls.Add(explanation);
        content.Controls.Add(_groupDetail);

        var box = new GroupBox
        {
            Text = _text[StringId.GroupsGroup],
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 6, 12, 6),
        };
        box.Controls.Add(content);
        return box;
    }

    private static Label Caption(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 8, 2),
        };
    }

    private void LoadFromSettings()
    {
        // Guarded, so setting the initial state does not count as a user change.
        _loading = true;
        _language.SelectedIndex = LanguageIndex();
        _standard.Checked = _settings.BarHeight == BarHeightMode.Standard;
        _compact.Checked = _settings.BarHeight == BarHeightMode.Compact;
        _followWindows.Checked = _settings.Colours == ColourMode.FollowWindows;
        _light.Checked = _settings.Colours == ColourMode.Light;
        _dark.Checked = _settings.Colours == ColourMode.Dark;
        _groupByApplication.Checked = _settings.GroupByApplication;
        _groupDetail.Enabled = _settings.GroupByApplication;
        _loading = false;

        ReloadGroups(_groups.SelectedIndex);
    }

    /// <summary>
    /// Which entry of the language list the settings name. Zero is automatic, which is what an
    /// empty setting and a language that is no longer available both come to.
    /// </summary>
    private int LanguageIndex()
    {
        for (int i = 0; i < Languages.All.Count; i++)
        {
            if (string.Equals(Languages.All[i].Code, _settings.Language,
                    StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }

        return 0;
    }

    private void OnLanguageChanged()
    {
        if (_loading) return;

        string language = _language.SelectedIndex <= 0
            ? Languages.Automatic
            : Languages.All[_language.SelectedIndex - 1].Code;

        if (string.Equals(language, _settings.Language, StringComparison.Ordinal)) return;

        _settings.Language = language;

        // The bar first, so that asking for the text again answers in the new language.
        _onChanged();

        // Rebuilding disposes the box this call came from, so it waits until the change has
        // finished being handled, as the tick in the application list does.
        BeginInvoke(new Action(Rebuild));
    }

    private void OnHeightChanged()
    {
        if (_loading) return;

        // CheckedChanged fires twice per click, once for the button being cleared and once for
        // the one being set. Comparing against the stored value keeps this to a single update.
        BarHeightMode mode = _compact.Checked ? BarHeightMode.Compact : BarHeightMode.Standard;
        if (mode == _settings.BarHeight) return;

        _settings.BarHeight = mode;
        _onChanged();
    }

    private void OnColoursChanged()
    {
        if (_loading) return;

        // As with the height above: CheckedChanged fires for the button being cleared as well as
        // the one being set, so the stored value decides whether anything happened.
        ColourMode mode = _light.Checked ? ColourMode.Light
            : _dark.Checked ? ColourMode.Dark
            : ColourMode.FollowWindows;
        if (mode == _settings.Colours) return;

        _settings.Colours = mode;
        _onChanged();
    }

    // ---------------------------------------------------------------
    // Groups
    // ---------------------------------------------------------------

    /// <summary>
    /// Redraws both lists from the settings and reports the change.
    /// </summary>
    /// <remarks>
    /// Always from the settings rather than from what was just typed. Applying a change
    /// normalizes it - a name that was taken gets a number, an executable claimed twice is left
    /// in the first group - and the lists have to show what was kept, not what was asked for.
    /// </remarks>
    private void Apply(int selection)
    {
        _onChanged();
        ReloadGroups(selection);
    }

    private void ReloadGroups(int selection)
    {
        _loading = true;
        try
        {
            _groups.Items.Clear();
            foreach (AppGroup group in _settings.Groups) _groups.Items.Add(group.Name);

            if (_groups.Items.Count > 0)
            {
                if (selection < 0) selection = 0;
                if (selection >= _groups.Items.Count) selection = _groups.Items.Count - 1;
                _groups.SelectedIndex = selection;
            }
        }
        finally
        {
            _loading = false;
        }

        ReloadApplications();
    }

    /// <summary>
    /// Fills the application list with everything that has a window open, plus anything already
    /// named by a group, ticking the ones in the selected group.
    /// </summary>
    /// <remarks>
    /// An application named by a group but not running is still shown. Dropping it would leave
    /// the user unable to see, let alone undo, a choice they made when it was open.
    ///
    /// The list is emptied and filled again on every change, which sends it back to the top. The
    /// first line the user can see is put back afterwards: a tick halfway down a long list is
    /// followed by another one near it, and a list that jumped to the top each time would have to
    /// be scrolled back before every tick.
    /// </remarks>
    private void ReloadApplications()
    {
        AppGroup selected = SelectedGroup();
        int firstVisible = _applications.TopIndex;
        int highlighted = _applications.SelectedIndex;

        var names = new List<string>(_runningApplications() ?? new List<string>());
        var seen = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        foreach (AppGroup group in _settings.Groups)
        {
            foreach (string executable in group.Executables)
            {
                if (seen.Add(executable)) names.Add(executable);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);

        _loading = true;
        try
        {
            _applications.Items.Clear();
            foreach (string name in names)
            {
                bool inGroup = selected != null
                               && selected.Executables.Contains(name, StringComparer.OrdinalIgnoreCase);
                _applications.Items.Add(name, inGroup);
            }

            _applications.Enabled = selected != null;
            _removeGroup.Enabled = selected != null;
            _groupName.Enabled = selected != null;
            _groupAccent.Enabled = selected != null;

            _groupName.Text = selected?.Name ?? string.Empty;
            _groupAccent.SelectedIndex = selected == null || selected.Accent < 0
                ? 0
                : selected.Accent + 1;

            // The list can be a line shorter or longer than it was, so both are clamped to what
            // it now holds. Highlighting first, because that scrolls of its own accord and would
            // otherwise undo the line put back below it.
            if (highlighted >= 0 && highlighted < _applications.Items.Count)
                _applications.SelectedIndex = highlighted;

            if (firstVisible > 0 && _applications.Items.Count > 0)
                _applications.TopIndex = Math.Min(firstVisible, _applications.Items.Count - 1);
        }
        finally
        {
            _loading = false;
        }
    }

    private AppGroup SelectedGroup()
    {
        int index = _groups.SelectedIndex;
        return index >= 0 && index < _settings.Groups.Count ? _settings.Groups[index] : null;
    }

    private void OnGroupingToggled()
    {
        if (_loading) return;

        _settings.GroupByApplication = _groupByApplication.Checked;
        _groupDetail.Enabled = _groupByApplication.Checked;
        _onChanged();
    }

    private void OnGroupSelected()
    {
        if (_loading) return;

        ReloadApplications();
    }

    private void OnAddGroup()
    {
        _settings.Groups.Add(new AppGroup { Name = UnusedName() });

        Apply(_settings.Groups.Count - 1);
        _groupName.Focus();
    }

    private string UnusedName()
    {
        for (int n = 1; ; n++)
        {
            string candidate = _text.Format(StringId.GroupsDefaultName, n);
            bool taken = false;

            foreach (AppGroup group in _settings.Groups)
            {
                if (string.Equals(group.Name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    taken = true;
                    break;
                }
            }

            if (!taken) return candidate;
        }
    }

    private void OnRemoveGroup()
    {
        int index = _groups.SelectedIndex;
        if (index < 0 || index >= _settings.Groups.Count) return;

        _settings.Groups.RemoveAt(index);
        Apply(index);
    }

    private void OnApplicationChecked(object sender, ItemCheckEventArgs e)
    {
        if (_loading) return;

        AppGroup group = SelectedGroup();
        if (group == null) return;

        string name = TabGrouping.KeyFor(_applications.Items[e.Index].ToString());
        if (name.Length == 0) return;

        if (e.NewValue == CheckState.Checked)
        {
            // Only one group may claim an application, so it leaves whichever group has it.
            foreach (AppGroup other in _settings.Groups)
            {
                if (!ReferenceEquals(other, group))
                    other.Executables.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            }

            if (!group.Executables.Contains(name, StringComparer.OrdinalIgnoreCase))
                group.Executables.Add(name);
        }
        else
        {
            group.Executables.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }

        // ItemCheck runs before the tick is drawn, so the reload has to wait for it to land.
        int selection = _groups.SelectedIndex;
        BeginInvoke(new Action(() => Apply(selection)));
    }

    private void OnGroupNameChanged()
    {
        if (_loading) return;

        AppGroup group = SelectedGroup();
        if (group == null) return;

        string name = _groupName.Text.Trim();
        if (name.Length == 0 || string.Equals(name, group.Name, StringComparison.Ordinal))
        {
            _groupName.Text = group.Name;
            return;
        }

        group.Name = name;
        Apply(_groups.SelectedIndex);
    }

    private void OnAccentChanged()
    {
        if (_loading) return;

        AppGroup group = SelectedGroup();
        if (group == null) return;

        int accent = _groupAccent.SelectedIndex - 1;   // the first entry is Automatic
        if (accent == group.Accent) return;

        group.Accent = accent;
        Apply(_groups.SelectedIndex);
    }

    /// <summary>
    /// Draws one entry of the colour list: a square of the colour, then its name.
    /// </summary>
    /// <remarks>
    /// The square is the light shade of the accent whatever the bar is set to; see
    /// <see cref="AccentPalette"/> for why. The bar draws the dark shade of the same hue when it
    /// is dark, and one name covers both.
    ///
    /// The first entry is Automatic, which stands for no colour and so is drawn without a square.
    /// The space is still left in front of its name, so the names line up.
    /// </remarks>
    private void OnDrawAccent(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _groupAccent.Items.Count) return;

        Font font = e.Font ?? this.Font;
        int size = Math.Max(6, Math.Min(e.Bounds.Height - 4, font.Height));
        var swatch = new Rectangle(
            e.Bounds.Left + 3, e.Bounds.Top + ((e.Bounds.Height - size) / 2), size, size);

        if (e.Index > 0)
        {
            using (var brush = new SolidBrush(AccentPalette.Swatch(e.Index - 1)))
                e.Graphics.FillRectangle(brush, swatch);

            // An outline, so a pale accent is still a square rather than a gap in the row.
            using var pen = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawRectangle(pen, swatch);
        }

        // DrawItem is raised with the ordinary colours even when the box is disabled, which it is
        // until a group is selected, so the grey has to be chosen here.
        Color text = _groupAccent.Enabled ? e.ForeColor : SystemColors.GrayText;
        var name = new Rectangle(
            swatch.Right + 4, e.Bounds.Top,
            Math.Max(0, e.Bounds.Right - swatch.Right - 6), e.Bounds.Height);

        TextRenderer.DrawText(e.Graphics, _groupAccent.Items[e.Index].ToString(), font, name, text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        e.DrawFocusRectangle();
    }
}
