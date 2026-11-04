using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Blast waves for the scenery: vegetation bends away from a burst as the front passes and swings back, dust is
    /// lifted. The front of a small charge decays to an acoustic wave within metres, so beyond a few metres it moves at
    /// about the speed of sound (Kinney and Graham, "Explosive Shocks in Air": Mach 1.1 at a scaled distance of
    /// ~3 m/kg^(1/3)). The push is an abstract measure (1 = bends a tree crown fully): a gameplay choice scaled with the
    /// cube root of the charge size (Hopkinson-Cranz scaling, en.wikipedia.org/wiki/Blast_wave), falling off as 1/r.
    /// Shader globals: _Shocks[4] (xyz = centre, w = start game time), _ShockParams[4] (x = strength, 0 = empty), a ring
    /// buffer the shader walks in full (a front's age discards the old ones).
    /// </summary>
    public static class Shockwaves
    {
        public const int Max = 4;
        public const float Speed = 360f; // m/s, slightly faster than sound near the burst
        /// <summary>Seconds a passing front keeps moving the scenery (the swing-back included).</summary>
        public const float Ring = 1.6f;

        static readonly int ShocksId = Shader.PropertyToID("_Shocks");
        static readonly int ParamsId = Shader.PropertyToID("_ShockParams");

        static readonly Vector4[] _shocks = new Vector4[Max];
        static readonly Vector4[] _params = new Vector4[Max];
        static int _next;

        /// <summary>Starts a blast wave at game time <paramref name="time"/>; <paramref name="size"/> is the effect scale (1 = drone warhead).</summary>
        public static void Emit(Vector3 centre, float size, float time)
        {
            _shocks[_next] = new Vector4(centre.x, centre.y, centre.z, time);
            _params[_next] = new Vector4(Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f), 0f, 0f, 0f);
            _next = (_next + 1) % Max;
        }

        public static void Emit(Vector3 centre, float size) => Emit(centre, size, Time.time);

        /// <summary>Seconds after the burst at which the front reaches <paramref name="distance"/>.</summary>
        public static float Arrival(float distance) => Mathf.Max(0f, distance) / Speed;

        /// <summary>
        /// Push (0..1, abstract) on the scenery at <paramref name="distance"/> from a burst of <paramref name="size"/>,
        /// <paramref name="sinceArrival"/> seconds after the front got there: a sharp shove, then a damped swing back.
        /// </summary>
        public static float Push(float size, float distance, float sinceArrival)
        {
            if (sinceArrival < 0f || sinceArrival > Ring) return 0f;
            float strength = Mathf.Clamp01(Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f) * 6f / Mathf.Max(distance, 1f));
            float t = sinceArrival;
            return strength * Mathf.Exp(-t * 3f) * Mathf.Cos(t * 7f) * Mathf.Clamp01(t * 30f);
        }

        /// <summary>The fronts for the vegetation shaders (old ones are dropped there by their age).</summary>
        public static void Publish(float time)
        {
            Shader.SetGlobalVectorArray(ShocksId, _shocks);
            Shader.SetGlobalVectorArray(ParamsId, _params);
        }

        public static void Clear()
        {
            for (int i = 0; i < Max; i++) { _shocks[i] = Vector4.zero; _params[i] = Vector4.zero; }
            _next = 0;
        }
    }
}
