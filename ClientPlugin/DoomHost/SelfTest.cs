#if DEBUG
// Development self-test. Compiled only in debug builds (Pulsar defines DEBUG only for a development folder set to a
// debug build; hub installs are compiled without it), and it runs only when DOOMFORSE_SELFTEST=1 is set and the game
// was started with -appdata pointing at a ".test" folder, so it can never touch a player's data.
// It drives the Doom screen through its own code paths and logs "DoomForSe SELFTEST: <step> PASS|FAIL <detail>".

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ManagedDoom;
using Sandbox;
using Sandbox.Graphics.GUI;
using VRage.Audio;
using VRage.FileSystem;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin
{
    public partial class Plugin
    {
        partial void SelfTestUpdate() => DoomHost.SelfTest.Update();
    }
}

namespace ClientPlugin.DoomHost
{
    public sealed partial class DoomRunner
    {
        private readonly ConcurrentQueue<Action<ManagedDoom.Doom>> selfTestActions = new();

        /// <summary>Runs an action on the Doom thread, between tics.</summary>
        public void SelfTestInvoke(Action<ManagedDoom.Doom> action) => selfTestActions.Enqueue(action);

        partial void SelfTestTick()
        {
            while (selfTestActions.TryDequeue(out var action))
                action(doom);
        }
    }

    public sealed partial class DoomAudio
    {
        public float SelfTestGameGain { get; private set; } = -1f;

        partial void SelfTestGain(float gameGain) => SelfTestGameGain = gameGain;
    }

    public sealed partial class DoomScreen
    {
        internal DoomRunner SelfTestRunner => runner;
        internal ManagedDoom.Config SelfTestConfig => config;
        internal DoomAudio SelfTestAudio => audio;
        internal DoomVideo SelfTestVideo => video;
        internal bool SelfTestTextureCreated => textureCreated;

        internal static string SelfTestSavePath() =>
            AutosavePath(WadKey(ConfigUtilities.GetWadPaths(new CommandLineArgs(new string[0]))[0]));

        internal static string SelfTestConfigPath => DoomConfigPath;
    }

    internal static class SelfTest
    {
        private static bool checkedEnv, enabled, finished;
        private static string mode; // "1": the full run; "walls": only the close-up wall pictures at scale 4 and 8
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static IEnumerator<float> steps;
        private static double resumeAt;
        private static bool waitOk;
        private static int passCount, failCount;
        private static string shotDir;

