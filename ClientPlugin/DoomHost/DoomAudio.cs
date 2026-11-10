using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ManagedDoom.Audio;
using VRage.Audio;
using VRage.Data.Audio;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.DoomHost;

/// <summary>
/// Plays Doom's sound effects and music through the game's own audio engine, so it works wherever the game's audio
/// does (Windows and Linux). The game's streaming voice (the one voice chat uses) takes 16-bit mono PCM at 24 kHz;
/// effects and music are mixed in software and submitted in short chunks, a little ahead of time.
/// </summary>
public sealed class DoomAudio : IDisposable
{
    public const int SampleRate = 24000;
    private const int ChunkSamples = SampleRate / 50;   // 20 ms per submitted buffer
    private const int LeadSamples = SampleRate / 8;     // keep about 125 ms queued

    private readonly DoomSound sound;
    private readonly DoomMusic music;
    private readonly Emitter emitter = new();
    private readonly float[] mix = new float[ChunkSamples];
    private readonly Stopwatch clock = new();

    private IMySourceVoice voice;
    private long submitted;
    private bool failed;

    public DoomAudio(DoomSound sound, DoomMusic music)
    {
        this.sound = sound;
        this.music = music;
    }

    /// <summary>Called every frame while Doom runs: tops up the queued audio.</summary>
    public void Pump()
    {
        if (failed)
            return;

        try
        {
            if (voice == null || !voice.IsValid)
            {
                voice = MyAudio.Static?.GetSound(emitter, MySoundDimensions.D2);
                if (voice == null)
                    return; // no audio device, or the game's audio is off

                submitted = 0;
                clock.Restart();
            }

            var due = clock.ElapsedTicks * SampleRate / Stopwatch.Frequency + LeadSamples;

            // After a stall (loading, a long frame), start over instead of trying to catch up.
            if (due - submitted > SampleRate / 2)
                submitted = due - LeadSamples;

            while (submitted < due)
            {
                Array.Clear(mix, 0, mix.Length);
                sound?.MixInto(mix, ChunkSamples);
                music?.MixInto(mix, ChunkSamples);
                voice.SubmitBuffer(ToPcm16(mix, Config.Current.VolumeGain));
                submitted += ChunkSamples;
            }

            if (!voice.IsPlaying)
                voice.StartBuffered();
        }
        catch (Exception e)
        {
            // Audio is a nice-to-have: on any problem Doom carries on silently.
            failed = true;
            MyLog.Default.WriteLine("DoomForSe: audio stopped: " + e);
        }
    }

    /// <summary>Stops the sound while Doom is paused (screen in the background) so nothing loops or stutters.</summary>
    public void Pause()
    {
        if (voice != null && voice.IsValid)
        {
            try { voice.Stop(force: true); voice.Destroy(); }
            catch (Exception) { /* already gone */ }
        }
        voice = null;
    }

    public void Dispose() => Pause();

    private static byte[] ToPcm16(float[] samples, float gain)
    {
        // The plugin's own volume, applied last so it is independent of the game's volume and Doom's menu sliders.
        // Squared, so the slider feels even to the ear.
        var scale = 32767f * gain * gain;
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (int)(samples[i] * scale);
            if (v > short.MaxValue) v = short.MaxValue;
            else if (v < short.MinValue) v = short.MinValue;
            bytes[2 * i] = (byte)v;
            bytes[2 * i + 1] = (byte)(v >> 8);
        }
        return bytes;
    }

    /// <summary>The game wants a sound emitter for its streaming voice; this one is fixed, 2D and at full volume.</summary>
    private sealed class Emitter : IMy3DSoundEmitter
    {
        public MyCueId SoundId => default;
        public IMySourceVoice Sound { get; private set; }
        public IMySourceVoice SecondarySound => null;
        public Vector3D SourcePosition => Vector3D.Zero;
        public Vector3 Velocity => Vector3.Zero;
        public float DopplerScaler => 1f;
        public float? CustomMaxDistance => 0f;
        public float? CustomVolume => 1f;
        public bool Realistic => false;
        public bool Force3D => false;
        public bool Plays2D => true;
        public int SourceChannels { get; set; } = 1;
        public int LastPlayedWaveNumber { get; set; }
        public object DebugData { get; set; }
        public object SyncRoot { get; } = new();

        public void SetSound(IMySourceVoice sound, [CallerMemberName] string caller = null) => Sound = sound;
    }
}
