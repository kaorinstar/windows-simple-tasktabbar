using WindowsSimpleTaskTabBar.Core.Preview;
using WindowsSimpleTaskTabBar.Interop;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The window a preview of another window is drawn in: a live picture, with the window's icon
/// and title under it.
/// </summary>
/// <remarks>
/// The picture is not drawn here. The desktop compositor is asked to draw the source window
/// inside this one, so what appears is the window as it is now rather than a picture taken
/// earlier. The icon and the title underneath are this window's own painting.
///
/// <b>The title has to be here rather than in a tooltip beside it.</b> Two answers arriving at
/// once - a picture above and a tooltip below - are harder to read than either alone, and the
/// picture cannot carry the title on its own: a window scaled to fit in 280 pixels draws its
/// title bar text at about two pixels. One panel answers both questions, which is what the
/// Windows taskbar does with its own thumbnails.
///
/// <b>It must never take the foreground.</b> The bar activates itself when it is clicked, and
/// the three-step activation in <c>WindowService</c> depends on that being the bar rather than
/// anything else this application owns. <c>WS_EX_NOACTIVATE</c> and
/// <see cref="ShowWithoutActivation"/> are what keep it out of the way, and
/// <c>WS_EX_TOOLWINDOW</c> keeps it out of Alt+Tab as well.
///
/// One instance is kept for the life of the bar rather than one per preview. Creating a window
/// costs more than showing one, and the pointer crossing a row of tabs would create and destroy
/// one for each.
/// </remarks>
internal sealed class PreviewWindow : Form
{
    /// <summary>The registration with the compositor, or zero when there is none.</summary>
    private IntPtr _thumbnail;

    /// <summary>Width of the frame drawn around the whole panel, in device pixels.</summary>
    private readonly int _border;

    private readonly Color _frameColour;
    private readonly Color _titleColour;

    // This window's own, disposed with it. The bar's font is not borrowed: a font handed over
    // would have two owners, and the bar rebuilds its own whenever the height or language
    // changes.
    private readonly Font _font;

    // The full title, for a title too long to be drawn whole. Nothing is shown for one that
    // fits: a tooltip repeating what is already on screen is in the way rather than of use.
    private readonly ToolTip _toolTip = new();
    private string _toolTipText = string.Empty;

    /// <summary>Where the picture goes, in this window's own coordinates.</summary>
    private Rectangle _pictureArea;

    /// <summary>Where the icon and the title go, under the picture.</summary>
    private Rectangle _titleArea;

    // Worked out when the preview is presented rather than while it is painted: whether the
    // title fits decides whether the tooltip has anything to add, and the pointer can reach the
    // panel before the first paint of it has run.
    private Rectangle _titleIconArea;
    private Rectangle _titleTextArea;

    private string _title = string.Empty;

    // Borrowed from the bar's icon cache for as long as one preview is up. Never disposed here.
    private Icon _icon;

    /// <summary>Whether the title had to be cut short to fit.</summary>
    private bool _titleClipped;

    /// <summary>Raised when the pointer leaves the panel, so the bar can take it down.</summary>
    public event EventHandler PointerLeft;

    /// <param name="border">Width of the frame, in device pixels.</param>
    /// <param name="background">Behind the icon and the title.</param>
    /// <param name="frame">The line drawn around the panel.</param>
    /// <param name="title">The title text.</param>
    /// <param name="fontFamily">Font family for the title, which the language decides.</param>
    /// <param name="fontPixels">Font size in pixels, not points.</param>
    public PreviewWindow(int border, Color background, Color frame, Color title,
                         string fontFamily, int fontPixels)
    {
        _border = border < 1 ? 1 : border;
        _frameColour = frame;
        _titleColour = title;
        _font = UiFonts.Create(fontFamily, fontPixels, GraphicsUnit.Pixel);

        // Room for one line of text, with as much again around it.
        TitleHeight = TextRenderer.MeasureText("Ag", _font).Height + _border * 4;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;

        // Off, because every size this window is given is already in device pixels: the caller
        // scaled them. Left on, Windows Forms would scale them a second time.
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = background;
        DoubleBuffered = true;

        // The bar is usually not the active window, and neither is this.
        _toolTip.ShowAlways = true;
        _toolTip.InitialDelay = 400;
        _toolTip.ReshowDelay = 200;
        _toolTip.AutoPopDelay = 10000;
    }

