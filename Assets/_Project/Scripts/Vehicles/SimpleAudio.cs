using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// 3D sounds of the fight from field recordings (Resources/Audio, cut by Tools/Audio/prepare_audio.py; sources and
    /// licences in CREDITS.md), with outdoor propagation (<see cref="SoundPropagation"/>): each sound reaches the
    /// listener distance / 343 m/s after it was made, measured in game time against whoever is listening when it
    /// arrives (the killcam's recon drone 350 m out hears a blast about a second late; a frozen replay holds it back),
    /// and loses its highs with distance. A blast is layered: the near take (crack, body, first debris) fades into a
    /// far take (low boom, rolling echo) with distance, falling debris follows, and a first echo off the terrain comes
    /// a little later. Every play picks one of several takes at a slightly different pitch.
    /// Supersonic rounds crack where they pass (they outrun their own muzzle blast: 94 ms apart at 50 m, 507 ms at
    /// 300 m, eprints.soton.ac.uk SA80 study). The listener is the drone's microphone or the recon camera. Blasts,
    /// fires and cook-offs play sound banks (<see cref="SoundBank"/> assets).
    /// </summary>
    public static class SimpleAudio
    {
        public const float SpeedOfSound = SoundPropagation.SpeedOfSound;

        static AudioListener _listener;
        static AudioClip _click;

        static AudioClip[] Takes(string prefix, int count)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            for (int i = 1; i <= count; i++)
            {
                var c = Resources.Load<AudioClip>("Audio/" + prefix + "_" + i);
                if (c != null) clips.Add(c);
            }
            var single = clips.Count == 0 ? Resources.Load<AudioClip>("Audio/" + prefix) : null;
            if (single != null) clips.Add(single);
            return clips.ToArray();
        }

        static AudioClip[] _mg, _cannon, _metal;
        static AudioClip _crack;

        static void Load()
        {
            if (_mg != null) return;
            _mg = Takes("mg", 4);
            _cannon = Takes("cannon", 3);
            _metal = Takes("metal", 2);
            _crack = Resources.Load<AudioClip>("Audio/crack");
        }

        static AudioClip Pick(AudioClip[] clips) => clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];

        /// <summary>
        /// A blast from <paramref name="bank"/>, layered: the near take (small events: the small takes) fades into the far
        /// take with distance, falling debris follows, and a first echo off the ground and tree lines a couple of hundred
        /// metres beyond (abstract reflector distance) comes a little later. Bigger events sound deeper.
        /// </summary>
        public static void Blast(SoundBank bank, Vector3 position, float size)
        {
            if (bank == null) return;
            float k = Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f);
            bool small = size < bank.smallSize, big = size >= bank.bigSize;
            float pitch = Mathf.Lerp(bank.pitchSmall, bank.pitchBig, Mathf.InverseLerp(0.3f, 3f, size)) * Random.Range(bank.pitchMin, bank.pitchMax);
            float min = bank.minDistance * k;
            var nearTakes = small && bank.small.Length > 0 ? bank.small : bank.near;
            Emit(position, new Layer { clip = SoundBank.Pick(nearTakes), volume = bank.volume * (small ? 0.8f : 1f), minDistance = min, pitch = pitch, near = true, size = size });
            Emit(position, new Layer { clip = SoundBank.Pick(bank.far), volume = big ? 1f : 0.85f, minDistance = min * 1.6f, pitch = pitch * 0.95f, far = true, size = size });
            var debris = SoundBank.Pick(bank.debris);
            if (debris != null && !small)
                Emit(position, new Layer { clip = debris, volume = bank.debrisVolume, minDistance = 10f * k, pitch = Random.Range(0.9f, 1.1f), extraDelay = Random.Range(0.5f, 1.2f), near = true, size = size });
            Emit(position, new Layer { clip = SoundBank.Pick(bank.far), volume = bank.echoVolume, minDistance = min * 1.6f, pitch = pitch * 0.9f, echo = Random.Range(120f, 320f), far = true, size = size });
        }

        /// <summary>Muzzle blast of an automatic gun (one call = a group of four rounds); 23 mm and up sound deeper and slower.</summary>
        public static void GunShot(Vector3 position, float caliberMm)
        {
            Load();
            bool heavy = caliberMm >= 20f;
            Emit(position, new Layer
            {
                clip = Pick(heavy ? _cannon : _mg), volume = heavy ? 0.95f : 0.8f, minDistance = heavy ? 14f : 10f,
                pitch = Random.Range(0.95f, 1.05f) * (heavy ? 1.15f : 1f), near = true, size = 0.3f
            });
        }

        /// <summary>Supersonic crack of a round passing the listener at the given miss distance.</summary>
        public static void Crack(Vector3 position, float missDistance)
        {
            Load();
            Emit(position, new Layer { clip = _crack, volume = Mathf.Clamp01(1.2f - missDistance / 20f), minDistance = 3f, pitch = Random.Range(0.9f, 1.1f), immediate = true, near = true });
        }

        /// <summary>Jet or fragment striking armour: short metallic ring.</summary>
        public static void MetalHit(Vector3 position)
        {
            Load();
            Emit(position, new Layer { clip = Pick(_metal), volume = 0.8f, minDistance = 6f, pitch = Random.Range(0.9f, 1.1f), near = true });
        }

        /// <summary>Thermal camera shutter click (synthesised: a 50 ms low-passed tick).</summary>
        public static void Click(Vector3 position)
        {
            if (_click == null) _click = Tick();
            Emit(position, new Layer { clip = _click, volume = 0.25f, minDistance = 2f, pitch = 1f, immediate = true, near = true });
        }

        public static void Shot(Vector3 position) => GunShot(position, 12.7f);

        /// <summary>
        /// A lasting sound from <paramref name="bank"/> attached to <paramref name="parent"/> (removed with it): the start
        /// one-shot (a jet lighting), the loop (fire roar, torch), the crackle, and pops with the odd bigger bang
        /// (rounds, primers, charges cooking off), for <paramref name="seconds"/> (0 = until the parent goes), fading out at
        /// the end. <paramref name="strength"/> is the size of the fire or jet.
        /// </summary>
        public static void Sustain(SoundBank bank, Transform parent, float seconds, float strength)
        {
            if (bank == null || parent == null) return;
            var start = SoundBank.Pick(bank.start);
            if (start != null)
                Emit(parent.position, new Layer { clip = start, volume = 0.9f * bank.volume, minDistance = 12f, pitch = Random.Range(0.9f, 1.05f), near = true, size = strength });
            var host = new GameObject("SFX_" + bank.name);
            host.transform.SetParent(parent, false);
            host.AddComponent<SustainedSound>().Begin(bank, seconds, strength);
        }

        // ---------------------------------------------------------------- playback

        internal struct Layer
        {
            public AudioClip clip;
            public float volume, minDistance, pitch, extraDelay, echo, size;
            public bool immediate, near, far;
        }

        static void Emit(Vector3 position, Layer layer)
        {
            if (layer.clip == null) return;
            var go = new GameObject("SFX_" + layer.clip.name);
            go.transform.position = position;
            go.AddComponent<DelayedSound>().Init(layer);
        }

        internal static AudioListener Listener
        {
            get
            {
                if (_listener == null || !_listener.isActiveAndEnabled)
                {
                    _listener = null;
                    foreach (var l in Object.FindObjectsByType<AudioListener>())
                        if (l.isActiveAndEnabled) { _listener = l; break; }
                }
                return _listener;
            }
        }

        internal static float Distance(Vector3 p) => Listener != null ? Vector3.Distance(Listener.transform.position, p) : 0f;

        internal static void Configure(AudioSource src, float minDistance)
        {
            src.spatialBlend = 1f;
            src.minDistance = Mathf.Max(1f, minDistance);
            src.maxDistance = 5000f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.dopplerLevel = 0f;
            src.playOnAwake = false;
        }

        /// <summary>Keeps a looping source's air absorption in step with the listener's distance.</summary>
        static void Follower(AudioSource src)
        {
            if (!src.TryGetComponent(out AudioLowPassFilter lp)) lp = src.gameObject.AddComponent<AudioLowPassFilter>();
            lp.lowpassResonanceQ = 1f;
            lp.cutoffFrequency = SoundPropagation.Cutoff(Distance(src.transform.position));
            if (src.gameObject.GetComponent<AbsorptionFollower>() == null) src.gameObject.AddComponent<AbsorptionFollower>();
        }

        static AudioClip Tick()
        {
            int rate = AudioSettings.outputSampleRate, n = Mathf.CeilToInt(0.05f * rate);
            var s = new float[n];
            var r = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                lp += ((float)r.NextDouble() * 2f - 1f - lp) * Mathf.Lerp(0.5f, 0.04f, t / 0.05f);
                s[i] = lp * Mathf.Min(1f, t / 0.0005f) * Mathf.Exp(-t / 0.01f) * 3f;
            }
            var clip = AudioClip.Create("Click", n, 1, rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        /// <summary>A one-shot that waits until its sound has travelled to the listener (in game time), then plays.</summary>
        sealed class DelayedSound : MonoBehaviour
        {
            Layer _layer;
            AudioSource _src;
            AudioLowPassFilter _lp;
            float _age;
            bool _playing;

            public void Init(Layer layer)
            {
                _layer = layer;
                _src = gameObject.AddComponent<AudioSource>();
                _src.clip = layer.clip;
                _src.pitch = layer.pitch;
                Configure(_src, layer.minDistance);
                _lp = gameObject.AddComponent<AudioLowPassFilter>();
                _lp.lowpassResonanceQ = 1f;
                if (layer.immediate) Play(Distance(transform.position));
            }

            void Play(float d)
            {
                _playing = true;
                float far = SoundPropagation.FarMix(d, _layer.size);
                float mix = _layer.far ? Mathf.Max(far, 0.3f) : _layer.near ? 1f - 0.75f * far : 1f;
                _src.volume = Mathf.Clamp01(_layer.volume * mix);
                // Echoes lose more highs: a longer path and a soft reflector.
                _lp.cutoffFrequency = SoundPropagation.Cutoff(d + _layer.echo * 2f) * (_layer.echo > 0f ? 0.5f : 1f);
                _src.Play();
                Destroy(gameObject, _layer.clip.length / Mathf.Max(0.1f, _layer.pitch) + 0.2f);
            }

            void Update()
            {
                if (_playing) return;
                _age += Time.deltaTime; // game time: a frozen world holds the sound back
                float d = Distance(transform.position);
                float due = SoundPropagation.Delay(d) + _layer.extraDelay + (_layer.echo > 0f ? SoundPropagation.EchoDelay(d, _layer.echo) : 0f);
                if (_age >= due) Play(d);
                else if (_age > 30f) Destroy(gameObject);
            }
        }

        sealed class AbsorptionFollower : MonoBehaviour
        {
            AudioLowPassFilter _lp;
            float _next;

            void Update()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.25f;
                if (_lp == null) _lp = GetComponent<AudioLowPassFilter>();
                if (_lp != null) _lp.cutoffFrequency = SoundPropagation.Cutoff(Distance(transform.position));
            }
        }

        /// <summary>Loop, crackle and pops of a fire or cook-off, fading out at the end of a timed one.</summary>
        sealed class SustainedSound : MonoBehaviour
        {
            SoundBank _bank;
            AudioSource _loopSrc, _crackleSrc;
            float _left, _total, _nextPop, _loopVolume;

            public void Begin(SoundBank bank, float seconds, float strength)
            {
                _bank = bank;
                _left = _total = seconds > 0f ? seconds : float.PositiveInfinity;
                var loop = strength >= 1.2f && bank.loop.Length > 0 ? bank.loop[0] : SoundBank.Pick(bank.loop);
                _loopVolume = bank.volume * Mathf.Clamp01(0.5f + 0.28f * strength);
                _loopSrc = Loop(loop, _loopVolume, bank.minDistance * Mathf.Max(0.5f, strength));
                _crackleSrc = Loop(SoundBank.Pick(bank.crackle), 0.5f * bank.volume, 8f);
                _nextPop = Random.Range(0.3f, 1.2f);
                var main = _loopSrc != null ? _loopSrc : _crackleSrc;
                if (main != null) Follower(main);
            }

            // The host is a pooled effect: when it is pooled again the sound goes with it.
            void OnDisable() => Destroy(gameObject);

            AudioSource Loop(AudioClip clip, float volume, float min)
            {
                if (clip == null) return null;
                var src = gameObject.AddComponent<AudioSource>();
                src.clip = clip;
                src.loop = true;
                src.volume = volume;
                src.pitch = Random.Range(0.92f, 1.05f);
                Configure(src, min);
                src.time = Random.Range(0f, clip.length);
                src.Play();
                return src;
            }

            void Update()
            {
                float dt = Time.deltaTime;
                if (dt <= 0f || _bank == null) return;
                _left -= dt;
                float fade = float.IsInfinity(_total) ? 1f : Mathf.Clamp01(_left / Mathf.Max(1f, _total * 0.3f));
                if (_loopSrc != null) _loopSrc.volume = _loopVolume * fade;
                if (_crackleSrc != null) _crackleSrc.volume = 0.5f * _bank.volume * fade;
                _nextPop -= dt;
                if (_nextPop <= 0f && _left > 0f && (_bank.pops.Length > 0 || _bank.bangs.Length > 0))
                {
                    _nextPop = Random.Range(0.28f, 1.78f) * _bank.popInterval;
                    // Mostly pops of rounds and primers; now and then a charge or shell goes with a bang.
                    bool bang = _bank.bangs.Length > 0 && (_bank.pops.Length == 0 || Random.value < _bank.bangChance);
                    Emit(transform.position + Random.insideUnitSphere * 0.8f, new Layer
                    {
                        clip = SoundBank.Pick(bang ? _bank.bangs : _bank.pops), volume = (bang ? 0.9f : 0.6f) * fade, minDistance = bang ? 14f : 8f,
                        pitch = Random.Range(0.85f, 1.2f), near = true, size = bang ? 0.5f : 0.1f
                    });
                }
                if (_left < -1f) Destroy(gameObject);
            }
        }
    }
}
