// Part of Doom for Space Engineers. Licensed under GPL-2.0-or-later, see LICENSE in the repository root.
// Added for Doom for Space Engineers: optional, purely cosmetic gameplay touches, each switched on by the plugin.
// Nothing here changes how the game plays: no damage, movement or AI changes, and the extra effects never use
// Doom's own random numbers, so demos and the game's randomness stay exactly as in vanilla.

using System;
using System.Diagnostics;
using System.Threading;

namespace ManagedDoom
{
    public static class GameplayExtras
    {
        /// <summary>The weapon kicks back and shakes a little when it fires.</summary>
        public static volatile bool Recoil;

        /// <summary>Hits and kills by the player are reported, for the hit marker the plugin draws.</summary>
        public static volatile bool HitMarkers;

        /// <summary>More blood when things are hit.</summary>
        public static volatile bool ExtraBlood;

        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly Random cosmetic = new();

        // Recoil is a critically damped spring, stepped with real time each time the weapon is drawn: one smooth kick
        // per shot that settles without bouncing, and shots that pile up for automatic weapons instead of restarting.
        // Both firing and drawing happen on the Doom thread, so no locking is needed.
        private const float SpringOmega = 26F;    // stiffness: settles in roughly a fifth of a second
        private const float MaxKick = 18F;        // never push the weapon further than this, in 320x200 pixels
        private static float springY, velocityY;
        private static float springX, velocityX;
        private static long lastStepTicks;

        private static int hitCount;
        private static int killCount;

        /// <summary>Counts hits by the player on monsters; the plugin compares it between frames.</summary>
        public static int HitCount => Volatile.Read(ref hitCount);

        /// <summary>Counts kills by the player.</summary>
        public static int KillCount => Volatile.Read(ref killCount);

        /// <summary>Random numbers for effects only; never Doom's game randomness.</summary>
        public static int CosmeticNext(int maxExclusive) => cosmetic.Next(maxExclusive);

        /// <summary>Called when the console player fires.</summary>
        public static void OnFire(WeaponType weapon)
        {
            if (!Recoil)
            {
                return;
            }

            // Peak kick in 320x200 pixels for a single shot.
            var kick = weapon switch
            {
                WeaponType.Fist => 1.5F,
                WeaponType.Chainsaw => 0.8F,
                WeaponType.Pistol => 4F,
                WeaponType.Shotgun => 9F,
                WeaponType.SuperShotgun => 13F,
                WeaponType.Chaingun => 2.5F,
                WeaponType.Missile => 10F,
                WeaponType.Plasma => 1.8F,
                WeaponType.Bfg => 14F,
                _ => 3F,
            };

            // For a critically damped spring starting at rest, an initial speed of v gives a peak of v / (omega * e).
            var impulse = kick * SpringOmega * (float)Math.E;
            velocityY += impulse;

            // A small sideways push to one side or the other, settling with the same spring.
            var side = cosmetic.Next(2) == 0 ? -1F : 1F;
            velocityX += side * impulse * 0.18F;
        }

        /// <summary>Called when the console player damages a monster.</summary>
        public static void OnHit(bool kill)
        {
            if (!HitMarkers)
            {
                return;
            }

            Interlocked.Increment(ref hitCount);
            if (kill)
            {
                Interlocked.Increment(ref killCount);
            }
        }

        /// <summary>
        /// How far to push the weapon sprite right now, in 320x200 pixels: down for the kick, and a little sideways.
        /// Called once per rendered frame; it steps the spring by the real time since the last call.
        /// </summary>
        public static void GetRecoilOffset(out float dx, out float dy)
        {
            var now = clock.ElapsedTicks;
            var seconds = lastStepTicks == 0 ? 0F : (float)((now - lastStepTicks) / (double)Stopwatch.Frequency);
            lastStepTicks = now;

            if (!Recoil)
            {
                springX = springY = velocityX = velocityY = 0;
                dx = dy = 0;
                return;
            }

            // After a pause (screen closed, a long frame), do not run the spring for the whole gap.
            seconds = Math.Min(seconds, 0.1F);

            // Small fixed steps keep the spring stable at any frame rate.
            const float Step = 1F / 480;
            while (seconds > 0)
            {
                var h = Math.Min(Step, seconds);
                StepSpring(ref springY, ref velocityY, h);
                StepSpring(ref springX, ref velocityX, h);
                seconds -= h;
            }

            if (springY > MaxKick)
            {
                springY = MaxKick;
                velocityY = Math.Min(velocityY, 0);
            }

            // Settled: snap to rest so the weapon is exactly where vanilla puts it.
            if (Math.Abs(springY) < 0.01F && Math.Abs(velocityY) < 0.1F && Math.Abs(springX) < 0.01F && Math.Abs(velocityX) < 0.1F)
            {
                springX = springY = velocityX = velocityY = 0;
            }

            dx = springX;
            dy = Math.Max(0, springY); // the weapon is only ever pushed down, never lifted above its rest position
        }

        /// <summary>One step of a critically damped spring: x'' = -w^2 x - 2 w x'.</summary>
        private static void StepSpring(ref float x, ref float v, float h)
        {
            var a = -SpringOmega * SpringOmega * x - 2F * SpringOmega * v;
            v += a * h;
            x += v * h;
        }
    }
}
