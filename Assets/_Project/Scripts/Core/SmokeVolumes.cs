using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Where the smoke is, for line-of-sight: every smoke effect registers spheres (a puff, or the stacked segments of a
    /// column drifting with the wind) and gunners or interceptors ask how much of the view along a line gets through.
    /// Beer-Lambert extinction: transmittance = exp(-sum(density * path length)). Visible-light optics lose a target
    /// behind a few metres of thick smoke; a thermal (LWIR) sight looks through smoke that blocks visible light
    /// (en.wikipedia.org/wiki/Smoke_screen; LWIR penetrates combustion smoke far better than visible light,
    /// lightpath.com MWIR vs LWIR). Densities per metre are gameplay values (abstract).
    /// </summary>
    public static class SmokeVolumes
    {
        public struct Volume
        {
            public Vector3 centre;
            public float radius;
            /// <summary>Extinction per metre for visible light at the centre (falls off to 0 at the radius).</summary>
            public float density;
            public float expires;
            public int owner;
        }

        /// <summary>Thermal sights see this share of the visible extinction (abstract).</summary>
        public const float ThermalFactor = 0.05f;

        static readonly List<Volume> _volumes = new List<Volume>();
        public static IReadOnlyList<Volume> Volumes => _volumes;

        public static void Add(Vector3 centre, float radius, float density, float expires, int owner = 0) =>
            _volumes.Add(new Volume { centre = centre, radius = radius, density = density, expires = expires, owner = owner });

        /// <summary>Replaces the spheres of one effect (a moving, growing column updates them as it drifts).</summary>
        public static void Set(int owner, Vector3 centre, float radius, float density, float expires)
        {
            for (int i = 0; i < _volumes.Count; i++)
                if (_volumes[i].owner == owner)
                {
                    _volumes[i] = new Volume { centre = centre, radius = radius, density = density, expires = expires, owner = owner };
                    return;
                }
            Add(centre, radius, density, expires, owner);
        }

        public static void Remove(int owner) => _volumes.RemoveAll(v => v.owner == owner);

        public static void Prune(float time) => _volumes.RemoveAll(v => v.expires <= time);

        public static void Clear() => _volumes.Clear();

        /// <summary>Share of light that gets from <paramref name="a"/> to <paramref name="b"/> through the smoke (1 = clear).</summary>
        public static float Transmittance(Vector3 a, Vector3 b, bool thermal = false)
        {
            float optical = 0f;
            foreach (var v in _volumes) optical += OpticalDepth(a, b, v);
            return Mathf.Exp(-optical * (thermal ? ThermalFactor : 1f));
        }

        /// <summary>Below this share of light getting through, an observer loses the target (abstract).</summary>
        public const float SeeThreshold = 0.35f;

        /// <summary>
        /// Whether an observer at <paramref name="eye"/> sees <paramref name="target"/> through the smoke: visible-light
        /// optics by day, thermal sights at night (<see cref="NightMode.Night"/>).
        /// </summary>
        public static bool Visible(Vector3 eye, Vector3 target) => _volumes.Count == 0 || Transmittance(eye, target, NightMode.Night) >= SeeThreshold;

        /// <summary>Density times path length through one sphere whose density falls linearly to its rim (exact chord integral, sampled).</summary>
        public static float OpticalDepth(Vector3 a, Vector3 b, Volume v)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f || v.radius <= 0f) return 0f;
            d /= len;
            float t = Vector3.Dot(v.centre - a, d);
            float closest = (a + d * Mathf.Clamp(t, 0f, len) - v.centre).magnitude;
            if (closest >= v.radius) return 0f;
            // Chord through the sphere clipped to the segment, integrated in a few steps.
            float half = Mathf.Sqrt(v.radius * v.radius - Mathf.Pow((a + d * t - v.centre).magnitude, 2f));
            float t0 = Mathf.Max(0f, t - half), t1 = Mathf.Min(len, t + half);
            if (t1 <= t0) return 0f;
            const int steps = 8;
            float sum = 0f, dt = (t1 - t0) / steps;
            for (int i = 0; i < steps; i++)
            {
                float r = (a + d * (t0 + (i + 0.5f) * dt) - v.centre).magnitude;
                sum += Mathf.Max(0f, 1f - r / v.radius);
            }
            return sum * dt * v.density;
        }
    }
}
