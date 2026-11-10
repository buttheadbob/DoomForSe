// Compatibility helpers so the Managed Doom sources also build for .NET Framework 4.8.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

namespace ManagedDoom
{
    internal static class Compat
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        public static void Rethrow(Exception e) => ExceptionDispatchInfo.Capture(e).Throw();
    }

#if NETFRAMEWORK
    internal static class CompatExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
                return false;
            dictionary.Add(key, value);
            return true;
        }

        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    throw new EndOfStreamException();
                read += n;
            }
        }

        public static string[] Split(this string text, char separator, StringSplitOptions options)
            => text.Split(new[] { separator }, options);
    }
#endif
}
