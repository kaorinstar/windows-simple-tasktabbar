using System.Diagnostics.CodeAnalysis;
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
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private bool _loading;

    // Both are added to a Controls collection in BuildControls, and a control is disposed by
    // whatever it was added to. CA2213 cannot see that, so the ownership is stated here.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _standard;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the Controls collection it is added to.")]
    private RadioButton _compact;

    /// <param name="settings">The live settings object, edited in place.</param>
    /// <param name="onChanged">Called after every change, to apply and save it.</param>
    public SettingsForm(AppSettings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;

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

        var heightChoices = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8),
        };
        heightChoices.Controls.Add(_standard);
        heightChoices.Controls.Add(_compact);

        var heightGroup = new GroupBox
        {
            Text = "Bar height",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 12, 12, 6),
        };
        heightGroup.Controls.Add(heightChoices);

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

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        root.Controls.Add(heightGroup);
        root.Controls.Add(note);
        root.Controls.Add(close);

        Controls.Add(root);

        AcceptButton = close;
        CancelButton = close;
    }

    private void LoadFromSettings()
    {
        // Guarded, so setting the initial state does not count as a user change.
        _loading = true;
        _standard.Checked = _settings.BarHeight == BarHeightMode.Standard;
        _compact.Checked = _settings.BarHeight == BarHeightMode.Compact;
        _loading = false;
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
}