        public static void Update()
        {
            if (finished)
                return;

            if (!checkedEnv)
            {
                checkedEnv = true;
                mode = Environment.GetEnvironmentVariable("DOOMFORSE_SELFTEST");
                enabled = mode == "1" || mode == "walls";
                if (!enabled)
                    return;

                if (MyFileSystem.UserDataPath.IndexOf("\\.test\\", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Log("start", false, "user data path is not a .test folder: " + MyFileSystem.UserDataPath + "; not running");
                    finished = true;
                    return;
                }

                shotDir = Path.Combine(MyFileSystem.UserDataPath, "SelfTest");
                Directory.CreateDirectory(shotDir);
                steps = (mode == "walls" ? Walls() : Run()).GetEnumerator();
                resumeAt = 25; // let the main menu finish loading
            }

            if (!enabled || Clock.Elapsed.TotalSeconds < resumeAt)
                return;

            try
            {
                if (steps.MoveNext())
                    resumeAt = Clock.Elapsed.TotalSeconds + steps.Current;
                else
                    Finish();
            }
            catch (Exception e)
            {
                Log("exception", false, e.ToString());
                Finish();
            }
        }

        private static void Finish()
        {
            finished = true;
            MyLog.Default.WriteLine($"DoomForSe SELFTEST: DONE {passCount} passed, {failCount} failed");
            MyLog.Default.Flush();
            MySandboxGame.ExitThreadSafe();
        }

        private static void Log(string step, bool ok, string detail)
        {
            if (ok) passCount++; else failCount++;
            MyLog.Default.WriteLine($"DoomForSe SELFTEST: {step} {(ok ? "PASS" : "FAIL")} {detail}");
        }

        private static void Shot(string name)
        {
            var path = Path.Combine(shotDir, name + ".png");
            MyRenderProxy.TakeScreenshot(Vector2.One, path, false, false, false);
            MyLog.Default.WriteLine("DoomForSe SELFTEST: screenshot " + path);
        }

        private static IEnumerable<float> WaitUntil(Func<bool> condition, double timeoutSeconds)
        {
            var start = Clock.Elapsed.TotalSeconds;
            while (!condition())
            {
                if (Clock.Elapsed.TotalSeconds - start > timeoutSeconds)
                {
                    waitOk = false;
                    yield break;
                }
                yield return 0.1f;
            }
            waitOk = true;
        }

        private static DoomScreen Screen => DoomScreen.Current;

        private static void OnDoom(Action<ManagedDoom.Doom> action) => Screen?.SelfTestRunner.SelfTestInvoke(action);

        private static void NewGameWithGodMode()
        {
            OnDoom(d => d.NewGame(GameSkill.Medium, 1, 1));
        }

        private static void GodMode() => OnDoom(d =>
        {
            if (d.State == DoomState.Game && d.Game.World != null)
                d.Game.World.ConsolePlayer.Cheats |= ManagedDoom.CheatFlags.GodMode;
        });

        /// <summary>Frame timing of the game's update loop over a few seconds: average FPS and the slowest frame.</summary>
        private static IEnumerable<float> MeasureFrames(string step, double seconds)
        {
            var frames = 0;
            var worst = 0.0;
            var start = Clock.Elapsed.TotalSeconds;
            var last = start;
            while (Clock.Elapsed.TotalSeconds - start < seconds)
            {
                yield return 0f;
                var now = Clock.Elapsed.TotalSeconds;
                worst = Math.Max(worst, now - last);
                last = now;
                frames++;
            }
            var fps = frames / (Clock.Elapsed.TotalSeconds - start);
            var video = Screen?.SelfTestVideo;
            var size = video != null ? video.Width + "x" + video.Height : "Doom closed";
            MyLog.Default.WriteLine($"DoomForSe SELFTEST: {step} INFO {fps:F1} fps, slowest frame {worst * 1000:F1} ms, Doom {size}");
        }

        /// <summary>
        /// Pushes the player for a while, relative to where they face (0 = forward, -90 = right), so they end up against
        /// a wall. Movement keys go through the screen's own input, which follows the real keyboard, so the momentum is
        /// set directly on the Doom thread instead.
        /// </summary>
        private static double lastX, lastY;

        private static IEnumerable<float> Walk(int degrees, double seconds)
        {
            var start = Clock.Elapsed.TotalSeconds;
            while (Clock.Elapsed.TotalSeconds - start < seconds)
            {
                OnDoom(d =>
                {
                    if (d.State != DoomState.Game || d.Game.World == null)
                        return;
                    var mobj = d.Game.World.ConsolePlayer.Mobj;
                    var angle = mobj.Angle + Angle.FromDegree(degrees);
                    mobj.MomX = 12 * Trig.Cos(angle);
                    mobj.MomY = 12 * Trig.Sin(angle);
                });
                yield return 0.05f;
            }
            OnDoom(d =>
            {
                if (d.State == DoomState.Game && d.Game.World != null)
                {
                    var m = d.Game.World.ConsolePlayer.Mobj;
                    lastX = m.X.ToDouble();
                    lastY = m.Y.ToDouble();
                }
            });
            yield return 1f;
        }

        /// <summary>Walks until the player stops moving (blocked by a wall), and logs whether that happened.</summary>
        private static IEnumerable<float> WalkToWall(string step, int degrees)
        {
            double px = double.NaN, py = double.NaN;
            for (var i = 0; i < 10; i++)
            {
                foreach (var w in Walk(degrees, 0.8)) yield return w;
                if (Math.Abs(lastX - px) < 2 && Math.Abs(lastY - py) < 2)
                {
                    MyLog.Default.WriteLine($"DoomForSe SELFTEST: {step} INFO blocked at ({lastX:F0}, {lastY:F0}) after {i + 1} pushes");
                    yield break;
                }
                px = lastX;
                py = lastY;
            }
            MyLog.Default.WriteLine($"DoomForSe SELFTEST: {step} INFO not blocked, last at ({lastX:F0}, {lastY:F0})");
        }

        private static IEnumerable<float> Close()
        {
            DoomScreen.Toggle(); // the hotkey's own path
            foreach (var w in WaitUntil(() => DoomScreen.Current == null, 10)) yield return w;
        }

        private static IEnumerable<float> Open(double timeout = 15)
        {
            DoomScreen.Toggle();
            foreach (var w in WaitUntil(() => Screen != null && Screen.SelfTestTextureCreated, timeout)) yield return w;
        }

        private static IEnumerable<float> Run()
        {
            var storage = ConfigUtilities.GetExeDirectory();
            var savePath = DoomScreen.SelfTestSavePath();
            var cfgPath = DoomScreen.SelfTestConfigPath;

            // Clean start inside the test folder only.
            foreach (var f in Directory.GetFiles(storage, "doomsav*").Concat(Directory.GetFiles(storage, "managed-doom.cfg")))
                File.Delete(f);
            Config.Current.Resolution = DoomResolution.Auto_MatchScreen;

            foreach (var w in MeasureFrames("M4 baseline (main menu, Doom closed)", 5)) yield return w;

            // ---- Run A
            // The game's configured music volume: what the game itself puts back, and what Doom must restore.
            var musicBefore = MySandboxGame.Config.MusicVolume;
            if (MyAudio.Static != null)
                MyAudio.Static.VolumeMusic = musicBefore;
            foreach (var w in Open()) yield return w;
            Log("A1 open", waitOk, waitOk ? "screen open, frame texture created" : "screen did not open");
            if (!waitOk) yield break;
            yield return 3f;
            Shot("A1-open");
            Log("A1b game music silenced", MyAudio.Static == null || MyAudio.Static.VolumeMusic == 0f, $"game music volume {MyAudio.Static?.VolumeMusic:F2} with Doom open");

            NewGameWithGodMode();
            foreach (var w in WaitUntil(() => Screen != null && Screen.SelfTestRunner.InLevel, 20)) yield return w;
            Log("A2 new game", waitOk, waitOk ? "in level E1M1" : "no level after 20 s");
            yield return 1f;
            GodMode();
            yield return 2f;
            Shot("A2-level");

            foreach (var w in MeasureFrames("M4 Doom open at Auto", 10)) yield return w;

            foreach (var w in WaitUntil(() => Screen != null && Screen.SelfTestRunner.AutosaveNote.StartsWith("Autosaved"), 75)) yield return w;
            var saveOk = File.Exists(savePath) && LastByte(savePath) == 0x1d && !File.Exists(savePath + ".tmp");
            Log("A3 autosave", waitOk && saveOk, $"note '{Screen?.SelfTestRunner.AutosaveNote}', file {Path.GetFileName(savePath)} {(File.Exists(savePath) ? new FileInfo(savePath).Length + " bytes, end 0x" + LastByte(savePath).ToString("x2") : "missing")}");

            // M1: change Doom's music volume through its own option, as the menu does.
            OnDoom(d => d.Options.Music.Volume = 3);
            yield return 1f;
            var before = File.Exists(savePath) ? File.GetLastWriteTimeUtc(savePath) : DateTime.MinValue;
            yield return 1.5f;
            foreach (var w in Close()) yield return w;
            Log("A4 close", waitOk, waitOk ? "screen closed by the hotkey path" : "screen still open");
            Log("A4b game music restored", MyAudio.Static == null || Math.Abs(MyAudio.Static.VolumeMusic - musicBefore) < 0.01f, $"game music volume {MyAudio.Static?.VolumeMusic:F2} after close (was {musicBefore:F2})");
            var cfgText = File.Exists(cfgPath) ? File.ReadAllText(cfgPath) : "";
            Log("A5 options saved", cfgText.Contains("audio_musicvolume = 3"), File.Exists(cfgPath) ? "managed-doom.cfg written" : "managed-doom.cfg missing");
            var after = File.Exists(savePath) ? File.GetLastWriteTimeUtc(savePath) : DateTime.MinValue;
            Log("A6 save on close", after > before && LastByte(savePath) == 0x1d, $"save time {before:HH:mm:ss.fff} -> {after:HH:mm:ss.fff}");
            yield return 2f;

            foreach (var w in Open()) yield return w;
            var note = Screen?.SelfTestRunner.AutosaveNote;
            var music = Screen?.SelfTestConfig.audio_musicvolume;
            foreach (var w in WaitUntil(() => Screen != null && Screen.SelfTestRunner.InLevel, 20)) yield return w;
            Log("A7 resume", waitOk && note == "Resumed your last game", $"note '{note}', in level {waitOk}");
            Log("A8 music volume kept", music == 3, $"audio_musicvolume {music}");
            yield return 2f;
            Shot("A7-resumed");

            // Mute: the game's mute must silence Doom.
            var audioOk = Screen?.SelfTestAudio != null;
            if (audioOk && MyAudio.Static != null)
            {
                var gameVolume = MyAudio.Static.VolumeGame;
                MyLog.Default.WriteLine($"DoomForSe SELFTEST: A9 INFO game volume {gameVolume:F2}, mute {MyAudio.Static.Mute}");
                MyAudio.Static.VolumeGame = 0.8f; // the game sets this to 0 while its window is not focused
                yield return 1f;
                var unmuted = Screen.SelfTestAudio.SelfTestGameGain;
                MyAudio.Static.Mute = true;
                yield return 1f;
                var muted = Screen.SelfTestAudio.SelfTestGameGain;
                MyAudio.Static.Mute = false;
                yield return 1f;
                var restored = Screen.SelfTestAudio.SelfTestGameGain;
                MyAudio.Static.VolumeGame = gameVolume;
                Log("A9 mute", unmuted > 0 && muted == 0f && restored > 0, $"game gain {unmuted:F2} -> muted {muted:F2} -> {restored:F2}");
            }
            else
            {
                Log("A9 mute", false, "no audio device or no Doom audio");
            }

            // M6: walk forward into a wall and take a picture.
            foreach (var w in WalkToWall("M6 walk", 0)) yield return w;
            Shot("A10-wall-auto");

            foreach (var w in Close()) yield return w;
            yield return 2f;

            // ---- Run B: a cut-off save.
            using (var s = new FileStream(savePath, FileMode.Open, FileAccess.Write))
                s.SetLength(100);
            foreach (var w in Open()) yield return w;
            yield return 5f;
            var badB = Path.ChangeExtension(savePath, ".bad");
            Log("B1 cut-off save", waitOk && Screen != null && Screen.SelfTestRunner.Error == null && File.Exists(badB) && !File.Exists(savePath),
                $"screen open {Screen != null}, note '{Screen?.SelfTestRunner.AutosaveNote}', .bad {File.Exists(badB)}");
            Shot("B1-fresh");

            // A fresh save for run C.
            NewGameWithGodMode();
            foreach (var w in WaitUntil(() => Screen != null && Screen.SelfTestRunner.InLevel, 20)) yield return w;
            yield return 2f;
            foreach (var w in Close()) yield return w;
            yield return 1f;
            var haveSave = File.Exists(savePath) && LastByte(savePath) == 0x1d;
            Log("C0 save for damage test", haveSave, haveSave ? new FileInfo(savePath).Length + " bytes" : "no save");
            if (!haveSave) yield break;

            // ---- Run C: a damaged save (map number 99 inside, end marker kept).
            File.Delete(badB);
            var bytes = File.ReadAllBytes(savePath);
            bytes[SaveAndLoad.DescriptionSize + 16 + 2] = 99;
            File.WriteAllBytes(savePath, bytes);
            foreach (var w in Open()) yield return w;
            foreach (var w in WaitUntil(() => MyScreenManager.Screens.Any(x => x is MyGuiScreenMessageBox), 15)) yield return w;
            var boxShown = waitOk;
            yield return 2f;
            Shot("C1-message");
            Log("C1 damaged save", boxShown && Screen != null && File.Exists(badB) && Screen.SelfTestRunner.AutosaveNote == "Saved on close",
                $"message box {boxShown}, reopened {Screen != null}, .bad {File.Exists(badB)}, note '{Screen?.SelfTestRunner.AutosaveNote}'");
            foreach (var box in MyScreenManager.Screens.OfType<MyGuiScreenMessageBox>().ToList())
                box.CloseScreen();
            yield return 1f;
            foreach (var w in Close()) yield return w;
            yield return 2f;

            // ---- M4/M6 at scale 8.
            foreach (var w in WallAt(DoomResolution.For4K_2560x1600, "scale8")) yield return w;
        }

        /// <summary>Only the close-up wall pictures, at scale 4 and at scale 8, from the same spot.</summary>
        private static IEnumerable<float> Walls()
        {
            foreach (var f in Directory.GetFiles(ConfigUtilities.GetExeDirectory(), "doomsav*"))
                File.Delete(f);
            foreach (var w in WallAt(DoomResolution.For1080p_1280x800, "scale4")) yield return w;
            foreach (var w in WallAt(DoomResolution.For4K_2560x1600, "scale8")) yield return w;
            Log("M6 pictures", true, "taken at scale 4 and scale 8");
        }

        /// <summary>A new game at the given resolution: frame timing, then walk into the same wall and strafe along it.</summary>
        private static IEnumerable<float> WallAt(DoomResolution resolution, string tag)
        {
            Config.Current.Resolution = resolution;
            foreach (var w in Open(30)) yield return w;
            NewGameWithGodMode();
            foreach (var w in WaitUntil(() => Screen != null && Screen.SelfTestRunner.InLevel, 30)) yield return w;
            yield return 1f;
            GodMode();
            foreach (var w in MeasureFrames("M4 Doom open at " + tag, 10)) yield return w;
            foreach (var w in WalkToWall("M6 walk " + tag, 0)) yield return w;
            Shot("M6-wall-" + tag);
            foreach (var w in WalkToWall("M6 strafe " + tag, -90)) yield return w;
            Shot("M6-wall-" + tag + "-strafed");
            foreach (var w in Close()) yield return w;
            Config.Current.Resolution = DoomResolution.Auto_MatchScreen;
            yield return 2f;
        }

        private static int LastByte(string path)
        {
            try
            {
                using var s = File.OpenRead(path);
                if (s.Length == 0) return -1;
                s.Seek(-1, SeekOrigin.End);
                return s.ReadByte();
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
#endif
