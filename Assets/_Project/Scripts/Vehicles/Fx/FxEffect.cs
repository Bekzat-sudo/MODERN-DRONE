using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// A pooled effect instance: particle systems (configured once, re-played), its lights, and actions that must wait
    /// until the world is running. Everything ages in game time, so a world frozen at the moment of impact (killcam
    /// replay, timeScale 0) keeps the effect at its first instant: particles do not simulate, lights stay dark, debris,
    /// decals and blast waves are not released yet. Size changes are applied by scaling each system's start size,
    /// speed and emitter shape from their template values.
    /// </summary>
    public sealed class FxEffect : MonoBehaviour
    {
        struct Template
        {
            public ParticleSystem ps;
            public float size, speed, radius, rate;
            public Vector3 boxScale;
        }

        public string Key { get; internal set; }
        public float Age { get; private set; }
        public bool Started { get; private set; }
        /// <summary>Seconds of game time before the effect returns to its pool (0 = until <see cref="Release"/>).</summary>
        public float Lifetime { get; set; }
        public float Scale { get; private set; } = 1f;

        readonly List<Template> _systems = new List<Template>();
        readonly List<FxLight> _lights = new List<FxLight>();
        readonly List<Action<FxEffect>> _onStart = new List<Action<FxEffect>>();
        public event Action<FxEffect> Released;

        /// <summary>Records the systems' template values (call once after building the effect).</summary>
        internal void Capture()
        {
            _systems.Clear();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var shape = ps.shape;
                _systems.Add(new Template
                {
                    ps = ps, size = main.startSizeMultiplier, speed = main.startSpeedMultiplier, radius = shape.radius,
                    rate = ps.emission.rateOverTimeMultiplier, boxScale = shape.scale
                });
            }
            _lights.Clear();
            GetComponentsInChildren(true, _lights);
        }

        public IReadOnlyList<FxLight> Lights => _lights;

        /// <summary>Resizes the effect (1 = as built). Speeds scale with the square root: bigger bursts are not proportionally faster.</summary>
        public void SetScale(float scale, float rateScale = 1f)
        {
            Scale = scale;
            foreach (var t in _systems)
            {
                var main = t.ps.main;
                main.startSizeMultiplier = t.size * scale;
                main.startSpeedMultiplier = t.speed * Mathf.Sqrt(scale);
                var shape = t.ps.shape;
                shape.radius = t.radius * scale;
                shape.scale = t.boxScale * scale;
                var em = t.ps.emission;
                em.rateOverTimeMultiplier = t.rate * rateScale;
            }
            foreach (var l in _lights) l.SetScale(scale);
        }

        /// <summary>Runs <paramref name="action"/> on the first frame the world moves (or now, if it already does).</summary>
        public void OnStart(Action<FxEffect> action)
        {
            if (Started) action(this);
            else _onStart.Add(action);
        }

        internal void Play()
        {
            Age = 0f;
            Started = false;
            _onStart.Clear();
            gameObject.SetActive(true);
            foreach (var t in _systems)
            {
                t.ps.Clear(false);
                t.ps.Play(false);
            }
            foreach (var l in _lights) l.Restart();
        }

        /// <summary>Stops emitting; the effect is pooled again once its particles are gone (or after <paramref name="linger"/> s).</summary>
        public void StopEmitting(float linger = 12f)
        {
            foreach (var t in _systems) t.ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            foreach (var l in _lights) l.FadeOut(1.5f);
            Lifetime = Age + linger;
        }

        public void Release()
        {
            Released?.Invoke(this);
            Released = null;
            FxPool.Return(this);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!Started)
            {
                Started = true;
                for (int i = 0; i < _onStart.Count; i++) _onStart[i](this);
                _onStart.Clear();
            }
            Age += dt;
            if (Lifetime > 0f && Age >= Lifetime) Release();
        }

        // Destroyed with whatever it was attached to (a vehicle removed from the scene).
        void OnDestroy() => FxPool.Forget(this);
    }

    /// <summary>
    /// Reuses effect instances per kind instead of building particle systems for every burst. Each kind has a cap; past
    /// it the oldest live instance is recycled (a 30-strike barrage keeps a bounded particle count).
    /// </summary>
    public static class FxPool
    {
        sealed class Kind
        {
            public Func<GameObject> build;
            public int max;
            public readonly Stack<FxEffect> free = new Stack<FxEffect>();
            public readonly List<FxEffect> live = new List<FxEffect>();
        }

        static readonly Dictionary<string, Kind> _kinds = new Dictionary<string, Kind>();
        static Transform _root;

        public static bool Has(string key) => _kinds.ContainsKey(key);

        public static int LiveCount(string key) => _kinds.TryGetValue(key, out var k) ? k.live.Count : 0;

        public static void Register(string key, Func<GameObject> build, int max)
        {
            if (!_kinds.ContainsKey(key)) _kinds[key] = new Kind { build = build, max = Mathf.Max(1, max) };
        }

        /// <summary>A playing instance of <paramref name="key"/> at the pose, optionally parented (it then follows a vehicle).</summary>
        public static FxEffect Spawn(string key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var kind = _kinds[key];
            kind.live.RemoveAll(f => f == null);
            FxEffect fx = null;
            while (kind.free.Count > 0 && fx == null) fx = kind.free.Pop();
            if (fx == null && kind.live.Count >= kind.max)
            {
                kind.live[0].Release(); // the oldest goes back to the free stack...
                fx = kind.free.Count > 0 ? kind.free.Pop() : null; // ...and is reused
            }
            if (fx == null)
            {
                var go = kind.build();
                go.SetActive(false);
                if (!go.TryGetComponent(out fx)) fx = go.AddComponent<FxEffect>();
                fx.Key = key;
                fx.Capture();
            }
            fx.transform.SetParent(parent, false);
            fx.transform.SetPositionAndRotation(position, rotation);
            fx.Lifetime = 0f;
            fx.SetScale(1f);
            kind.live.Add(fx);
            fx.Play();
            return fx;
        }

        internal static void Forget(FxEffect fx)
        {
            if (fx.Key != null && _kinds.TryGetValue(fx.Key, out var k)) k.live.Remove(fx);
        }

        internal static void Return(FxEffect fx)
        {
            if (fx == null || !_kinds.TryGetValue(fx.Key ?? "", out var kind)) return;
            if (!kind.live.Remove(fx)) return;
            if (_root == null)
            {
                _root = new GameObject("FxPool").transform;
                _root.gameObject.SetActive(false);
            }
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            fx.transform.SetParent(_root, false);
            fx.gameObject.SetActive(false);
            kind.free.Push(fx);
        }

        /// <summary>Forgets every pooled instance (scene change: they were destroyed with the scene).</summary>
        public static void Reset()
        {
            foreach (var k in _kinds.Values) { k.free.Clear(); k.live.Clear(); }
            _root = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _kinds.Clear();
            _root = null;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += _ => Reset();
        }
    }
}
