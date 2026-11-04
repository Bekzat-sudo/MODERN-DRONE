using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Outdoor sound propagation for the game's 3D sounds (plain math, unit-tested):
    /// - arrival delay: distance / 343 m/s (dry air at 20 C, en.wikipedia.org/wiki/Speed_of_sound);
    /// - air absorption as a low-pass cut-off: at 1 km air takes ~1 dB at 100 Hz but ~18 dB at 10 kHz (ISO 9613-1
    ///   figures quoted in the sfu.ca Sonic Studio handbook "Sound Propagation"), so a far blast is a dull boom;
    /// - near / far layers of a blast: within ~100 m the crack and debris dominate; by a few hundred metres only the low
    ///   boom and its echoes off the ground and tree lines remain. The cross-over distances scale with the cube root of
    ///   the charge (blast scaling); the exact values are a mixing choice (abstract);
    /// - the first echo: a reflection off terrain or a tree line some 100-300 m away arrives after the direct sound.
    /// </summary>
    public static class SoundPropagation
    {
        public const float SpeedOfSound = 343f;

        public static float Delay(float distance) => Mathf.Max(0f, distance) / SpeedOfSound;

        /// <summary>Low-pass cut-off for air absorption: full band close by, ~2 kHz at 1 km, 700 Hz floor.</summary>
        public static float Cutoff(float distance) => Mathf.Clamp(22000f * Mathf.Exp(-Mathf.Max(0f, distance) / 420f), 700f, 22000f);

        /// <summary>Weight of the far (boom and roll) layer, 0 close to the burst, 1 far away.</summary>
        public static float FarMix(float distance, float size)
        {
            float k = Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f * k, 260f * k, distance));
        }

        /// <summary>Extra delay of the first echo: the path via a reflector <paramref name="reflector"/> metres beyond the source.</summary>
        public static float EchoDelay(float distance, float reflector) =>
            (Mathf.Sqrt(distance * distance + 4f * reflector * reflector) - distance) / SpeedOfSound;
    }
}
