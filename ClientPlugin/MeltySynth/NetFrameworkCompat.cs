using System;
using System.Collections.Generic;

namespace MeltySynth
{
    /// <summary>Added for Doom for Space Engineers: Math.Clamp is missing on .NET Framework.</summary>
    internal static class SynthCompat
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

#if NETFRAMEWORK
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }
#endif
    }
}

// Added for Doom for Space Engineers: System.MathF is missing on .NET Framework 4.8. Span and ArrayPool
// come from the game's own System.Memory and System.Buffers.

#if NETFRAMEWORK
namespace MeltySynth
{
    public static class MathF
    {
        public const float PI = (float)Math.PI;
        public static float Abs(float x) => Math.Abs(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Log10(float x) => (float)Math.Log10(x);
        public static float Exp(float x) => (float)Math.Exp(x);
        public static float Log(float x) => (float)Math.Log(x);
        public static float Floor(float x) => (float)Math.Floor(x);
        public static float Round(float x) => (float)Math.Round(x);
    }
}
#endif
