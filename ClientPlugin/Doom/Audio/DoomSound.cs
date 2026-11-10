//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

// Adapted from Managed Doom's SilkSound.cs (GPL-2.0-or-later) for Doom for Space Engineers.
// The channel allocation, priorities, distance falloff and pitch variation are Managed Doom's. Instead of OpenAL
// channels, the sounds are mixed in software into a mono buffer that the plugin sends to the game's audio engine.
// The game's streaming voice is mono, so sounds keep their distance falloff but have no left/right panning.

using System;

namespace ManagedDoom.Audio
{
    public sealed class DoomSound : ISound
    {
        private const int ChannelCount = 8;

        private static readonly float fastDecay = (float)Math.Pow(0.5, 1.0 / (35 / 5));
        private static readonly float slowDecay = (float)Math.Pow(0.5, 1.0 / 35);

        private const float ClipDist = 1200;
        private const float CloseDist = 160;
        private const float Attenuator = ClipDist - CloseDist;

        private readonly Config config;
        private readonly int outputRate;

        private readonly Clip[] clips;
        private readonly float[] amplitudes;
        private readonly DoomRandom random;

        private readonly Voice[] voices;
        private readonly ChannelInfo[] infos;
        private readonly Voice uiVoice = new();
        private Sfx uiReserved;

        private Mobj listener;
        private float masterVolumeDecay;
        private bool paused;

        public DoomSound(Config config, GameContent content, int outputRate)
        {
            this.config = config;
            this.outputRate = outputRate;

            config.audio_soundvolume = Compat.Clamp(config.audio_soundvolume, 0, MaxVolume);

            clips = new Clip[DoomInfo.SfxNames.Length];
            amplitudes = new float[DoomInfo.SfxNames.Length];

            if (config.audio_randompitch)
            {
                random = new DoomRandom();
            }

            for (var i = 0; i < DoomInfo.SfxNames.Length; i++)
            {
                var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpper();
                if (content.Wad.GetLumpNumber(name) == -1)
                {
                    continue;
                }

                var clip = ReadClip(content.Wad, name);
                if (clip != null)
                {
                    clips[i] = clip;
                    amplitudes[i] = GetAmplitude(clip);
                }
            }

            voices = new Voice[ChannelCount];
            infos = new ChannelInfo[ChannelCount];
            for (var i = 0; i < ChannelCount; i++)
            {
                voices[i] = new Voice();
                infos[i] = new ChannelInfo();
            }

            uiReserved = Sfx.NONE;
            masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
        }

        /// <summary>A sound effect: 8-bit unsigned mono samples at their own rate.</summary>
        private sealed class Clip
        {
            public byte[] Data;
            public int Offset;
            public int Count;
            public int Rate;
        }

        /// <summary>One playing sound in the software mixer.</summary>
        private sealed class Voice
        {
            public Clip Clip;
            public double Position;
            public float Pitch = 1F;
            public float Volume;
            public bool Playing => Clip != null;

            public void Play(Clip clip, float pitch)
            {
                Clip = clip;
                Position = 0;
                Pitch = pitch;
            }

            public void Stop() => Clip = null;
        }

        private static Clip ReadClip(Wad wad, string name)
        {
            var data = wad.ReadLump(name);
            if (data.Length < 8)
            {
                return null;
            }

            var sampleRate = BitConverter.ToUInt16(data, 2);
            var sampleCount = BitConverter.ToInt32(data, 4);

            var offset = 8;
            if (ContainsDmxPadding(data))
            {
                offset += 16;
                sampleCount -= 32;
            }

            sampleCount = Math.Min(sampleCount, data.Length - offset);
            if (sampleCount <= 0 || sampleRate == 0)
            {
                return null;
            }

            return new Clip { Data = data, Offset = offset, Count = sampleCount, Rate = sampleRate };
        }

        // If the first and last 16 samples are the same, the data contains pad bytes. https://doomwiki.org/wiki/Sound
        private static bool ContainsDmxPadding(byte[] data)
        {
            var sampleCount = BitConverter.ToInt32(data, 4);
            if (sampleCount < 32 || 8 + sampleCount > data.Length)
            {
                return false;
            }

            var first = data[8];
            for (var i = 1; i < 16; i++)
            {
                if (data[8 + i] != first)
                {
                    return false;
                }
            }

            var last = data[8 + sampleCount - 1];
            for (var i = 1; i < 16; i++)
            {
                if (data[8 + sampleCount - i - 1] != last)
                {
                    return false;
                }
            }

            return true;
        }

        private static float GetAmplitude(Clip clip)
        {
            var max = 0;
            var count = Math.Min(clip.Rate / 5, clip.Count);
            for (var t = 0; t < count; t++)
            {
                var a = Math.Abs(clip.Data[clip.Offset + t] - 128);
                if (a > max)
                {
                    max = a;
                }
            }
            return (float)max / 128;
        }

        /// <summary>Mixes the playing sounds into the buffer (mono, at the output rate).</summary>
        public void MixInto(float[] mono, int count)
        {
            if (paused)
            {
                return;
            }

            for (var i = 0; i < voices.Length; i++)
            {
                Mix(voices[i], mono, count);
            }
            Mix(uiVoice, mono, count);
        }

        private void Mix(Voice voice, float[] mono, int count)
        {
            var clip = voice.Clip;
            if (clip == null)
            {
                return;
            }

            var step = (double)clip.Rate * voice.Pitch / outputRate;
            var volume = voice.Volume / 128F;
            var pos = voice.Position;
            for (var t = 0; t < count; t++)
            {
                var i = (int)pos;
                if (i >= clip.Count)
                {
                    voice.Stop();
                    return;
                }

                // Linear interpolation between neighbouring samples.
                var s0 = clip.Data[clip.Offset + i] - 128;
                var s1 = i + 1 < clip.Count ? clip.Data[clip.Offset + i + 1] - 128 : s0;
                var frac = (float)(pos - i);
                mono[t] += volume * (s0 + (s1 - s0) * frac);
                pos += step;
            }
            voice.Position = pos;
        }

