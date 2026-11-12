using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ManagedDoom;
using ManagedDoom.Audio;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Audio;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.DoomHost;

/// <summary>
/// Doom as an in-game GUI screen. Doom itself runs on its own thread (<see cref="DoomRunner"/>); this screen forwards
/// the keyboard and mouse to it, uploads its newest frame into a generated game texture and draws it inside a styled
/// frame. The game is saved to a hidden slot every minute and when the screen closes, and is resumed on the next open.
/// </summary>
public sealed partial class DoomScreen : MyGuiScreenBase
{
    private const string FrameTexture = "DoomForSeFrame";
    private const string Blank = "Textures\\GUI\\Blank.dds";
    private const string Font = "White";
    private const int AutosaveSlot = DoomRunner.AutosaveSlot;

    private static readonly Color Accent = new(255, 98, 40);
    private static readonly Color Bright = new(255, 210, 150);
    private static readonly Color Dim = new(190, 160, 150);
    private static readonly Color Panel = new(18, 8, 8, 215);
    private static readonly Color Line = new(150, 48, 28);

    public static DoomScreen Current { get; private set; }

    private readonly ManagedDoom.Config config;
    private readonly DoomVideo video;
    private readonly DoomInput input;
    private readonly DoomAudio audio;
    private readonly DoomRunner runner;
    private readonly string wadName;
    private GameContent gameContent;
    private bool shutDown;

    // The game's own music is silenced while Doom is open, and put back when it closes.
    private bool musicSilenced;
    private float savedMusicVolume;
    private bool textureCreated;
    private bool closing;
    private int uploadedFrame;

    // Gameplay extras drawn by the screen: zoom and hit markers.
    private const float ZoomFactor = 1.6f;
    private const float ZoomSeconds = 0.12f;
    private const float MarkerSeconds = 0.25f;
    private readonly System.Diagnostics.Stopwatch extrasClock = System.Diagnostics.Stopwatch.StartNew();
    private bool zoomHeld;
    private float zoom;                       // 0 = normal, 1 = fully zoomed
    private float lastExtrasSeconds;
    private int seenHits, seenKills;
    private float lastHitSeconds = -10f;
    private bool lastWasKill;
    private bool hitMarkers;
    private bool closeHover;
    private bool downHover, upHover;

    // Strings and measurements that only change now and then are built once and kept until their inputs change.
    private readonly string wadTitle;
    private string statusBar;
    private (bool, int, int, int) statusBarKey;
    private (int, int, int) controlsKey;
    private (string Key, string Action)[] controlRows;
    private (int, int) statusKey;
    private string statusNote;
    private string[] statusLinesGrabbed, statusLinesFree;
    private static readonly Dictionary<(string, float), Vector2> TextSizes = new();
    private static Rectangle textSizesScreen;
    private static readonly string[] NoLines = new string[0];

    private static string DoomConfigPath => Path.Combine(ConfigUtilities.GetExeDirectory(), "managed-doom.cfg");

    // The WAD whose DeHackEd lump patched Doom's static tables in this session, if any.
    private static bool tablesPatched;
    private static string patchedKey;

    // Things to do once the screen is gone: open Doom again with a new game, and tell the player what happened.
    private static bool reopenFresh;
    private static string pendingMessage;

    private static string WadKey(string wadPath) =>
        Path.GetFileNameWithoutExtension(wadPath).ToLowerInvariant() + "-" + new FileInfo(wadPath).Length.ToString("x");

    private static string AutosavePath(string wadKey) =>
        Path.Combine(ConfigUtilities.GetExeDirectory(), "doomsav" + AutosaveSlot + "-" + wadKey + ".dsg");

    /// <summary>A finished save ends with Doom's 0x1d marker; a cut-off file does not.</summary>
    private static bool SaveLooksComplete(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 64)
                return false;
            stream.Seek(-1, SeekOrigin.End);
            return stream.ReadByte() == 0x1d;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Renames a save that cannot be used, so it is not tried again but is not lost either.</summary>
    private static void SetSaveAside(string path)
    {
        try
        {
            var bad = Path.ChangeExtension(path, ".bad");
            File.Delete(bad);
            File.Move(path, bad);
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("DoomForSe: could not set the bad save aside: " + e.Message);
            try { File.Delete(path); } catch (Exception) { /* nothing more to do */ }
        }
    }