    /// <summary>
    /// How much room the icon and title take under the picture, in device pixels.
    /// </summary>
    /// <remarks>
    /// Read before the size of the picture is decided: it comes out of the same room above the
    /// bar, so the picture has to be measured against what is left after this.
    /// </remarks>
    public int TitleHeight { get; }

    /// <summary>Keeps <see cref="Form.Show()"/> from taking the foreground.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= (int)NativeMethods.WS_EX_NOACTIVATE;
            cp.ExStyle |= (int)NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>
    /// Asks the compositor for a picture of <paramref name="source"/>, and for the size that
    /// window is, which is the shape the picture has to be drawn in.
    /// </summary>
    /// <remarks>
    /// Registering is what makes the size readable, so the two happen together. A registration
    /// that answers no size is released here rather than left for the caller to remember.
    ///
    /// A minimized window is registered like any other. The compositor is not drawing one, so
    /// there may be no picture to show, but the icon painted behind it is what the panel falls
    /// back to and asking costs nothing.
    /// </remarks>
    /// <returns>False when there is nothing to show, in which case nothing is registered.</returns>
    public bool Register(IntPtr source, out int sourceWidth, out int sourceHeight)
    {
        sourceWidth = 0;
        sourceHeight = 0;

        Unregister();
        if (source == IntPtr.Zero) return false;

        // Handle first: the compositor draws into a window, so there has to be one to name.
        if (NativeMethods.DwmRegisterThumbnail(Handle, source, out IntPtr thumbnail) != 0)
            return false;

        _thumbnail = thumbnail;

        if (NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out NativeMethods.SIZE size) != 0
            || size.cx <= 0 || size.cy <= 0)
        {
            Unregister();
            return false;
        }

