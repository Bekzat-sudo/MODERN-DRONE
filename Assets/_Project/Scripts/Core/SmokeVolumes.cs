using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Where the smoke and dust are, for line-of-sight: every cloud is a sphere that grows, rises, drifts with the wind
    /// and thins out over time, and gunners or interceptors ask how much of the view along a line gets through
    /// (Beer-Lambert: transmittance = exp(-sum(density * path length))). Bright bursts also dazzle thermal sights.
    /// Sources for the cloud kinds (all numbers below are abstract gameplay estimates built on them):
    /// - FM 3-50 app. G (globalsecurity.org/military/library/policy/army/fm/3-50/Appg.htm): a ground burst makes a hot
    ///   dust ball 4-6 m and a skirt 6-10 m that rises to 10-30 m within 10 s, then drifts and disperses with the wind;
    ///   on dry soil the dust hinders thermal sights less than the eye.
    /// - FM 3-50 app. B (.../Appb.htm): dust blocks the view for several seconds when the burst is on or near the line of
    ///   sight; the flash of a burst whites out or dazzles a thermal sight for a few seconds.
    /// - DTIC ADA080459 (smoke and dust obscuration model): HE dust is effective for seconds, sometimes a minute or more.
    /// - Soot of oxygen-poor explosives is particles under 100 nm (OSTI 1881828): it hides things from the eye but
    ///   barely from an 8-12 um thermal sight, the particles being far smaller than the wavelength (our estimate from
    ///   scattering physics).
    /// - Smoke grenades: a ~10 m cloud within 1-2 s that lasts up to ~2 min, much less in wind (patent US5700971).
    /// Cloud size scales with the cube root of the charge, like the fireball (blast scaling).
    /// </summary>
    public static class SmokeVolumes
    {
        public enum Kind { AirBurst, GroundBurst, VehicleBurst, Fuel, Era, Screen }

        public struct Volume
        {
            public Vector3 centre;
            public float radius, growth, maxRadius;
            public float rise, riseTime;
            public Vector3 drift;
            /// <summary>Extinction per metre for visible light at the centre (falls off to 0 at the radius) while it holds.</summary>
            public float density;
            public float start, hold, decay, expires;
            /// <summary>Share of the density that lingers as thin haze, and how fast that haze clears, s.</summary>
            public float linger, lingerDecay;
            /// <summary>Share of the visible extinction a thermal sight sees (soot ~0, dust about half).</summary>
            public float thermal;
            public int owner;

            public Vector3 CentreAt(float time)
            {
                float t = Mathf.Max(0f, time - start);
                return centre + drift * t + Vector3.up * rise * Mathf.Min(t, riseTime);
            }

            public float RadiusAt(float time) => Mathf.Min(maxRadius, radius + growth * Mathf.Max(0f, time - start));

            /// <summary>
            /// Holds its density for a moment, then the thick cloud thins out fast as it mixes with air while a share
            /// lingers as haze that clears slowly (two exponentials).
            /// </summary>
            public float DensityAt(float time)
            {
                float t = time - start;
                if (t < 0f || time >= expires) return 0f;
                if (t <= hold) return density;
                t -= hold;
                return density * ((1f - linger) * Mathf.Exp(-t / Mathf.Max(0.01f, decay)) + linger * Mathf.Exp(-t / Mathf.Max(0.01f, lingerDecay)));
            }
        }

        struct Flash
        {
            public Vector3 position;
            public float until, reach;
        }

        /// <summary>Below this share of light getting through, an observer loses the target (abstract).</summary>
        public const float SeeThreshold = 0.35f;
        /// <summary>A thermal sight looking within this angle of a burst's flash is whited out (abstract).</summary>
        public const float DazzleAngle = 10f;

        static readonly List<Volume> _volumes = new List<Volume>();
        static readonly List<Flash> _flashes = new List<Flash>();
        public static IReadOnlyList<Volume> Volumes => _volumes;

        /// <summary>The cloud a burst of <paramref name="kind"/> leaves at <paramref name="time"/>, drifting with <paramref name="wind"/>.</summary>
        public static Volume Cloud(Kind kind, Vector3 centre, float size, float time, Vector3 wind)
        {
            float k = Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f);
            var v = new Volume { centre = centre, start = time, drift = wind * 0.8f };
            switch (kind)
            {
                // radius from / to (m), growth m/s, rise m/s for s, density /m, hold s, decay s, linger share and decay s,
                // life s, thermal share. Tuned so a target straight behind the cloud is hidden for the times in the notes.
                case Kind.AirBurst: // soot 2-4 m: hides for ~2 s, nearly clear by ~7 s
                    Set(ref v, 2f * k, 4f * k, 1f, 0.6f, 4f, 0.6f, 0.8f, 1.5f, 0.1f, 6f, 20f, 0.02f);
                    break;
                case Kind.GroundBurst: // dust ball and skirt 6-10 m rising to 10-30 m: hides ~5 s, haze for 30-60 s
                    Set(ref v, 6f * k, 10f * k, 1.2f, 2.5f, 8f, 0.35f, 3f, 1.2f, 0.15f, 15f, 60f, 0.45f);
                    break;
                case Kind.VehicleBurst: // soot and some dust off a hull: hides ~3 s
                    Set(ref v, 4f * k, 8f * k, 1f, 1.5f, 8f, 0.4f, 1.5f, 1.2f, 0.12f, 10f, 40f, 0.15f);
                    break;
                case Kind.Fuel: // oily black cloud: hides ~5 s, lingers
                    Set(ref v, 5f * k, 11f * k, 1.2f, 2f, 10f, 0.45f, 3f, 1.5f, 0.2f, 15f, 60f, 0.05f);
                    break;
                case Kind.Era: // a small grey puff, about a second
                    Set(ref v, 2f * k, 2.5f * k, 0.5f, 0.5f, 2f, 1f, 0.5f, 0.4f, 0f, 1f, 4f, 0.05f);
                    break;
                default: // smoke screen: ~10 m in 1-2 s, up to 1-2 min, shorter in wind
                    float windy = 1f + wind.magnitude / 3f;
                    Set(ref v, 8f, 13f, 3f, 0.2f, 5f, 1.5f, 75f / windy, 25f / windy, 0f, 1f, 130f / windy, 0.05f);
                    break;
            }
            return v;
        }

        static void Set(ref Volume v, float r0, float rMax, float growth, float rise, float riseTime, float density,
            float hold, float decay, float linger, float lingerDecay, float life, float thermal)
        {
            v.radius = r0; v.maxRadius = rMax; v.growth = growth; v.rise = rise; v.riseTime = riseTime;
            v.density = density; v.hold = hold; v.decay = decay; v.linger = linger; v.lingerDecay = lingerDecay;
            v.expires = v.start + life; v.thermal = thermal;
        }

        public static void Add(Kind kind, Vector3 centre, float size) => Add(Cloud(kind, centre, size, Time.time, Wind.Current));

        public static void Add(Volume v) => _volumes.Add(v);

        /// <summary>
        /// Replaces the sphere of one long-lived source (a burning vehicle's smoke column, moved and refreshed as it
        /// drifts): constant density until it is no longer refreshed.
        /// </summary>
        public static void Set(int owner, Vector3 centre, float radius, float density, float expires)
        {
            var v = new Volume
            {
                centre = centre, radius = radius, maxRadius = radius, density = density, start = Time.time,
                hold = float.MaxValue, decay = 1f, expires = expires, thermal = 0.05f, owner = owner
            };
            for (int i = 0; i < _volumes.Count; i++)
                if (_volumes[i].owner == owner) { _volumes[i] = v; return; }
            _volumes.Add(v);
        }

        /// <summary>A burst's flash whites out thermal sights looking at it for a few seconds (1-3 s by charge size).</summary>
        public static void AddFlash(Vector3 position, float size, float time) =>
            _flashes.Add(new Flash { position = position, until = time + Mathf.Clamp(Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f) * 1.5f, 1f, 3f), reach = 600f });

        public static void AddFlash(Vector3 position, float size) => AddFlash(position, size, Time.time);

        public static void Remove(int owner)
        {
            for (int i = _volumes.Count - 1; i >= 0; i--)
                if (_volumes[i].owner == owner) _volumes.RemoveAt(i);
        }

        public static void Prune(float time)
        {
            for (int i = _volumes.Count - 1; i >= 0; i--)
                if (_volumes[i].expires <= time) _volumes.RemoveAt(i);
            for (int i = _flashes.Count - 1; i >= 0; i--)
                if (_flashes[i].until <= time) _flashes.RemoveAt(i);
        }

        public static void Clear()
        {
            _volumes.Clear();
            _flashes.Clear();
        }

        /// <summary>Share of light that gets from <paramref name="a"/> to <paramref name="b"/> through the smoke now (1 = clear).</summary>
        public static float Transmittance(Vector3 a, Vector3 b, bool thermal = false) => Transmittance(a, b, thermal, Time.time);

        public static float Transmittance(Vector3 a, Vector3 b, bool thermal, float time)
        {
            float optical = 0f;
            foreach (var v in _volumes) optical += OpticalDepth(a, b, v, time) * (thermal ? v.thermal : 1f);
            return Mathf.Exp(-optical);
        }

        /// <summary>Whether a thermal sight at <paramref name="eye"/> looking at <paramref name="target"/> is whited out by a flash.</summary>
        public static bool Dazzled(Vector3 eye, Vector3 target, float time)
        {
            foreach (var f in _flashes)
            {
                if (time > f.until || (f.position - eye).sqrMagnitude > f.reach * f.reach) continue;
                if (Vector3.Angle(target - eye, f.position - eye) < DazzleAngle) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether an observer at <paramref name="eye"/> sees <paramref name="target"/>: visible-light optics by day,
        /// thermal sights at night (<see cref="NightMode.Night"/>), which see through soot but are dazzled by flashes.
        /// </summary>
        public static bool Visible(Vector3 eye, Vector3 target) => Visible(eye, target, NightMode.Night, Time.time);

        public static bool Visible(Vector3 eye, Vector3 target, bool thermal, float time)
        {
            if (thermal && Dazzled(eye, target, time)) return false;
            return _volumes.Count == 0 || Transmittance(eye, target, thermal, time) >= SeeThreshold;
        }

        /// <summary>Density times path length through one sphere whose density falls linearly to its rim (sampled chord).</summary>
        public static float OpticalDepth(Vector3 a, Vector3 b, Volume v, float time)
        {
            float density = v.DensityAt(time);
            float radius = v.RadiusAt(time);
            if (density <= 0f || radius <= 0f) return 0f;
            Vector3 c = v.CentreAt(time);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return 0f;
            d /= len;
            float t = Vector3.Dot(c - a, d);
            float miss = (a + d * t - c).magnitude;
            if (miss >= radius) return 0f;
            float half = Mathf.Sqrt(radius * radius - miss * miss);
            float t0 = Mathf.Max(0f, t - half), t1 = Mathf.Min(len, t + half);
            if (t1 <= t0) return 0f;
            const int steps = 8;
            float sum = 0f, dt = (t1 - t0) / steps;
            for (int i = 0; i < steps; i++)
            {
                float r = (a + d * (t0 + (i + 0.5f) * dt) - c).magnitude;
                sum += Mathf.Max(0f, 1f - r / radius);
            }
            return sum * dt * density;
        }
    }
}
