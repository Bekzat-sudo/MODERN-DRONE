using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.Vehicles;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Night scene with thermal FPV cameras. Emulates two behaviours of uncooled thermal cores
    /// (thermal-image.com; flyeye.io on FFC):
    /// - AGC: gain drops when hot objects fill more of the frame, so the background darkens as a target comes close;
    /// - shutter calibration (NUC/FFC): every one to two and a half minutes the picture freezes for ~0.4 s with a click,
    ///   and the cloudy non-uniformity that built up since the last calibration is cleared.
    /// </summary>
    public sealed class ThermalNight : MonoBehaviour
    {
        const float MaxGain = 14f;
        const float MinGain = 5f;

        HeatSignature[] _hot;
        DroneCamera _fpv;
        Camera _fpvCam;
        float _nextNuc;

        void Awake()
        {
            NightMode.Night = true;
            NightMode.Thermal = true; // until the loadout fits a camera
            NightMode.ThermalGain = MaxGain;
            NightMode.ThermalDrift = 0f;
        }

        void Start()
        {
            // The ambient probe is otherwise still the daytime sky: rebuild it from the night ambient colour.
            var probe = new UnityEngine.Rendering.SphericalHarmonicsL2();
            probe.AddAmbientLight(RenderSettings.ambientLight);
            RenderSettings.ambientProbe = probe;
            RenderSettings.reflectionIntensity = 0f;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.005f, 0.005f, 0.01f);
            }
            _hot = FindObjectsByType<HeatSignature>(FindObjectsSortMode.None);
            _fpv = FindAnyObjectByType<DroneCamera>();
            _fpvCam = _fpv != null ? _fpv.GetComponent<Camera>() : null;
            _nextNuc = Time.time + Random.Range(60f, 150f);
        }

        void Update()
        {
            if (_fpvCam == null || GamePause.IsPaused) return;

            float target = Mathf.Lerp(MaxGain, MinGain, Mathf.Clamp01(HotFraction() * 5f));
            NightMode.ThermalGain = Mathf.Lerp(NightMode.ThermalGain, target, 1f - Mathf.Exp(-Time.deltaTime / 0.8f));
            NightMode.ThermalDrift = Mathf.Clamp01(NightMode.ThermalDrift + Time.deltaTime / 120f);

            if (Time.time >= _nextNuc)
            {
                _nextNuc = Time.time + Random.Range(60f, 150f);
                _fpv.Freeze(0.4f);
                NightMode.ThermalDrift = 0f;
                SimpleAudio.Click(_fpv.transform.position);
            }
        }

        /// <summary>Approximate share of the FPV frame covered by warm vehicles (screen-space bounds).</summary>
        // ponytail: ignores occlusion (a vehicle behind trees still lowers the gain); add a raycast check if it matters.
        float HotFraction()
        {
            float sum = 0f;
            foreach (var h in _hot)
            {
                if (h == null || !h.gameObject.activeInHierarchy) continue;
                var r = h.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                var b = r.bounds;
                foreach (var other in h.GetComponentsInChildren<Renderer>()) b.Encapsulate(other.bounds);
                Vector3 vpMin = new Vector3(float.MaxValue, float.MaxValue, 0f), vpMax = new Vector3(float.MinValue, float.MinValue, 0f);
                bool inFront = false;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var vp = _fpvCam.WorldToViewportPoint(corner);
                    if (vp.z <= 0f) continue;
                    inFront = true;
                    vpMin = Vector3.Min(vpMin, vp);
                    vpMax = Vector3.Max(vpMax, vp);
                }
                if (!inFront) continue;
                float w = Mathf.Clamp01(vpMax.x) - Mathf.Clamp01(vpMin.x);
                float hgt = Mathf.Clamp01(vpMax.y) - Mathf.Clamp01(vpMin.y);
                sum += Mathf.Max(0f, w) * Mathf.Max(0f, hgt);
            }
            return sum;
        }

        void OnDestroy() => NightMode.Night = NightMode.Thermal = false;
    }
}