        public void SetListener(Mobj listener)
        {
            this.listener = listener;
        }

        /// <summary>Called by the engine once per tic.</summary>
        public void Update()
        {
            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                var voice = voices[i];

                if (info.Playing != Sfx.NONE)
                {
                    if (voice.Playing)
                    {
                        info.Priority *= info.Type == SfxType.Diffuse ? slowDecay : fastDecay;
                        SetParam(voice, info);
                    }
                    else
                    {
                        info.Playing = Sfx.NONE;
                        if (info.Reserved == Sfx.NONE)
                        {
                            info.Source = null;
                        }
                    }
                }

                if (info.Reserved != Sfx.NONE)
                {
                    voice.Play(clips[(int)info.Reserved], GetPitch(info.Type, info.Reserved));
                    SetParam(voice, info);
                    info.Playing = info.Reserved;
                    info.Reserved = Sfx.NONE;
                }
            }

            if (uiReserved != Sfx.NONE)
            {
                uiVoice.Play(clips[(int)uiReserved], 1F);
                uiVoice.Volume = masterVolumeDecay;
                uiReserved = Sfx.NONE;
            }
        }

        public void StartSound(Sfx sfx)
        {
            if (clips[(int)sfx] == null)
            {
                return;
            }

            uiReserved = sfx;
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type)
        {
            StartSound(mobj, sfx, type, 100);
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
        {
            if (clips[(int)sfx] == null || listener == null)
            {
                return;
            }

            var x = (mobj.X - listener.X).ToFloat();
            var y = (mobj.Y - listener.Y).ToFloat();
            var dist = (float)Math.Sqrt(x * x + y * y);

            var priority = type == SfxType.Diffuse
                ? volume
                : amplitudes[(int)sfx] * GetDistanceDecay(dist) * volume;

            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Source == mobj && info.Type == type)
                {
                    info.Reserved = sfx;
                    info.Priority = priority;
                    info.Volume = volume;
                    return;
                }
            }

            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Reserved == Sfx.NONE && info.Playing == Sfx.NONE)
                {
                    info.Reserved = sfx;
                    info.Priority = priority;
                    info.Source = mobj;
                    info.Type = type;
                    info.Volume = volume;
                    return;
                }
            }

            var minPriority = float.MaxValue;
            var minChannel = -1;
            for (var i = 0; i < infos.Length; i++)
            {
                if (infos[i].Priority < minPriority)
                {
                    minPriority = infos[i].Priority;
                    minChannel = i;
                }
            }

            if (minChannel >= 0 && priority >= minPriority)
            {
                var info = infos[minChannel];
                info.Reserved = sfx;
                info.Priority = priority;
                info.Source = mobj;
                info.Type = type;
                info.Volume = volume;
            }
        }

        public void StopSound(Mobj mobj)
        {
            for (var i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                if (info.Source == mobj)
                {
                    info.LastX = info.Source.X;
                    info.LastY = info.Source.Y;
                    info.Source = null;
                    info.Volume /= 5;
                }
            }
        }

        public void Reset()
        {
            random?.Clear();

            for (var i = 0; i < infos.Length; i++)
            {
                voices[i].Stop();
                infos[i].Clear();
            }

            listener = null;
        }

        public void Pause() => paused = true;

        public void Resume() => paused = false;

        private void SetParam(Voice voice, ChannelInfo info)
        {
            if (info.Type == SfxType.Diffuse || listener == null)
            {
                voice.Volume = 0.01F * masterVolumeDecay * info.Volume;
                return;
            }

            var sourceX = info.Source == null ? info.LastX : info.Source.X;
            var sourceY = info.Source == null ? info.LastY : info.Source.Y;

            var x = (sourceX - listener.X).ToFloat();
            var y = (sourceY - listener.Y).ToFloat();
            var dist = (float)Math.Sqrt(x * x + y * y);
            voice.Volume = 0.01F * masterVolumeDecay * GetDistanceDecay(dist) * info.Volume;
        }

        private static float GetDistanceDecay(float dist)
        {
            return dist < CloseDist ? 1F : Math.Max((ClipDist - dist) / Attenuator, 0F);
        }

        private float GetPitch(SfxType type, Sfx sfx)
        {
            if (random == null || sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO)
            {
                return 1.0F;
            }

            return type == SfxType.Voice
                ? 1.0F + 0.075F * (random.Next() - 128) / 128
                : 1.0F + 0.025F * (random.Next() - 128) / 128;
        }

        public int MaxVolume => 15;

        public int Volume
        {
            get => config.audio_soundvolume;
            set
            {
                config.audio_soundvolume = value;
                masterVolumeDecay = (float)config.audio_soundvolume / MaxVolume;
            }
        }

        private sealed class ChannelInfo
        {
            public Sfx Reserved;
            public Sfx Playing;
            public float Priority;
            public Mobj Source;
            public SfxType Type;
            public int Volume;
            public Fixed LastX;
            public Fixed LastY;

            public void Clear()
            {
                Reserved = Sfx.NONE;
                Playing = Sfx.NONE;
                Priority = 0;
                Source = null;
                Type = 0;
                Volume = 0;
                LastX = Fixed.Zero;
                LastY = Fixed.Zero;
            }
        }
    }
}
