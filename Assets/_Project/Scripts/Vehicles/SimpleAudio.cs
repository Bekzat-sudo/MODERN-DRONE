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
    /// 300 m, eprints.soton.ac.uk SA80 study). The listener is the drone's microphone or the recon camera.
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

        static AudioClip[] _close, _far, _small, _mg, _cannon, _metal, _pops, _fire;
        static AudioClip _debrisMetal, _debrisDirt, _jet, _burst, _crackle, _crack;

        static void Load()
        {
            if (_close != null) return;
            _close = Takes("exp_close", 3);
            _far = Takes("exp_far", 4);
            _small = Takes("exp_small", 3);
            _mg = Takes("mg", 4);
            _cannon = Takes("cannon", 3);
            _metal = Takes("metal", 2);
            _pops = Takes("pop", 2);
            _fire = new[] { Resources.Load<AudioClip>("Audio/fire_large"), Resources.Load<AudioClip>("Audio/fire_fuel"), Resources.Load<AudioClip>("Audio/fire_car") };
            _fire = System.Array.FindAll(_fire, c => c != null);
            _debrisMetal = Resources.Load<AudioClip>("Audio/debris_metal");
            _debrisDirt = Resources.Load<AudioClip>("Audio/debris_dirt");
            _jet = Resources.Load<AudioClip>("Audio/flame_jet");
            _burst = Resources.Load<AudioClip>("Audio/flame_burst");
            _crackle = Resources.Load<AudioClip>("Audio/cookoff_crackle");
            _crack = Resources.Load<AudioClip>("Audio/crack");
        }

        static AudioClip Pick(AudioClip[] clips) => clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];

        public static void Explosion(Vector3 position, float size) => Explosion(position, size, ExplosionKind.Auto);

        public static void Explosion(Vector3 position, float size, ExplosionKind kind)
        {
            Load();
            float k = Mathf.Pow(Mathf.Max(0.05f, size), 1f / 3f);
            bool small = kind == ExplosionKind.Era || size < 0.7f;
            bool big = kind == ExplosionKind.Ammo || size >= 2f;
            // Bigger charges sound deeper.
            float pitch = Mathf.Lerp(1.12f, 0.82f, Mathf.InverseLerp(0.3f, 3f, size)) * Random.Range(0.95f, 1.05f);
            float min = 22f * k;
            var near = new Layer { clip = Pick(small ? _small : _close), volume = small ? 0.8f : 1f, minDistance = min, pitch = pitch, near = true, size = size };
            var far = new Layer { clip = Pick(_far), volume = big ? 1f : 0.85f, minDistance = min * 1.6f, pitch = pitch * 0.95f, far = true, size = size };
            Emit(position, near);
            Emit(position, far);
            // Debris raining down: dirt on ground bursts, metal off a vehicle.
            var debris = kind == ExplosionKind.Ground ? _debrisDirt : kind == ExplosionKind.Ammo || kind == ExplosionKind.Armor ? _debrisMetal : null;
            if (debris != null && !small)
                Emit(position, new Layer { clip = debris, volume = 0.55f, minDistance = 10f * k, pitch = Random.Range(0.9f, 1.1f), extraDelay = Random.Range(0.5f, 1.2f), near = true, size = size });
            // First echo off the ground and tree lines a couple of hundred metres beyond (abstract reflector distance).
            Emit(position, new Layer { clip = Pick(_far), volume = 0.35f, minDistance = min * 1.6f, pitch = pitch * 0.9f, echo = Random.Range(120f, 320f), far = true, size = size });
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

        /// <summary>Roaring, crackling fire loop attached to a fire effect (removed with it).</summary>
        public static AudioSource FireLoop(Transform parent, float size)
        {
            Load();
            var clip = size >= 1.2f && _fire.Length > 0 ? _fire[0] : Pick(_fire);
            if (clip == null) return null;
            var src = parent.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = Mathf.Clamp01(0.45f + 0.25f * size);
            src.pitch = Random.Range(0.92f, 1.06f);
            Configure(src, 5f * size);
            Follower(src);
            src.time = Random.Range(0f, clip.length);
            src.Play();
            return src;
        }

        /// <summary>
        /// Propellant venting: a burst as the jet lights, a roaring torch for <paramref name="seconds"/>, and the crackle
        /// and pops of charges and rounds cooking off (secondary bangs now and then).
        /// </summary>
        public static void FlameJet(Transform parent, float seconds, float strength)
        {
            Load();
            if (_burst != null)
                Emit(parent.position, new Layer { clip = _burst, volume = 0.9f, minDistance = 12f, pitch = Random.Range(0.9f, 1.05f), near = true, size = strength });
            var host = new GameObject("SFX_CookOff");
            host.transform.SetParent(parent, false);
            var c = host.AddComponent<CookOffSound>();
            c.Begin(seconds, strength);
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

        /// <summary>Torch roar and crackle of a cook-off, with pops and the odd secondary bang, fading out at the end.</summary>
        sealed class CookOffSound : MonoBehaviour
        {
            AudioSource _roarSrc, _crackleSrc;
            float _left, _total, _nextPop, _strength;

            public void Begin(float seconds, float strength)
            {
                _left = _total = seconds;
                _strength = strength;
                _roarSrc = Loop(_jet, Mathf.Clamp01(0.55f + 0.3f * strength), 10f);
                _crackleSrc = Loop(_crackle, 0.5f, 8f);
                _nextPop = Random.Range(0.3f, 1.2f);
                Follower(_roarSrc != null ? _roarSrc : _crackleSrc);
            }

            // The host is a pooled jet: when it is pooled again the sound goes with it.
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
                if (dt <= 0f) return;
                _left -= dt;
                float fade = Mathf.Clamp01(_left / Mathf.Max(1f, _total * 0.3f));
                if (_roarSrc != null) _roarSrc.volume = Mathf.Clamp01(0.55f + 0.3f * _strength) * fade;
                if (_crackleSrc != null) _crackleSrc.volume = 0.5f * fade;
                _nextPop -= dt;
                if (_nextPop <= 0f && _left > 0f)
                {
                    _nextPop = Random.Range(0.25f, 1.6f);
                    // Mostly pops of rounds and primers; now and then a charge or shell goes with a bang.
                    bool bang = Random.value < 0.12f;
                    Emit(transform.position + Random.insideUnitSphere * 0.8f, new Layer
                    {
                        clip = Pick(bang ? _small : _pops), volume = bang ? 0.9f : 0.6f, minDistance = bang ? 14f : 8f,
                        pitch = Random.Range(0.85f, 1.2f), near = true, size = bang ? 0.5f : 0.1f
                    });
                }
                if (_left < -1f) Destroy(gameObject);
            }
        }
    }
}
