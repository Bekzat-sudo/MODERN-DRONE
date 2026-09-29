using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Procedural one-shot and looping sounds played in 3D, with outdoor propagation: every sound arrives after
    /// distance / 343 m/s and loses its highs with distance (air absorption: at 1 km ~1 dB at 100 Hz, ~18 dB at
    /// 10 kHz, sfu.ca Sonic Studio handbook "Sound Propagation"). The listener is the drone's microphone.
    /// Clips are synthesised once: explosions as a broadband crack, a low boom and a long rumble; guns as a muzzle
    /// thump sized by calibre; supersonic rounds as an N-wave crack heard where the round passes (it outruns its own
    /// muzzle blast: 94 ms apart at 50 m, 507 ms at 300 m, eprints.soton.ac.uk SA80 study).
    /// </summary>
    public static class SimpleAudio
    {
        public const float SpeedOfSound = 343f;

        static AudioClip _explosion, _gunHeavy, _gunMg, _crack, _click, _fire, _metal;
        static AudioListener _listener;

        public static void Explosion(Vector3 position, float size) =>
            Play(position, _explosion != null ? _explosion : _explosion = Synth.Explosion(),
                Mathf.Clamp01(0.55f + size * 0.2f), 15f * size, Mathf.Lerp(1.15f, 0.8f, Mathf.InverseLerp(0.3f, 3f, size)));

        /// <summary>Muzzle blast of an automatic gun; 23 mm and up sound deeper and longer than a 12.7 mm.</summary>
        public static void GunShot(Vector3 position, float caliberMm)
        {
            bool heavy = caliberMm >= 20f;
            var clip = heavy ? (_gunHeavy != null ? _gunHeavy : _gunHeavy = Synth.Gun(true)) : (_gunMg != null ? _gunMg : _gunMg = Synth.Gun(false));
            Play(position, clip, heavy ? 0.9f : 0.75f, heavy ? 10f : 7f, Random.Range(0.94f, 1.06f));
        }

        /// <summary>Supersonic crack of a round passing the listener at the given miss distance.</summary>
        public static void Crack(Vector3 position, float missDistance) =>
            Play(position, _crack != null ? _crack : _crack = Synth.Crack(), Mathf.Clamp01(1.2f - missDistance / 20f), 3f, Random.Range(0.9f, 1.1f), delayed: false);

        /// <summary>Jet or fragment striking armour: short metallic ring.</summary>
        public static void MetalHit(Vector3 position) =>
            Play(position, _metal != null ? _metal : _metal = Synth.Metal(), 0.8f, 6f, Random.Range(0.9f, 1.1f));

        /// <summary>Thermal camera shutter click.</summary>
        public static void Click(Vector3 position) =>
            Play(position, _click != null ? _click : _click = Synth.Burst("Click", 0.05f, 0.0005f, 0.01f), 0.25f, 2f, 1f, delayed: false);

        public static void Shot(Vector3 position) => GunShot(position, 12.7f);

        /// <summary>Crackling, roaring loop for a vehicle fire, attached to the flames.</summary>
        public static AudioSource FireLoop(Transform parent, float size)
        {
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = _fire != null ? _fire : _fire = Synth.Fire();
            src.loop = true;
            src.volume = Mathf.Clamp01(0.35f + 0.2f * size);
            Configure(src, 4f * size);
            AddAbsorption(src.gameObject, Distance(parent.position));
            src.time = Random.Range(0f, src.clip.length);
            src.Play();
            return src;
        }

        static float Distance(Vector3 p)
        {
            if (_listener == null || !_listener.isActiveAndEnabled) _listener = Object.FindAnyObjectByType<AudioListener>();
            return _listener != null ? Vector3.Distance(_listener.transform.position, p) : 0f;
        }

        static void Play(Vector3 position, AudioClip clip, float volume, float minDistance, float pitch, bool delayed = true)
        {
            float d = Distance(position);
            var go = new GameObject("SFX_" + clip.name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            Configure(src, minDistance);
            AddAbsorption(go, d);
            float delay = delayed ? d / SpeedOfSound : 0f;
            src.PlayDelayed(delay);
            Object.Destroy(go, delay + clip.length / pitch + 0.1f);
        }

        static void Configure(AudioSource src, float minDistance)
        {
            src.spatialBlend = 1f;
            src.minDistance = Mathf.Max(1f, minDistance);
            src.maxDistance = 3000f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
        }

        /// <summary>Air absorption as a low-pass: full band close by, ~2 kHz at 1 km, ~800 Hz at 3 km.</summary>
        static void AddAbsorption(GameObject go, float distance)
        {
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = Mathf.Clamp(22000f * Mathf.Exp(-distance / 420f), 700f, 22000f);
            lp.lowpassResonanceQ = 1f;
        }

        /// <summary>Clip synthesis (runs once per clip).</summary>
        static class Synth
        {
            static int Rate => AudioSettings.outputSampleRate;

            static AudioClip Make(string name, float[] s)
            {
                float peak = 1e-4f;
                foreach (var v in s) peak = Mathf.Max(peak, Mathf.Abs(v));
                for (int i = 0; i < s.Length; i++) s[i] = s[i] / peak * 0.95f;
                var clip = AudioClip.Create(name, s.Length, 1, Rate, false);
                clip.SetData(s, 0);
                return clip;
            }

            static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

            /// <summary>Crack (first ms, full band), boom (falling 55 -> 28 Hz tone + low noise), rumble tail (echoes off the ground).</summary>
            public static AudioClip Explosion()
            {
                int n = (int)(3.5f * Rate);
                var s = new float[n];
                var r = new System.Random(11);
                float lp1 = 0f, lp2 = 0f, phase = 0f, echo = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float w = Noise(r);
                    float crack = w * Mathf.Exp(-t / 0.012f) * 1.6f;
                    lp1 += (w - lp1) * 0.15f;
                    float body = lp1 * Mathf.Exp(-t / 0.3f) * 2.4f; // the mid-band "blast" that small speakers carry
                    phase += Mathf.Lerp(55f, 28f, Mathf.Clamp01(t / 0.6f)) / Rate;
                    float boom = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t / 0.45f) * (1f - Mathf.Exp(-t / 0.004f)) * 1.3f;
                    lp2 += (w - lp2) * 0.012f;
                    echo = Mathf.Lerp(echo, (float)r.NextDouble(), 0.0005f);
                    float rumble = lp2 * Mathf.Exp(-t / 1.1f) * (0.6f + echo) * 3.5f * Mathf.Clamp01(t / 0.08f);
                    s[i] = crack + body + boom + rumble;
                }
                return Make("Explosion", s);
            }

            /// <summary>Muzzle blast: sharp report, a low thump (~80 Hz for 23 mm, ~120 Hz for 12.7 mm) and a short tail.</summary>
            public static AudioClip Gun(bool heavy)
            {
                float len = heavy ? 0.7f : 0.45f, thumpHz = heavy ? 80f : 120f, tail = heavy ? 0.18f : 0.1f;
                int n = (int)(len * Rate);
                var s = new float[n];
                var r = new System.Random(heavy ? 23 : 12);
                float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float w = Noise(r);
                    float report = w * Mathf.Exp(-t / 0.003f) * 1.5f;
                    float thump = Mathf.Sin(t * thumpHz * Mathf.PI * 2f) * Mathf.Exp(-t / 0.035f);
                    lp += (w - lp) * 0.15f;
                    lp2 += (w - lp2) * 0.03f;
                    float body = lp * Mathf.Exp(-t / 0.04f) * 1.5f + lp2 * Mathf.Exp(-t / tail) * 2.5f;
                    s[i] = report + thump + body;
                }
                return Make(heavy ? "Gun23" : "Gun12_7", s);
            }

            /// <summary>N-wave of a supersonic round: a sub-millisecond +/- pulse, then a faint ring.</summary>
            public static AudioClip Crack()
            {
                int n = (int)(0.08f * Rate);
                var s = new float[n];
                var r = new System.Random(5);
                int half = Mathf.Max(2, (int)(0.0003f * Rate));
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float nw = i < half * 2 ? 1f - i / (float)half : 0f; // linear +1 -> -1 over the pulse
                    s[i] = nw + Noise(r) * Mathf.Exp(-t / 0.008f) * 0.4f;
                }
                return Make("Crack", s);
            }

            /// <summary>Clang: a few inharmonic partials of a struck plate with fast decay.</summary>
            public static AudioClip Metal()
            {
                int n = (int)(0.6f * Rate);
                var s = new float[n];
                var r = new System.Random(8);
                float[] f = { 420f, 1130f, 1970f, 3310f };
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate, v = Noise(r) * Mathf.Exp(-t / 0.004f);
                    for (int k = 0; k < f.Length; k++) v += Mathf.Sin(t * f[k] * Mathf.PI * 2f) * Mathf.Exp(-t / (0.25f / (k + 1))) * 0.5f;
                    s[i] = v;
                }
                return Make("Metal", s);
            }

            /// <summary>Fire: low roar (brown noise, slow swell) plus random crackles. Loops seamlessly (cross-faded ends).</summary>
            public static AudioClip Fire()
            {
                float len = 4f;
                int n = (int)(len * Rate);
                var s = new float[n];
                var r = new System.Random(3);
                float lpFast = 0f, lpSlow = 0f, swell = 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float w = Noise(r);
                    // Roar: noise band-passed to roughly 60-600 Hz (difference of two one-pole low-passes).
                    lpFast += (w - lpFast) * 0.08f;
                    lpSlow += (w - lpSlow) * 0.008f;
                    swell = Mathf.Lerp(swell, (float)r.NextDouble(), 0.00005f);
                    s[i] = (lpFast - lpSlow) * (0.4f + swell) * 1.6f;
                    if (r.NextDouble() < 22.0 / Rate) // ~22 crackles a second, some loud pops
                    {
                        float amp = (float)r.NextDouble();
                        amp = 0.2f + amp * amp * 1.6f;
                        int m = (int)(0.003f * Rate);
                        for (int k = 0; k < m && i + k < n; k++) s[i + k] += Noise(r) * amp * (1f - k / (float)m);
                    }
                }
                int fade = Rate / 4;
                for (int k = 0; k < fade; k++)
                {
                    float a = k / (float)fade;
                    s[k] = s[k] * a + s[n - fade + k] * (1f - a);
                }
                System.Array.Resize(ref s, n - fade);
                return Make("Fire", s);
            }

            /// <summary>Low-passed noise with a sharp attack and exponential decay.</summary>
            public static AudioClip Burst(string name, float seconds, float attack, float decay)
            {
                int n = Mathf.CeilToInt(seconds * Rate);
                var s = new float[n];
                var r = new System.Random(name.GetHashCode());
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = (t < attack ? t / attack : 1f) * Mathf.Exp(-(t - attack) / decay);
                    lp += (Noise(r) - lp) * Mathf.Lerp(0.5f, 0.04f, Mathf.Clamp01(t / seconds));
                    s[i] = lp * env;
                }
                return Make(name, s);
            }
        }
    }
}
