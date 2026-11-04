using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Light thrown on the ground and hulls by an effect. Flash: peak within milliseconds, gone in a tenth of a second
    /// (the visible fireball of a detonation lasts ~100 ms with its brightest light in the first milliseconds,
    /// arxiv.org 2010.13537). Glow: follows a burning fireball for its life. Fire: flickers at 2-12 Hz around its level
    /// (flame flicker of pool fires is ~1.5/sqrt(D) Hz, a few Hz for metre-scale fires, Pagni 1990 / Cetegen; the faster
    /// shimmer is a look choice). Ages in game time: dark while the world is frozen at the moment of impact.
    /// Intensities are look values for the scene's exposure (abstract).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class FxLight : MonoBehaviour
    {
        public enum Mode { Flash, Glow, Fire }

        [SerializeField] Mode mode;
        [SerializeField] float intensity = 40f;
        [SerializeField] float range = 20f;
        [SerializeField] float duration = 0.12f;
        [SerializeField] float delay;

        Light _light;
        float _age, _fade = -1f, _fadeLength = 1f, _scale = 1f, _seed;

        public float Level { get; set; } = 1f;

        public void Configure(Mode m, float peak, float lightRange, float seconds, Color color, float startDelay = 0f)
        {
            mode = m;
            intensity = peak;
            range = lightRange;
            duration = seconds;
            delay = startDelay;
            _light = GetComponent<Light>();
            _light.type = LightType.Point;
            _light.color = color;
            _light.shadows = LightShadows.None;
            _light.renderMode = LightRenderMode.ForcePixel;
            _light.intensity = 0f;
        }

        public void SetDelay(float seconds) => delay = seconds;

        public void SetScale(float scale)
        {
            _scale = scale;
            if (_light == null) _light = GetComponent<Light>();
            _light.range = range * Mathf.Sqrt(scale);
        }

        public void Restart()
        {
            if (_light == null) _light = GetComponent<Light>();
            _age = 0f;
            _fade = -1f;
            Level = 1f;
            _seed = Random.value * 100f;
            _light.intensity = 0f;
            _light.enabled = true;
        }

        public void FadeOut(float seconds)
        {
            if (_fade < 0f) { _fade = 0f; _fadeLength = Mathf.Max(0.01f, seconds); }
        }

        /// <summary>Brightness (0..1 of peak) <paramref name="t"/> seconds after start.</summary>
        public static float Curve(Mode mode, float t, float duration, float seed)
        {
            if (t < 0f) return 0f;
            switch (mode)
            {
                case Mode.Flash:
                    return Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t / Mathf.Max(0.005f, duration * 0.35f));
                case Mode.Glow:
                    return Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01(1f - t / Mathf.Max(0.01f, duration)) *
                           (0.85f + 0.15f * Mathf.PerlinNoise(seed, t * 9f));
                default:
                    float slow = Mathf.PerlinNoise(seed, t * 2.2f), fast = Mathf.PerlinNoise(t * 11f, seed);
                    return Mathf.Clamp01(t / 0.6f) * (0.55f + 0.3f * slow + 0.25f * fast);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _age += dt;
            float k = Curve(mode, _age - delay, duration, _seed) * Level;
            if (_fade >= 0f)
            {
                _fade += dt;
                k *= Mathf.Clamp01(1f - _fade / _fadeLength);
            }
            _light.intensity = intensity * Mathf.Sqrt(_scale) * k;
            _light.enabled = _light.intensity > 0.01f;
        }
    }
}
