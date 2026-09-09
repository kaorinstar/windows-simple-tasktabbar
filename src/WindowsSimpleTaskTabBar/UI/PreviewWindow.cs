using WindowsSimpleTaskTabBar.Core.Preview;
using WindowsSimpleTaskTabBar.Interop;

namespace WindowsSimpleTaskTabBar.UI;

/// <summary>
/// The window a preview of another window is drawn in.
/// </summary>
/// <remarks>
/// It draws nothing itself. The desktop compositor is asked to draw the source window inside
/// this one, so what appears is the window as it is now rather than a picture taken earlier.
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

    /// <summary>How much of the window is left showing around the picture, as a border.</summary>
    private readonly int _border;

    /// <param name="border">Thickness of the border, in device pixels.</param>
    /// <param name="borderColour">
    /// What the border is drawn in. It is the window's own background: the picture covers
    /// everything but this margin, so nothing has to be painted for it to show.
    /// </param>
    public PreviewWindow(int border, Color borderColour)
    {
        _border = border < 0 ? 0 : border;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;

        // Off, because every size this window is given is already in device pixels: the caller
        // scaled them. Left on, Windows Forms would scale them a second time.
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = borderColour;
    }

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
    /// window is, which is the shape the preview has to be drawn in.
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
    /// Puts the window where <paramref name="box"/> says, shows it, and points the picture at
    /// the space inside the border.
    /// </summary>
    /// <remarks>
    /// The destination is in this window's own coordinates rather than the screen's, which is
    /// why the border is added to the size here and taken off again for the picture.
    /// </remarks>
    public void Present(PreviewBox box)
    {
        if (_thumbnail == IntPtr.Zero) return;

        Bounds = new Rectangle(box.X - _border, box.Y - _border,
                               box.Width + _border * 2, box.Height + _border * 2);

        if (!Visible) Show();

        var properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION
                      | NativeMethods.DWM_TNP_VISIBLE
                      | NativeMethods.DWM_TNP_OPACITY
                      | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new NativeMethods.RECT
            {
                left = _border,
                top = _border,
                right = _border + box.Width,
                bottom = _border + box.Height,
            },
            opacity = 255,
            fVisible = true,

            // The whole window, frame included. The client area alone would drop the title bar,
            // which is often the only thing telling two windows of one application apart.
            fSourceClientAreaOnly = false,
        };

        NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, ref properties);
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
        base.Dispose(disposing);
    }
}
