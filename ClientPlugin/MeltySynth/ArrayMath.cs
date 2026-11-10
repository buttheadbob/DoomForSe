using System;
#if !NETFRAMEWORK
using System.Numerics;
using System.Runtime.InteropServices;
#endif

namespace MeltySynth
{
    internal static class ArrayMath
    {
        public static void MultiplyAdd(float a, float[] x, float[] destination)
        {
            /*
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] += a * x[i];
            }
            */

#if NETFRAMEWORK
            // .NET Framework: Pulsar does not reference System.Numerics.Vectors there, so a plain loop.
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] += a * x[i];
            }
#else
            var vx = MemoryMarshal.Cast<float, Vector<float>>(x.AsSpan());
            var vd = MemoryMarshal.Cast<float, Vector<float>>(destination.AsSpan()); // AsSpan: C# 14 would pick the read-only overload for an array

            var count = 0;

            for (var i = 0; i < vd.Length; i++)
            {
                vd[i] += a * vx[i];
                count += Vector<float>.Count;
            }

            for (var i = count; i < destination.Length; i++)
            {
                destination[i] += a * x[i];
            }
#endif
        }

        public static void MultiplyAdd(float a, float step, float[] x, float[] destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] += a * x[i];
                a += step;
            }
        }
    }
}