    private void SaveDoomConfig() => config.Save(DoomConfigPath);

    /// <summary>
    /// Called once per game update by the plugin: does what the last screen left to do (a new game after a bad
    /// save, or a message about why Doom stopped).
    /// </summary>
    public static void RunPending()
    {
        if (Current != null || (!reopenFresh && pendingMessage == null))
            return;

        var open = reopenFresh;
        var message = pendingMessage;
        reopenFresh = false;
        pendingMessage = null;

        if (open)
            Toggle();
        if (message != null)
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
                messageText: new StringBuilder(message),
                messageCaption: new StringBuilder("Doom")));
    }

    /// <summary>Opens the screen, or closes it if it is already open.</summary>
    public static void Toggle()
    {
        if (Current != null)
        {
            Current.CloseScreen();
            return;
        }

        try
        {
            MyGuiSandbox.AddScreen(new DoomScreen());
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("DoomForSe: could not start Doom: " + e);
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
                messageText: new StringBuilder(e.Message),
                messageCaption: new StringBuilder("Doom")));
        }
    }

    private DoomScreen() : base(new Vector2(0.5f, 0.5f), new Vector4(0f, 0f, 0f, 1f), new Vector2(1f, 1f))
    {
        m_closeOnEsc = false; // Escape belongs to Doom's menu.
        CanBeHidden = false;
        m_drawEvenWithoutFocus = false;

        // Doom's own options (volumes, mouse sensitivity, screen size, gamma) come back from the last session.
        config = File.Exists(DoomConfigPath) ? new ManagedDoom.Config(DoomConfigPath) : new ManagedDoom.Config();
        config.video_highresolution = true;
        config.video_scale = Config.Current.ResolutionScale(MyGuiManager.GetFullscreenRectangle().Height);
        config.video_fpsscale = 1;
        config.game_alwaysrun = true;
        config.mouse_disableyaxis = true;

        // The autosave belongs to the WAD it was made with, so switching WAD never loads the wrong save.
        var wadPath = ConfigUtilities.GetWadPaths(new CommandLineArgs(new string[0]))[0];
        var wadKey = WadKey(wadPath);
        var savePath = AutosavePath(wadKey);

        // Doom's tables are patched by a WAD's DeHackEd lump and cannot be put back, so a different WAD needs a restart.
        if (tablesPatched && wadKey != patchedKey)
            throw new InvalidOperationException("Doom's data was changed by the WAD used earlier in this session. Restart Space Engineers to use a different WAD.");

        // A missing WAD throws here, and Toggle reports it.
        var resume = File.Exists(savePath) && SaveLooksComplete(savePath);
        if (File.Exists(savePath) && !resume)
            SetSaveAside(savePath);

        ManagedDoom.Doom created = null;
        DoomVideo createdVideo = null;
        DoomInput createdInput = null;
        DoomAudio createdAudio = null;
        GameContent content = null;
        try
        {
            for (var attempt = 0; attempt < 2 && created == null; attempt++)
            {
                try
                {
                    var args = new CommandLineArgs(resume
                        ? ["-loadgame", AutosaveSlot.ToString()]
                        : []);
                    content = new GameContent(args);
                    createdVideo = new DoomVideo(config, content);
                    createdInput = new DoomInput(config);
                    var (sound, music) = CreateAudio(config, content);
                    created = new ManagedDoom.Doom(args, config, content, createdVideo, sound, music, createdInput);
                    if (resume)
                        created.Game.LoadPath = savePath; // the load itself happens on the Doom thread, on its first tic
                    createdAudio = new DoomAudio(sound, music);
                }
                catch (Exception e) when (resume)
                {
                    // Doom could not be built with the autosave: start fresh.
                    MyLog.Default.WriteLine("DoomForSe: autosave not used: " + e.Message);
                    resume = false;
                    content?.Dispose();
                    content = null;
                }
            }
        }
        catch
        {
            content?.Dispose();
            throw;
        }

        gameContent = content;
        if (content.Wad.GetLumpNumber("DEHACKED") != -1)
        {
            tablesPatched = true;
            patchedKey = wadKey;
        }

        video = createdVideo;
        input = createdInput;
        audio = createdAudio;
        wadName = Path.GetFileName(wadPath);
        wadTitle = WadTitle(wadName);
        ApplyExtras();
        seenHits = GameplayExtras.HitCount;
        seenKills = GameplayExtras.KillCount;
        runner = new DoomRunner(created, video, audio, savePath, resume, resume ? "Resumed your last game" : "Saved on close");
        Current = this;
    }

    /// <summary>
    /// Sound effects come from the WAD. Music also needs the General MIDI sound font that comes with the plugin;
    /// without it (or if it fails to load) Doom plays without music.
    /// </summary>
    private static (DoomSound, DoomMusic) CreateAudio(ManagedDoom.Config config, GameContent content)
    {
        DoomSound sound = null;
        DoomMusic music = null;
        try
        {
            sound = new DoomSound(config, content, DoomAudio.SampleRate);
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("DoomForSe: no sound effects: " + e.Message);
        }

        var soundFont = Plugin.FindSoundFont();
        if (soundFont != null)
        {
            try
            {
                music = new DoomMusic(config, content, soundFont);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine("DoomForSe: no music: " + e.Message);
            }
        }
        else
        {
            MyLog.Default.WriteLine("DoomForSe: no music: the sound font asset was not found");
        }

        return (sound, music);
    }

    public override string GetFriendlyName() => "DoomScreen";

    public override void LoadContent()
    {
        base.LoadContent();
        MyRenderProxy.CreateGeneratedTexture(FrameTexture, video.Width, video.Height, MyGeneratedTextureType.RGBA, 1, new byte[4 * video.Width * video.Height], generateMipmaps: false, immediatelyReady: true);
        textureCreated = true;
        SilenceGameMusic();
        runner.Start();
    }

    /// <summary>
    /// Keeps the game's music at zero while Doom plays. Called every update too, because the game puts the music
    /// volume back by itself when its window regains focus; the volume it puts back is the one to restore later.
    /// </summary>
    private void SilenceGameMusic()
    {
        var audio = MyAudio.Static;
        if (audio == null)
            return;

        var volume = audio.VolumeMusic;
        if (!musicSilenced || volume > 0f)
            savedMusicVolume = volume;
        musicSilenced = true;
        if (volume > 0f)
            audio.VolumeMusic = 0f;
    }

    private void RestoreGameMusic()
    {
        if (!musicSilenced)
            return;
        musicSilenced = false;
        if (MyAudio.Static != null)
            MyAudio.Static.VolumeMusic = savedMusicVolume;
    }

    public override void UnloadContent()
    {
        if (textureCreated)
        {
            MyRenderProxy.DestroyGeneratedTexture(FrameTexture);
            textureCreated = false;
        }
        base.UnloadContent();
    }

    protected override void OnClosed()
    {
        Shutdown();
        base.OnClosed();
    }

    /// <summary>
    /// Stops Doom: the Doom thread ends, the game is saved, the audio voice and the WAD are released. Safe to call
    /// more than once; the plugin calls it at game exit, when no more screen updates would run <see cref="OnClosed"/>.
    /// </summary>
    public void Shutdown()
    {
        if (shutDown)
            return;
        shutDown = true;

        // Stop the Doom thread first; then the engine belongs to this thread again for the save on close.
        // If the thread is still busy after the wait, it ends by itself and the engine is left alone.
        if (runner.Stop())
        {
            runner.FinalSave();
            audio?.Dispose();
            gameContent?.Dispose();
            gameContent = null;
        }

        RestoreGameMusic();
        SaveDoomConfig();
        if (ReferenceEquals(Current, this))
            Current = null;
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        base.HandleInput(receivedFocusInThisUpdate);

        var game = MyInput.Static;
        foreach (var (key, doomKey) in DoomInput.Map)
        {
            var pressed = game.IsKeyPress(key);
            if (pressed == input.IsDown(key))
                continue;

            input.SetKey(key, pressed);
            runner.Post(new DoomEvent(pressed ? EventType.KeyDown : EventType.KeyUp, doomKey));
        }

        // Zoom: held key or middle mouse button, only while the game has the mouse (in a level, no menu).
        var zoomKey = Config.Current.ZoomKey;
        zoomHeld = input.MouseGrabbed && runner.InLevel
            && ((zoomKey.Key != MyKeys.None && zoomKey.IsPressedAnyModifiers(game)) || game.IsMiddleMousePressed());

        var mouseScale = 1f - zoom * (1f - 1f / ZoomFactor); // turn slower while zoomed, so aiming feels the same
        input.AddMouse(game.GetMouseXForGamePlayF() * mouseScale, game.GetMouseYForGamePlayF() * mouseScale,
            game.IsLeftMousePressed(), game.IsRightMousePressed());

        // The cursor is only shown while Doom is not using the mouse for looking around.
        DrawMouseCursor = !input.MouseGrabbed;

        var layout = new DoomLayout(MyGuiManager.GetFullscreenRectangle());
        var mouse = game.GetMousePosition();
        var free = !input.MouseGrabbed;
        closeHover = free && layout.Close.Contains((int)mouse.X, (int)mouse.Y);
        downHover = free && layout.VolumeDown.Contains((int)mouse.X, (int)mouse.Y);
        upHover = free && layout.VolumeUp.Contains((int)mouse.X, (int)mouse.Y);
        if (game.IsNewLeftMousePressed())
        {
            if (closeHover)
                CloseScreen();
            else if (downHover)
                Config.Current.ChangeVolume(-Config.VolumeStep);
            else if (upHover)
                Config.Current.ChangeVolume(Config.VolumeStep);
        }

        // The volume keys (Page Down / Page Up by default) work at any time.
        var cfg = Config.Current;
        if (cfg.VolumeDownKey.Key != MyKeys.None && cfg.VolumeDownKey.HasPressedAnyModifiers(game))
            cfg.ChangeVolume(-Config.VolumeStep);
        if (cfg.VolumeUpKey.Key != MyKeys.None && cfg.VolumeUpKey.HasPressedAnyModifiers(game))
            cfg.ChangeVolume(Config.VolumeStep);

        // The open key also closes the screen; Plugin.Update sees it too, so only one of them acts on it.
    }

    public override void InputLost()
    {
        input.ReleaseAll();
        base.InputLost();
    }

    public override bool Update(bool hasFocus)
    {
        if (!base.Update(hasFocus))
            return false;

        if (closing)
            return true;

        if (runner.Finished || runner.Error != null)
        {
            // Doom quit from its own menu, or stopped with an error (already logged).
            closing = true;
            if (runner.ResumeFailed)
            {
                // The saved game could not be loaded: set it aside and open Doom again with a new game.
                SetSaveAside(AutosavePath(WadKey(ConfigUtilities.GetWadPaths(new CommandLineArgs(new string[0]))[0])));
                reopenFresh = true;
                pendingMessage = "The saved game could not be loaded. It was kept as a .bad file in the Doom folder, and a new game starts.";
            }
            else if (runner.Error != null)
            {
                pendingMessage = "Doom stopped: " + runner.Error.Message;
            }
            CloseScreen();
            return true;
        }

        // Paused while another screen is on top.
        runner.Paused = !hasFocus;
        if (musicSilenced)
            SilenceGameMusic();

        ApplyExtras();
        var now = (float)extrasClock.Elapsed.TotalSeconds;
        var dt = Math.Min(0.1f, now - lastExtrasSeconds);
        lastExtrasSeconds = now;
        zoom = zoomHeld ? Math.Min(1f, zoom + dt / ZoomSeconds) : Math.Max(0f, zoom - dt / ZoomSeconds);

        var hits = GameplayExtras.HitCount;
        var kills = GameplayExtras.KillCount;
        if (hits != seenHits)
        {
            lastHitSeconds = now;
            lastWasKill = kills != seenKills;
            seenHits = hits;
            seenKills = kills;
        }

        if (textureCreated && runner.TryTakeFrame(ref uploadedFrame, out var frame))
            MyRenderProxy.ResetGeneratedTexture(FrameTexture, frame);

        return true;
    }

    public override bool Draw()
    {
        if (!base.Draw())
            return false;

        var cfg = Config.Current;
        hitMarkers = cfg.HitMarkers;
        var l = new DoomLayout(MyGuiManager.GetFullscreenRectangle());
        var u = l.Unit;
        var line = Math.Max(1, (int)(u * 0.0019f));

        // Backdrop, then the frame.
        var backdrop = Plugin.Texture(l.BackdropName);
        Sprite(backdrop ?? Blank, l.Screen, backdrop != null ? Color.White : new Color(12, 6, 6));

        // Title bar.
        Fill(l.Title, Panel);
        Fill(new Rectangle(l.Title.X, l.Title.Bottom - line, l.Title.Width, line), Line);
        Text("DOOM", l.Title.X + (int)(u * 0.016f), l.Title.Y + l.Title.Height / 2, 1.25f, Accent, vcenter: true);
        Text("Space Engineers edition", l.Title.X + (int)(u * 0.016f) + (int)(u * 0.12f), l.Title.Y + l.Title.Height / 2, 0.7f, Dim, vcenter: true);

        var buttonTexture = Plugin.Texture(closeHover ? "DoomButtonHover" : "DoomButton");
        Sprite(buttonTexture ?? Blank, l.Close, buttonTexture != null ? Color.White : (closeHover ? new Color(120, 30, 20) : new Color(60, 16, 12)));
        Text("CLOSE", l.Close.X + l.Close.Width / 2, l.Close.Y + l.Close.Height / 2, 0.7f, closeHover ? Bright : Color.White, center: true, vcenter: true);

        // Picture frame: the black backing first, then the border lines over it.
        Fill(l.Frame, new Color(0, 0, 0, 255));
        Fill(new Rectangle(l.Frame.X, l.Frame.Y, l.Frame.Width, line), Line);
        Fill(new Rectangle(l.Frame.X, l.Frame.Bottom - line, l.Frame.Width, line), Line);
        Fill(new Rectangle(l.Frame.X, l.Frame.Y, line, l.Frame.Height), Line);
        Fill(new Rectangle(l.Frame.Right - line, l.Frame.Y, line, l.Frame.Height), Line);

        // Doom's picture.
        if (textureCreated)
            DrawFrame(l.View);

        Corners(l);

        // Footer: a status bar. The text is rebuilt only when one of its values changes.
        Fill(l.Footer, Panel);
        Fill(new Rectangle(l.Footer.X, l.Footer.Y, l.Footer.Width, line), Line);
        Text(StatusBar(cfg.Volume), l.Footer.X + l.Footer.Width / 2, l.Footer.Y + l.Footer.Height / 2, 0.7f, Dim, center: true, vcenter: true, maxWidth: l.Footer.Width - (int)(u * 0.02f));

        if (l.HasPanels)
        {
            ControlsPanel(l, l.Left, ControlRows(cfg));
            SidePanel(l, l.Right, "STATUS", StatusLines(cfg));
            VolumeControl(l, cfg.Volume);
        }

        return true;
    }

    /// <summary>The bottom line: game data, current map, volume and picture size.</summary>
    private string StatusBar(int volume)
    {
        var inLevel = runner.InLevel;
        var key = (inLevel, runner.Episode, runner.Map, volume);
        if (statusBar == null || !key.Equals(statusBarKey))
        {
            statusBarKey = key;
            var map = !inLevel ? "Menu"
                : runner.Commercial ? "MAP" + runner.Map.ToString("00")
                : "E" + runner.Episode + "M" + runner.Map;
            statusBar = wadTitle + "    |    " + map + "    |    Volume " + volume + "%    |    " + video.Width + " x " + video.Height;
        }

        return statusBar;
    }

    /// <summary>The controls list; the key names are rebuilt only when a binding changes.</summary>
    private (string Key, string Action)[] ControlRows(Config cfg)
    {
        var key = (BindingSignature(cfg.ZoomKey), BindingSignature(cfg.VolumeDownKey), BindingSignature(cfg.VolumeUpKey));
        if (controlRows == null || !key.Equals(controlsKey))
        {
            controlsKey = key;
            controlRows =
            [
                ("W A S D", "Move"),
                ("Arrows", "Move / turn"),
                ("Mouse", "Look"),
                ("Click", "Fire"),
                ("R-click", "Use"),
                ("Space", "Use"),
                ("Shift", "Walk (run is on)"),
                ("1 - 7", "Weapons"),
                ("Tab", "Automap"),
                ("Esc", "Doom menu"),
                (KeyName(cfg.ZoomKey) + " / M3", "Zoom (hold)"),
                (KeyName(cfg.VolumeDownKey) + " / " + KeyName(cfg.VolumeUpKey), "Volume")
            ];
        }

        return controlRows;
    }

    /// <summary>The status panel lines; rebuilt only when the open key or the resolution setting changes.</summary>
    private string[] StatusLines(Config cfg)
    {
        var key = (BindingSignature(cfg.OpenKey), (int)cfg.Resolution);
        if (statusLinesFree == null || !key.Equals(statusKey) || !ReferenceEquals(statusNote, runner.AutosaveNote))
        {
            statusKey = key;
            statusNote = runner.AutosaveNote;
            string[] Build(string mouse) =>
            [
                "Game data",
                "  " + wadName + (wadName.StartsWith("freedoom", StringComparison.OrdinalIgnoreCase) ? " (bundled)" : ""),
                "",
                "Resolution",
                "  " + video.Width + " x " + video.Height + (cfg.Resolution == DoomResolution.Auto_MatchScreen ? " (auto)" : ""),
                "",
                "Mouse",
                mouse,
                "",
                "Save",
                "  " + runner.AutosaveNote,
                "  Every minute and on close",
                "",
                KeyName(cfg.OpenKey) + " closes this",
            ];
            statusLinesGrabbed = Build("  Captured by Doom");
            statusLinesFree = Build("  Free (use the menu)");
        }

        return input.MouseGrabbed ? statusLinesGrabbed : statusLinesFree;
    }

    private static int BindingSignature(ClientPlugin.Settings.Tools.Binding b) =>
        (int)b.Key * 8 + (b.Ctrl ? 1 : 0) + (b.Alt ? 2 : 0) + (b.Shift ? 4 : 0);

    /// <summary>A friendly name for the common IWADs; any other file shows its file name.</summary>
    private static string WadTitle(string fileName)
    {
        switch (Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant())
        {
            case "freedoom1": return "Freedoom: Phase 1";
            case "freedoom2": return "Freedoom: Phase 2";
            case "freedm": return "FreeDM";
            case "doom": return "The Ultimate Doom";
            case "doom1": return "Doom (shareware)";
            case "doom2": return "Doom II";
            case "plutonia": return "Final Doom: Plutonia";
            case "tnt": return "Final Doom: TNT";
            default: return fileName;
        }
    }

    /// <summary>The controls list, with the keys and the actions in two aligned columns.</summary>
    private void ControlsPanel(DoomLayout l, Rectangle r, (string Key, string Action)[] rows)
    {
        SidePanel(l, r, "CONTROLS", NoLines);

        var u = l.Unit;
        var pad = (int)(u * 0.016f);
        var x = r.X + pad;
        var actionX = r.X + (int)(r.Width * 0.38f);
        var y = r.Y + (int)(u * 0.022f) + (int)(u * 0.05f);
        var step = (int)(u * 0.030f);
        foreach (var (key, action) in rows)
        {
            Text(key, x, y, 0.62f, Accent, maxWidth: actionX - x - pad / 2);
            Text(action, actionX, y, 0.62f, Color.White, maxWidth: r.Right - pad - actionX);
            y += step;
        }
    }

    /// <summary>The volume row at the bottom of the status panel: [-] level bar [+], with the value above.</summary>
    private void VolumeControl(DoomLayout l, int volume)
    {
        var u = l.Unit;
        var line = Math.Max(1, (int)(u * 0.0019f));

        var labelY = l.VolumeDown.Y - (int)(u * 0.036f);
        Text("Volume", l.VolumeDown.X, labelY, 0.62f, Dim);

        VolumeButton(l.VolumeDown, "-", downHover, line);
        VolumeButton(l.VolumeUp, "+", upHover, line);

        var bar = l.VolumeBar;
        Fill(bar, new Color(40, 16, 12, 230));
        var filled = (int)(bar.Width * volume / (float)Config.MaxVolume);
        if (filled > 0)
            Fill(new Rectangle(bar.X, bar.Y, filled, bar.Height), Accent);
        Outline(bar, Line, line);
    }

    private void VolumeButton(Rectangle r, string label, bool hover, int line)
    {
        Fill(r, hover ? new Color(110, 28, 18, 240) : new Color(46, 14, 12, 235));
        Outline(r, hover ? Bright : Line, line);
        Text(label, r.X + r.Width / 2, r.Y + r.Height / 2, 0.9f, hover ? Bright : Color.White, center: true, vcenter: true);
    }

    private static void Outline(Rectangle r, Color color, int line)
    {
        Fill(new Rectangle(r.X, r.Y, r.Width, line), color);
        Fill(new Rectangle(r.X, r.Bottom - line, r.Width, line), color);
        Fill(new Rectangle(r.X, r.Y, line, r.Height), color);
        Fill(new Rectangle(r.Right - line, r.Y, line, r.Height), color);
    }

    /// <summary>A short key name for the hints, e.g. "PgDn" instead of "PageDown", so it fits the panel.</summary>
    private static string KeyName(ClientPlugin.Settings.Tools.Binding binding)
    {
        if (binding.Key == MyKeys.None)
            return "(unbound)";

        var key = binding.Key switch
        {
            MyKeys.PageDown => "PgDn",
            MyKeys.PageUp => "PgUp",
            MyKeys.Delete => "Del",
            MyKeys.Insert => "Ins",
            _ => binding.Key.ToString(),
        };
        return (binding.Ctrl ? "Ctrl+" : "") + (binding.Alt ? "Alt+" : "") + (binding.Shift ? "Shift+" : "") + key;
    }

    private static void ApplyExtras()
    {
        var cfg = Config.Current;
        GameplayExtras.Recoil = cfg.Recoil;
        GameplayExtras.HitMarkers = cfg.HitMarkers;
        GameplayExtras.ExtraBlood = cfg.ExtraBlood;
    }

    /// <summary>
    /// Draws the frame. While zoomed, the 3D view is cropped around its centre and stretched, and the status bar is
    /// drawn as it is; the frame is rendered at a higher resolution than shown, so the crop stays sharp.
    /// </summary>
    private void DrawFrame(Rectangle view)
    {
        if (zoom <= 0f)
        {
            // Not zoomed: the whole frame as it is.
            Sprite(FrameTexture, view, Color.White);
            DrawHitMarker(new Vector2(view.X + view.Width / 2f, view.Y + (view.Height - view.Height * video.StatusBarHeight / (float)video.Height) / 2f), view.Height);
            return;
        }

        var w = video.Width;
        var h = video.Height;
        var bar = video.StatusBarHeight;
        var viewH = h - bar;

        // The screen is split between the 3D view and the status bar in the same proportion as the frame.
        var destBar = (int)Math.Round(view.Height * (bar / (float)h));
        var dest3D = new RectangleF(view.X, view.Y, view.Width, view.Height - destBar);

        var factor = 1f + zoom * (ZoomFactor - 1f);
        var srcW = (int)(w / factor);
        var srcH = (int)(viewH / factor);
        var src = new Rectangle((w - srcW) / 2, (viewH - srcH) / 2, srcW, srcH);
        MyRenderProxy.DrawSprite(FrameTexture, ref dest3D, src, Color.White, 0f, true, false);

        if (bar > 0)
        {
            var destStatus = new RectangleF(view.X, view.Bottom - destBar, view.Width, destBar);
            MyRenderProxy.DrawSprite(FrameTexture, ref destStatus, new Rectangle(0, viewH, w, bar), Color.White, 0f, true, false);
        }

        DrawHitMarker(new Vector2(dest3D.X + dest3D.Width / 2f, dest3D.Y + dest3D.Height / 2f), view.Height);
    }

    /// <summary>A small gapped cross on the crosshair after a hit: white, or red on a kill; fades out quickly.</summary>
    private void DrawHitMarker(Vector2 centre, int viewHeight)
    {
        if (!hitMarkers)
            return;

        var age = (float)extrasClock.Elapsed.TotalSeconds - lastHitSeconds;
        if (age < 0 || age > MarkerSeconds)
            return;

        var alpha = (byte)(255 * (1f - age / MarkerSeconds));
        var color = lastWasKill ? new Color((byte)255, (byte)60, (byte)40, alpha) : new Color((byte)255, (byte)255, (byte)255, alpha);

        var gap = Math.Max(3, viewHeight / 90);
        var len = Math.Max(5, viewHeight / 45);
        var thick = Math.Max(2, viewHeight / 400);
        var cx = (int)centre.X;
        var cy = (int)centre.Y;
        Fill(new Rectangle(cx - gap - len, cy - thick / 2, len, thick), color);
        Fill(new Rectangle(cx + gap, cy - thick / 2, len, thick), color);
        Fill(new Rectangle(cx - thick / 2, cy - gap - len, thick, len), color);
        Fill(new Rectangle(cx - thick / 2, cy + gap, thick, len), color);
    }

    private void SidePanel(DoomLayout l, Rectangle r, string title, string[] lines)
    {
        var u = l.Unit;
        var line = Math.Max(1, (int)(u * 0.0019f));
        Fill(r, Panel);
        Fill(new Rectangle(r.X, r.Y, r.Width, line), Line);
        Fill(new Rectangle(r.X, r.Bottom - line, r.Width, line), Line);
        Fill(new Rectangle(r.X, r.Y, line, r.Height), Line);
        Fill(new Rectangle(r.Right - line, r.Y, line, r.Height), Line);

        var x = r.X + (int)(u * 0.016f);
        var y = r.Y + (int)(u * 0.022f);
        Text(title, x, y, 0.85f, Accent);
        Fill(new Rectangle(x, y + (int)(u * 0.034f), r.Width - 2 * (int)(u * 0.016f), line), Line);
        y += (int)(u * 0.05f);
        var step = (int)(u * 0.030f);
        foreach (var text in lines)
        {
            if (text.Length > 0)
                Text(text, x, y, 0.62f, text.StartsWith(" ") ? Color.White : Dim, maxWidth: r.Width - 2 * (int)(u * 0.016f));
            y += step;
        }
    }

    private void Corners(DoomLayout l)
    {
        var size = (int)(l.Unit * 0.07f);
        var o = (int)(l.Unit * 0.006f);
        var f = l.Frame;
        Corner("DoomCornerTL", new Rectangle(f.X - o, f.Y - o, size, size));
        Corner("DoomCornerTR", new Rectangle(f.Right + o - size, f.Y - o, size, size));
        Corner("DoomCornerBL", new Rectangle(f.X - o, f.Bottom + o - size, size, size));
        Corner("DoomCornerBR", new Rectangle(f.Right + o - size, f.Bottom + o - size, size, size));
    }

    private static void Corner(string name, Rectangle r)
    {
        var path = Plugin.Texture(name);
        if (path != null)
            Sprite(path, r, Color.White);
    }

    /// <summary>The size of a text in pixels. Measured once per text and scale; forgotten when the screen size changes.</summary>
    private static Vector2 TextSize(string text, float scale)
    {
        var screen = MyGuiManager.GetFullscreenRectangle();
        if (screen != textSizesScreen || TextSizes.Count > 512)
        {
            TextSizes.Clear();
            textSizesScreen = screen;
        }

        var key = (text, scale);
        if (!TextSizes.TryGetValue(key, out var size))
        {
            size = MyGuiManager.GetScreenSizeFromNormalizedSize(MyGuiManager.MeasureString(Font, text, scale));
            TextSizes[key] = size;
        }

        return size;
    }

    private static void Sprite(string texture, Rectangle r, Color color) =>
        MyGuiManager.DrawSpriteBatch(texture, r, color, ignoreBounds: true, waitTillLoaded: false);

    private static void Fill(Rectangle r, Color color) => Sprite(Blank, r, color);

    /// <summary>
    /// Draws text at a pixel position. The alignment is worked out here in pixels, because the game measures text in
    /// GUI-area units (a 4:3 area in the middle of the screen) while full-screen drawing uses whole-screen units.
    /// </summary>
    private static void Text(string text, int x, int y, float scale, Color color, bool center = false, bool vcenter = false, int maxWidth = int.MaxValue, bool right = false)
    {
        // Text size in pixels: MeasureString is in GUI-area units.
        var size = TextSize(text, scale);
        var width = Math.Min(size.X, maxWidth);

        var left = center ? x - width / 2f : right ? x - width : x;
        var top = vcenter ? y - size.Y / 2f : y;

        // Pixel position to whole-screen units, as DrawString reads them with useFullClientArea.
        var full = MyGuiManager.GetSafeFullscreenRectangle();
        var at = new Vector2((left - full.X) / full.Width, (top - full.Y) / full.Height);

        // maxTextWidth is read in GUI-area units.
        var limit = maxWidth == int.MaxValue
            ? float.PositiveInfinity
            : MyGuiManager.GetNormalizedSizeFromScreenSize(new Vector2(maxWidth, 0)).X;

        MyGuiManager.DrawString(Font, text, at, scale, color, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
            useFullClientArea: true, maxTextWidth: limit, ignoreBounds: true);
    }
}
