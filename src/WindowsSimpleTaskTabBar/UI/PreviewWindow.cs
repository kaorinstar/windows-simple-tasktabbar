using WindowsSimpleTaskTabBar.Core.Preview;
using WindowsSimpleTaskTabBar.Interop;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The window a preview of another window is drawn in: a live picture, with the window's title
/// under it.
/// </summary>
/// <remarks>
/// The picture is not drawn here. The desktop compositor is asked to draw the source window
/// inside this one, so what appears is the window as it is now rather than a picture taken
/// earlier. The title underneath is this window's own painting.
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

    /// <summary>Where the picture goes, in this window's own coordinates.</summary>
    private Rectangle _pictureArea;

    private string _title = string.Empty;

    /// <param name="border">Width of the frame, in device pixels.</param>
    /// <param name="background">Behind the title, and the frame's own colour underneath.</param>
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
    }

    /// <summary>
    /// How much room the title takes under the picture, in device pixels.
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
    public void Present(PreviewBox content, string title)
    {
        if (_thumbnail == IntPtr.Zero) return;

        _title = title ?? string.Empty;

        int pictureHeight = content.Height - TitleHeight;
        if (pictureHeight < 1) return;

        _pictureArea = new Rectangle(_border, _border, content.Width, pictureHeight);

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
    /// Draws the frame and the title. The picture is not drawn here: the compositor puts it over
    /// the area this leaves for it, after this method has run.
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using (var pen = new Pen(_frameColour, _border))
        {
            // Inset by half the pen, which is drawn centred on the line it is given.
            int inset = _border / 2;
            Rectangle frame = Rectangle.FromLTRB(inset, inset,
                ClientSize.Width - inset - 1, ClientSize.Height - inset - 1);

            if (frame.Width > 0 && frame.Height > 0) e.Graphics.DrawRectangle(pen, frame);
        }

        if (_title.Length == 0 || _pictureArea.Height <= 0) return;

        var titleArea = new Rectangle(_pictureArea.Left, _pictureArea.Bottom,
                                      _pictureArea.Width, TitleHeight);

        TextRenderer.DrawText(e.Graphics, _title, _font, titleArea, _titleColour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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

        if (disposing) _font?.Dispose();

        base.Dispose(disposing);
    }
}