        sourceWidth = size.cx;
        sourceHeight = size.cy;
        return true;
    }

    /// <summary>
    /// Puts the panel where <paramref name="content"/> says, shows it, and points the picture at
    /// the space above the title.
    /// </summary>
    /// <param name="content">
    /// The picture and the title together, without the frame. Its height therefore already
    /// includes <see cref="TitleHeight"/>.
    /// </param>
    /// <param name="title">The window's title, drawn under the picture.</param>
    /// <param name="icon">
    /// The window's icon, from the bar's cache. Drawn beside the title, and behind the picture
    /// so that a window with no picture to show still says which application it belongs to.
    /// </param>
    public void Present(PreviewBox content, string title, Icon icon)
    {
        if (_thumbnail == IntPtr.Zero) return;

        int pictureHeight = content.Height - TitleHeight;
        if (pictureHeight < 1) return;

        _title = title ?? string.Empty;
        _icon = icon;
        _pictureArea = new Rectangle(_border, _border, content.Width, pictureHeight);
        _titleArea = new Rectangle(_border, _pictureArea.Bottom, content.Width, TitleHeight);
        LayoutTitle();

        Bounds = new Rectangle(content.X - _border, content.Y - _border,
                               content.Width + _border * 2, content.Height + _border * 2);

        if (!Visible) Show();
        Invalidate();

        var properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION
                      | NativeMethods.DWM_TNP_VISIBLE
                      | NativeMethods.DWM_TNP_OPACITY
                      | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new NativeMethods.RECT
            {
                left = _pictureArea.Left,
                top = _pictureArea.Top,
                right = _pictureArea.Right,
                bottom = _pictureArea.Bottom,
            },
            opacity = 255,
            fVisible = true,

            // The whole window, frame included. Not for the title bar's text, which is far too
            // small to read at this size - the title below says that. For its shape and colour,
            // which is part of how a window is recognised at a glance.
            fSourceClientAreaOnly = false,
        };

        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref properties);
    }

    /// <summary>
    /// Draws the frame, the icon standing in for the picture, and the icon and title below it.
    /// </summary>
    /// <remarks>
    /// The picture itself is not drawn here. The compositor puts it over the area this leaves
    /// for it, after this method has run, which is what makes the icon in that area a fallback:
    /// a window the compositor is drawing covers it, and one it is not - a minimized window -
    /// leaves it showing.
    /// </remarks>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        DrawStandIn(e.Graphics);
        DrawTitle(e.Graphics);

        using var pen = new Pen(_frameColour, _border);

        // Inset by half the pen, which is drawn centred on the line it is given.
        int inset = _border / 2;
        Rectangle frame = Rectangle.FromLTRB(inset, inset,
            ClientSize.Width - inset - 1, ClientSize.Height - inset - 1);

        if (frame.Width > 0 && frame.Height > 0) e.Graphics.DrawRectangle(pen, frame);
    }

    /// <summary>The icon, centred where the picture goes, at the size it actually is.</summary>
    /// <remarks>
    /// Its own size rather than filled to the area: the icon a window answers with is usually
    /// 16 pixels across, and stretched to fill a preview it is a blur. Small and sharp says
    /// which application this is; large and blurred says it less well.
    /// </remarks>
    private void DrawStandIn(Graphics g)
    {
        if (_icon == null || _pictureArea.Width <= 0 || _pictureArea.Height <= 0) return;

        int width = _icon.Width;
        int height = _icon.Height;
        if (width > _pictureArea.Width) width = _pictureArea.Width;
        if (height > _pictureArea.Height) height = _pictureArea.Height;

        g.DrawIcon(_icon, new Rectangle(
            _pictureArea.Left + (_pictureArea.Width - width) / 2,
            _pictureArea.Top + (_pictureArea.Height - height) / 2,
            width, height));
    }

    /// <summary>
    /// Where the icon and the title sit under the picture: from the left, the icon first, the
    /// way the taskbar lays out its own.
    /// </summary>
    private void LayoutTitle()
    {
        _titleIconArea = Rectangle.Empty;
        _titleTextArea = Rectangle.Empty;
        _titleClipped = false;

        if (_titleArea.Width <= 0 || _title.Length == 0) return;

        int padding = _border * 2;
        int side = _titleArea.Height - padding * 2;
        int left = _titleArea.Left + padding;

        if (_icon != null && side > 0)
        {
            _titleIconArea = new Rectangle(left, _titleArea.Top + padding, side, side);
            left += side + padding;
        }

        _titleTextArea = Rectangle.FromLTRB(left, _titleArea.Top,
                                            _titleArea.Right - padding, _titleArea.Bottom);
        if (_titleTextArea.Width <= 0) return;

        _titleClipped = TextRenderer.MeasureText(_title, _font).Width > _titleTextArea.Width;
    }

    private void DrawTitle(Graphics g)
    {
        if (!_titleIconArea.IsEmpty) g.DrawIcon(_icon, _titleIconArea);
        if (_titleTextArea.Width <= 0) return;

        TextRenderer.DrawText(g, _title, _font, _titleTextArea, _titleColour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// Shows the whole title while the pointer rests on the clipped one.
    /// </summary>
    /// <remarks>
    /// Only over the title, and only when it was cut short. Over the picture it would cover the
    /// thing the pointer came here to look at, and a tooltip repeating a title already shown in
    /// full is in the way rather than of use.
    /// </remarks>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        string wanted = _titleClipped && _titleArea.Contains(e.Location)
            ? _title
            : string.Empty;

        // Setting the same text again restarts the tooltip and makes it flicker.
        if (wanted == _toolTipText) return;

        _toolTipText = wanted;
        _toolTip.SetToolTip(this, wanted);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        PointerLeft?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes the preview off the screen and releases the registration behind it.
    /// </summary>
    /// <remarks>
    /// Hiding alone would leave the compositor drawing a window nobody is looking at, so the two
    /// always go together. Safe to call when nothing is showing.
    /// </remarks>
    public void HidePreview()
    {
        Unregister();

        _icon = null;
        _title = string.Empty;
        LayoutTitle();

        if (_toolTipText.Length != 0)
        {
            _toolTipText = string.Empty;
            _toolTip.SetToolTip(this, string.Empty);
        }

        if (Visible) Visible = false;
    }

    private void Unregister()
    {
        if (_thumbnail == IntPtr.Zero) return;

        NativeMethods.DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        // Before the base call, which destroys the window the registration names.
        Unregister();

        if (disposing)
        {
            _toolTip.Dispose();
            _font?.Dispose();
        }

        base.Dispose(disposing);
    }
}
