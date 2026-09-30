using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Video
{
    /// <summary>
    /// Camera auto exposure (URP has none built in). A centre-weighted average of log luminance, metered from a 64 x 36
    /// copy of the frame read back asynchronously, drives a spring towards the exposure that brings the scene to the
    /// reference level; the result goes into post exposure through a runtime volume.
    /// A small CMOS FPV camera adapts within a fraction of a second and visibly overshoots (brightness "pumping" when the
    /// view swings from sky to ground); a gimbal zoom camera adapts slowly and smoothly. Spring rates are presentation
    /// tuning, not measured camera data.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class AutoExposure : MonoBehaviour
    {
        const int Width = 64, Height = 36;

        [Tooltip("Post exposure (EV) when the metered scene sits at the reference luminance.")]
        [SerializeField] float baseExposure = 0.3f;
        [Tooltip("Geometric-mean scene luminance treated as correctly exposed (metered: sunlit field from 50 m up 0.20, partly cloudy sky 0.27).")]
        [SerializeField] float referenceLuminance = 0.2f;
        [SerializeField] float minExposure = -1.5f;
        [Tooltip("Upper limit is small so night scenes stay dark.")]
        [SerializeField] float maxExposure = 0.7f;
        [Tooltip("Spring natural frequency, rad/s: higher adapts faster.")]
        [SerializeField] float frequency = 5f;
        [Tooltip("Spring damping ratio: below 1 overshoots.")]
        [SerializeField] float damping = 0.55f;

        static Volume _volume;
        static ColorAdjustments _adjust;

        public static AutoExposure Active { get; private set; }
        public Camera Camera { get; private set; }
        public RTHandle Target { get; private set; }

        /// <summary>Metered geometric-mean luminance (-1 before the first reading), for tuning.</summary>
        public float Metered { get; private set; } = -1f;
        public float Exposure => _ev;

        float _ev, _velocity, _targetEv;
        bool _pending, _primed;

        public void Configure(float frequency, float damping)
        {
            this.frequency = frequency;
            this.damping = damping;
        }

        void Awake()
        {
            Camera = GetComponent<Camera>();
            _ev = _targetEv = baseExposure;
        }

        void OnEnable() => Target ??= RTHandles.Alloc(Width, Height, GraphicsFormat.R16G16B16A16_SFloat, name: "AutoExposureMeter");

        void OnDisable()
        {
            if (Active == this) Active = null;
            _primed = false; // a camera switched back on snaps to its scene instead of fading from a stale exposure
        }

        void OnDestroy()
        {
            Target?.Release();
            Target = null;
        }

        void Update()
        {
            if (!Camera.enabled) { if (Active == this) Active = null; return; }
            Active = this;

            if (!_pending)
            {
                _pending = true;
                AsyncGPUReadback.Request(Target.rt, 0, OnReadback);
            }

            if (!_primed) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _velocity += ((_targetEv - _ev) * frequency * frequency - 2f * damping * frequency * _velocity) * dt;
            _ev += _velocity * dt;
            Apply(_ev);
        }

        void OnReadback(AsyncGPUReadbackRequest request)
        {
            _pending = false;
            if (this == null || request.hasError) return;
            Metered = CentreWeightedLogAverage(request.GetData<ushort>(), Width, Height);
            _targetEv = TargetExposure(Metered, referenceLuminance, baseExposure, minExposure, maxExposure);
            if (_primed) return;
            _ev = _targetEv;
            _velocity = 0f;
            _primed = true;
        }

        /// <summary>Exposure that maps the metered luminance to the reference, clamped.</summary>
        public static float TargetExposure(float metered, float reference, float baseEv, float min, float max)
            => baseEv + Mathf.Clamp(Mathf.Log(reference / Mathf.Max(metered, 1e-4f), 2f), min, max);

        /// <summary>Geometric mean of luminance over RGBA half-float pixels, weighted towards the centre (Gaussian).</summary>
        public static float CentreWeightedLogAverage(NativeArray<ushort> rgbaHalf, int width, int height)
        {
            double sum = 0, weights = 0;
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height - 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width - 0.5f;
                    int i = (y * width + x) * 4;
                    float lum = 0.2126f * Mathf.HalfToFloat(rgbaHalf[i]) + 0.7152f * Mathf.HalfToFloat(rgbaHalf[i + 1]) + 0.0722f * Mathf.HalfToFloat(rgbaHalf[i + 2]);
                    if (float.IsNaN(lum) || float.IsInfinity(lum)) continue;
                    float w = Mathf.Exp(-(u * u + v * v) * 8f);
                    sum += w * Mathf.Log(Mathf.Max(lum, 1e-4f));
                    weights += w;
                }
            }
            return weights > 0 ? Mathf.Exp((float)(sum / weights)) : 0f;
        }

        static void Apply(float ev)
        {
            if (_volume == null)
            {
                var go = new GameObject("AutoExposureVolume") { hideFlags = HideFlags.DontSave };
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 100f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.DontSave;
                _adjust = profile.Add<ColorAdjustments>();
                _adjust.postExposure.overrideState = true;
                _volume.sharedProfile = profile;
            }
            _adjust.postExposure.value = ev;
        }
    }
}
