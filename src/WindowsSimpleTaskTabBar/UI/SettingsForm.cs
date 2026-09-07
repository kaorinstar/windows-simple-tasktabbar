using System.Diagnostics.CodeAnalysis;
using WindowsSimpleTaskTabBar.Core.Grouping;
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
    /// <summary>
    /// The first entry of the colour list. It stands for no colour chosen, which leaves the group
    /// to be given one.
    /// </summary>
    private const string AutomaticAccent = "Automatic";

    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private readonly Func<List<string>> _runningApplications;
    private bool _loading;

    // Every control below is added to a Controls collection in BuildControls, and a control is
    // disposed by whatever it was added to. CA2213 cannot see that, so the ownership is stated
    // here.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _standard;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _compact;

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
    public SettingsForm(AppSettings settings, Action onChanged,
        Func<List<string>> runningApplications)
    {
        _settings = settings;
        _onChanged = onChanged;
        _runningApplications = runningApplications;

        Text = "WindowsSimpleTaskTabBar settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        BuildControls();
        LoadFromSettings();
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
        root.Controls.Add(BuildHeightGroup());
        root.Controls.Add(BuildGroupingGroup());

        var note = new Label
        {
            Text = "Changes apply straight away.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(14, 0, 12, 8),
        };

        var close = new Button
        {
            Text = "Close",
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

    private GroupBox BuildHeightGroup()
    {
        _standard = new RadioButton
        {
            Text = $"Standard ({AppSettings.HeightInPixels(BarHeightMode.Standard)} px)",
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 2),
        };
        _standard.CheckedChanged += (_, __) => OnHeightChanged();

        _compact = new RadioButton
        {
            Text = $"Compact ({AppSettings.HeightInPixels(BarHeightMode.Compact)} px)"
                   + " - gives the height back to your windows",
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
            Text = "Bar height",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 12, 12, 6),
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
            Text = "Group tabs by application",
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 2),
        };
        _groupByApplication.CheckedChanged += (_, __) => OnGroupingToggled();

        var explanation = new Label
        {
            Text = "Windows of one application sit together and share a colour."
                   + " Dragging a tab past another group moves its whole group.",
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
        lists.Controls.Add(Caption("Groups:"), 0, 0);
        lists.Controls.Add(Caption("Applications in the selected group:"), 1, 0);
        lists.Controls.Add(_groups, 0, 1);
        lists.Controls.Add(_applications, 1, 1);

        _addGroup = new Button
        {
            Text = "New group",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 0, 4, 4),
        };
        _addGroup.Click += (_, __) => OnAddGroup();

        _removeGroup = new Button
        {
            Text = "Remove",
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
        _groupAccent.Items.Add(AutomaticAccent);
        for (int i = 0; i < AppSettings.AccentCount; i++)
            _groupAccent.Items.Add(AccentPalette.Name(i));

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
        editRow.Controls.Add(Caption("Name:"));
        editRow.Controls.Add(_groupName);
        editRow.Controls.Add(Caption("Colour:"));
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
            Text = "Tab groups",
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
        _standard.Checked = _settings.BarHeight == BarHeightMode.Standard;
        _compact.Checked = _settings.BarHeight == BarHeightMode.Compact;
        _groupByApplication.Checked = _settings.GroupByApplication;
        _groupDetail.Enabled = _settings.GroupByApplication;
        _loading = false;

        ReloadGroups(_groups.SelectedIndex);
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
    /// </remarks>
    private void ReloadApplications()
    {
        AppGroup selected = SelectedGroup();

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
            string candidate = "Group " + n;
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
    /// The light shade of each accent, because this dialog is drawn in the standard Windows
    /// controls, which stay light whatever the theme is. The bar itself uses the dark shade of
    /// the same hue when the theme is dark, and one name covers both.
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
            using (var brush = new SolidBrush(AccentPalette.Colour(e.Index - 1, light: true)))
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
