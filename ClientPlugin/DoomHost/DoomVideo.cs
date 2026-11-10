using ManagedDoom;
using ManagedDoom.Video;

namespace ClientPlugin.DoomHost;

/// <summary>
/// IVideo that renders the Doom software framebuffer as row-major RGBA, ready to upload to a generated game texture.
/// The renderer writes straight into one of a small ring of buffers, so a frame needs a single pass and no copy.
/// </summary>
public sealed class DoomVideo : IVideo
{
    // Frames go to the render thread, which reads them a frame or two after they are handed over. The ring lets the
    // Doom thread keep rendering without touching a buffer that may still be waiting for its upload.
    private const int RingSize = 4;

    private readonly Renderer renderer;
    private readonly byte[][] ring = new byte[RingSize][];
    private int next;

    public int Width => renderer.Width;
    public int Height => renderer.Height;

    /// <summary>Height of the status bar in frame pixels, or 0 when the view fills the whole frame.</summary>
    public int StatusBarHeight => renderer.WindowSize < renderer.MaxWindowSize ? 32 * renderer.Width / 320 : 0;

    public DoomVideo(ManagedDoom.Config config, GameContent content)
    {
        renderer = new Renderer(config, content) { RowMajorOutput = true };
        for (var i = 0; i < ring.Length; i++)
            ring[i] = new byte[4 * renderer.Width * renderer.Height];
    }

    /// <summary>Renders a frame into the next buffer of the ring and returns it.</summary>
    public byte[] RenderFrame(ManagedDoom.Doom doom, Fixed frameFrac)
    {
        var pixels = ring[next];
        next = (next + 1) % ring.Length;
        renderer.Render(doom, pixels, frameFrac);
        return pixels;
    }

    // IVideo: the screen draws the frame itself, so there is nothing to do here.
    public void Render(ManagedDoom.Doom doom, Fixed frameFrac) { }
    public void InitializeWipe() => renderer.InitializeWipe();
    public bool HasFocus() => true;

    public int WipeBandCount => renderer.WipeBandCount;
    public int WipeHeight => renderer.WipeHeight;
    public int MaxWindowSize => renderer.MaxWindowSize;

    public int WindowSize
    {
        get => renderer.WindowSize;
        set => renderer.WindowSize = value;
    }

    public bool DisplayMessage
    {
        get => renderer.DisplayMessage;
        set => renderer.DisplayMessage = value;
    }

    public int MaxGammaCorrectionLevel => renderer.MaxGammaCorrectionLevel;

    public int GammaCorrectionLevel
    {
        get => renderer.GammaCorrectionLevel;
        set => renderer.GammaCorrectionLevel = value;
    }
}
