using VRageMath;

namespace ClientPlugin.DoomHost;

/// <summary>
/// Where everything sits on the Doom screen. All sizes are shares of the screen height, so the screen looks the
/// same at 1080p, 1440p and 4K and on any shape. Only the backdrop art depends on the shape.
/// </summary>
public readonly struct DoomLayout
{
    private const float ViewHeight = 0.74f;     // Doom's picture, as a share of the screen height.
    private const float ViewTop = 0.125f;
    private const float Pad = 0.012f;           // Between the picture and its frame.
    private const float PanelGap = 0.025f;
    private const float PanelMinWidth = 0.22f;

    public readonly Rectangle Screen;
    public readonly float Unit;                 // One screen height in pixels.
    public readonly Rectangle View;             // Doom's picture, always 4:3.
    public readonly Rectangle Frame;            // Around the picture.
    public readonly Rectangle Title;            // Title bar above the picture.
    public readonly Rectangle Close;            // Close button, inside the title bar.
    public readonly Rectangle Footer;           // Controls bar below the picture.
    public readonly Rectangle Left, Right;      // Side panels; empty when there is no room.
    public readonly Rectangle VolumeDown, VolumeUp, VolumeBar; // In the right panel; empty without panels.
    public readonly string BackdropName;

    public bool HasPanels => Left.Width > 0;

    public DoomLayout(Rectangle screen)
    {
        Screen = screen;
        var h = (float)screen.Height;
        Unit = h;

        var vh = ViewHeight * h;
        var vw = vh * 4f / 3f;
        var left = screen.X + (screen.Width - vw) / 2f;
        var top = screen.Y + ViewTop * h;
        View = new Rectangle((int)left, (int)top, (int)vw, (int)vh);

        var pad = Pad * h;
        Frame = Inflate(View, pad);

        Title = new Rectangle(Frame.X, (int)(screen.Y + 0.035f * h), Frame.Width, (int)(0.062f * h));
        var bh = 0.046f * h;
        var bw = bh * 256f / 96f;
        Close = new Rectangle(Title.Right - (int)bw - (int)(0.008f * h), Title.Y + (int)((Title.Height - bh) / 2f), (int)bw, (int)bh);
        Footer = new Rectangle(Frame.X, Frame.Bottom + (int)(0.014f * h), Frame.Width, (int)(0.05f * h));

        var panelW = Frame.X - screen.X - (int)((PanelGap + 0.03f) * h);
        if (panelW >= PanelMinWidth * h)
        {
            var y = Title.Y;
            var ph = Footer.Bottom - Title.Y;
            Left = new Rectangle(Frame.X - (int)(PanelGap * h) - panelW, y, panelW, ph);
            Right = new Rectangle(Frame.Right + (int)(PanelGap * h), y, panelW, ph);

            // Volume: [-] [=====     ] [+] near the bottom of the right panel.
            var vpad = (int)(0.016f * h);
            var bs = (int)(0.04f * h);
            var by = Right.Bottom - vpad - bs;
            VolumeDown = new Rectangle(Right.X + vpad, by, bs, bs);
            VolumeUp = new Rectangle(Right.Right - vpad - bs, by, bs, bs);
            var gap = (int)(0.01f * h);
            VolumeBar = new Rectangle(VolumeDown.Right + gap, by + bs / 3, VolumeUp.X - gap - VolumeDown.Right - gap, bs / 3);
        }
        else
        {
            Left = Right = new Rectangle(0, 0, 0, 0);
            VolumeDown = VolumeUp = VolumeBar = new Rectangle(0, 0, 0, 0);
        }

        // One 1440p backdrop per shape serves every screen height: the game scales it up for 4K and down for 1080p.
        BackdropName = "DoomBg" + AspectName(screen.Width / h) + "_1440";
    }

    /// <summary>The names match make_assets.py.</summary>
    public static string AspectName(float aspect) => aspect < 1.45f ? "4x3" : aspect < 1.68f ? "16x10" : aspect < 2f ? "16x9" : "21x9";

    private static Rectangle Inflate(Rectangle r, float by)
    {
        var d = (int)by;
        return new Rectangle(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);
    }
}
