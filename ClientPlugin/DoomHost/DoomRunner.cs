using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using ManagedDoom;
using VRage.Utils;

namespace ClientPlugin.DoomHost;

/// <summary>
/// Runs Doom on a thread of its own: the 35 tics per second, rendering (up to 60 frames per second), the autosave and
/// the audio mixing. The game's main thread only posts input and picks up the newest finished frame, so the game keeps
/// its frame rate whatever resolution Doom renders at.
/// </summary>
public sealed partial class DoomRunner
{
    public const int AutosaveSlot = 7;          // Doom's menu shows slots 0 to 5, so this one stays hidden.
    private const int TicsPerSecond = 35;
    private const int AutosaveSeconds = 60;
    private const double FrameSeconds = 1.0 / 60;

    private readonly ManagedDoom.Doom doom;
    private readonly DoomVideo video;
    private readonly DoomAudio audio;
    private readonly ConcurrentQueue<DoomEvent> events = new();
    private readonly Stopwatch clock = new();
    private readonly object frameLock = new();

    private Thread thread;
    private volatile bool running;
    private volatile bool paused;
    private long ticsDone;
    private long lastAutosaveMs;
    private bool finalSaveDone;
    private readonly string autosavePath;
    private readonly bool resuming;
    private volatile bool levelSeen;

    private byte[] latestFrame;
    private int latestFrameNumber;

    public DoomRunner(ManagedDoom.Doom doom, DoomVideo video, DoomAudio audio, string autosavePath, bool resuming, string autosaveNote)
    {
        this.doom = doom;
        this.video = video;
        this.audio = audio;
        this.autosavePath = autosavePath;
        this.resuming = resuming;
        AutosaveNote = autosaveNote;
    }

    /// <summary>Doom asked to quit (its menu's Quit Game).</summary>
    public bool Finished => finished;
    private volatile bool finished;

    /// <summary>Set if Doom stopped with an error.</summary>
    public Exception Error => error;
    private volatile Exception error;

    /// <summary>True if Doom stopped with an error before the resumed game ever reached its level: the save is bad.</summary>
    public bool ResumeFailed { get; private set; }

    /// <summary>True while a level is being played (not a menu screen, tally, finale or demo).</summary>
    public bool InLevel => inLevel;
    private volatile bool inLevel;

    /// <summary>The episode and map of the level being played (the last one, outside a level).</summary>
    public int Episode => episode;
    public int Map => map;
    public bool Commercial => commercial;
    private volatile int episode, map;
    private volatile bool commercial;

    /// <summary>What the status panel shows about saving.</summary>
    public string AutosaveNote { get; private set; }

    public void Start()
    {
        if (thread != null)
            return;

        running = true;
        clock.Start();
        thread = new Thread(Run) { IsBackground = true, Name = "Doom for Space Engineers" };
        thread.Start();
    }

    /// <summary>
    /// Stops the Doom thread and waits for it. Returns true when the thread has ended, so the caller may touch the
    /// engine directly; false if it was still busy after the wait (it then ends by itself).
    /// </summary>
    public bool Stop()
    {
        running = false;
        var ended = true;
        if (thread != null && thread != Thread.CurrentThread)
            ended = thread.Join(5000);
        thread = null;
        if (ended)
            audio?.Pause();
        return ended;
    }

    /// <summary>Paused while another screen covers Doom: no tics, no sound.</summary>
    public bool Paused
    {
        get => paused;
        set => paused = value;
    }

    public void Post(DoomEvent e) => events.Enqueue(e);

    /// <summary>Hook for the development self-test (debug builds only): runs its queued actions between tics.</summary>
    partial void SelfTestTick();

    /// <summary>The newest frame, if there is one the caller has not taken yet.</summary>
    public bool TryTakeFrame(ref int lastNumber, out byte[] frame)
    {
        lock (frameLock)
        {
            frame = latestFrame;
            if (frame == null || latestFrameNumber == lastNumber)
                return false;

            lastNumber = latestFrameNumber;
            return true;
        }
    }

    private void Run()
    {
        var wasPaused = false;
        var nextFrame = 0.0;

        try
        {
            while (running)
            {
                if (paused)
                {
                    if (!wasPaused)
                    {
                        audio?.Pause();
                        wasPaused = true;
                    }

                    // Keep the tic clock current, so Doom does not rush to catch up when it resumes.
                    ticsDone = clock.ElapsedMilliseconds * TicsPerSecond / 1000;
                    Thread.Sleep(15);
                    continue;
                }
                wasPaused = false;

                while (events.TryDequeue(out var e))
                    doom.PostEvent(e);
                SelfTestTick();

                if (clock.ElapsedMilliseconds - lastAutosaveMs > AutosaveSeconds * 1000L)
                    Autosave(final: false);

                var due = clock.ElapsedMilliseconds * TicsPerSecond / 1000;
                if (due - ticsDone > 5)
                    ticsDone = due - 5; // after a stall, do not try to catch up more than a few tics

                while (ticsDone < due)
                {
                    ticsDone++;
                    if (doom.Update() == UpdateResult.Completed)
                    {
                        finished = true;
                        running = false;
                        return;
                    }
                }

                inLevel = doom.State == DoomState.Game && doom.Game.State == GameState.Level;
                if (inLevel)
                {
                    levelSeen = true;
                    episode = doom.Options.Episode;
                    map = doom.Options.Map;
                    commercial = doom.Options.GameMode == GameMode.Commercial;
                }

                var progress = clock.ElapsedMilliseconds * TicsPerSecond / 1000.0 - ticsDone;
                var k = Math.Max(1, Math.Min(64, (int)(progress * 64) + 1));
                var frame = video.RenderFrame(doom, Fixed.FromInt(k) / 64);
                lock (frameLock)
                {
                    latestFrame = frame;
                    latestFrameNumber++;
                }

                audio?.Pump();

                // Pace to at most 60 frames per second.
                nextFrame += FrameSeconds;
                var now = clock.Elapsed.TotalSeconds;
                if (nextFrame < now - FrameSeconds)
                    nextFrame = now; // fell behind: start the schedule again from now
                while (running && !paused && clock.Elapsed.TotalSeconds < nextFrame)
                {
                    audio?.Pump();
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception e)
        {
            ResumeFailed = resuming && !levelSeen;
            error = e;
            running = false;
            MyLog.Default.WriteLine("DoomForSe: Doom stopped: " + e);
        }
    }

    /// <summary>The save on close. Call only after <see cref="Stop"/>, from the thread that owns the screen.</summary>
    public void FinalSave()
    {
        if (finalSaveDone || error != null)
            return;

        Autosave(final: true);
        finalSaveDone = true;
    }

    /// <summary>
    /// Saves the game to the autosave file while a level is running and the player is alive. It writes the file
    /// directly, between tics, so a failed write is caught here and the engine's "game saved." message does not show.
    /// </summary>
    private void Autosave(bool final)
    {
        lastAutosaveMs = clock.ElapsedMilliseconds;
        try
        {
            if (doom.State != DoomState.Game || doom.Game.State != GameState.Level)
                return;

            var world = doom.Game.World;
            if (world.ConsolePlayer.Health <= 0)
                return; // keep the last good save instead of one taken at the death screen
            if (world.Completed)
                return; // the exit was already used; a save now would resume next to a used exit switch

            SaveAndLoad.Save(doom.Game, "Autosave", autosavePath);
            AutosaveNote = "Autosaved " + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("DoomForSe: autosave failed: " + e.Message);
            AutosaveNote = "Autosave failed";
        }
    }
}
