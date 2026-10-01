using System.Collections.Generic;
using System.IO;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Vehicles;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Effects review in play mode: sets up fixed views of the first target vehicle (FPV drone camera with its video
    /// feed, a ground camera, the recon drone's zoom camera), sets off a scenario and saves frame series at fixed
    /// game times to Temp/FxReview/&lt;tag&gt;/. Game time advances in fixed 1/30 s steps (Time.captureDeltaTime), so
    /// series are repeatable and independent of the editor's frame rate. Driven from the Unity CLI (eval).
    /// Scenarios: side (shaped charge on the hull side), ammo (ammunition detonation), ground (burst on the ground
    /// beside the vehicle), fire (fuel fire), cookoff (propellant fire that blows the hatches).
    /// </summary>
    public static class FxReview
    {
        const float Step = 1f / 30f;

        static readonly List<(string name, Camera cam, bool fpv)> Views = new List<(string, Camera, bool)>();
        static readonly List<float> Times = new List<float>();
        static string _dir, _scenario;
        static int _frame, _next;
        static float _t0;
        static VehicleDamageModel _target;
        static RenderTexture _rt;
        static Vector3 _point, _dir3;
        public static bool Busy { get; private set; }
        /// <summary>Debug: render only the particle systems whose name contains this (null = all).</summary>
        public static string Only;
        public static string Status { get; private set; } = "idle";

        /// <summary>Starts a capture; returns at once (poll <see cref="Status"/>).</summary>
        public static string Begin(string scenario, string tag, string views = "fpv,ground,recon", string times = "0,0.05,0.15,0.4,1,2,4,8,15",
            float fpvRange = 30f, float yaw = 0f, int width = 1280, int height = 720)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            Cleanup();
            _scenario = scenario;
            _dir = Path.Combine("Temp/FxReview", tag);
            Directory.CreateDirectory(_dir);
            Times.Clear();
            foreach (var s in times.Split(',')) Times.Add(float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

            _target = Object.FindAnyObjectByType<VehicleDamageModel>();
            foreach (var v in Object.FindObjectsByType<VehicleDamageModel>())
                if (v.name.Contains("№1")) _target = v;
            var mission = Object.FindAnyObjectByType<ModernDrone.Missions.MissionController>();
            if (mission != null) mission.enabled = false;

            Vector3 centre = _target.transform.position + Vector3.up * 1.4f;
            // Views look at the target's flank (its local +x side) unless turned by yaw.
            Vector3 side = Quaternion.Euler(0f, yaw, 0f) * -_target.transform.right;
            var drone = Object.FindAnyObjectByType<DroneController>();
            var fpvCam = drone.GetComponentInChildren<DroneCamera>().GetComponent<Camera>();
            Views.Clear();
            foreach (var v in views.Split(','))
            {
                if (v == "fpv")
                {
                    var rb = drone.GetComponent<Rigidbody>();
                    rb.isKinematic = true;
                    Vector3 p = centre + side * fpvRange + Vector3.up * fpvRange * 0.3f;
                    float tilt = drone.GetComponentInChildren<DroneCamera>().TiltDeg;
                    drone.transform.SetPositionAndRotation(p, Quaternion.LookRotation(centre - p) * Quaternion.Euler(tilt, 0f, 0f));
                    Views.Add(("fpv", fpvCam, true));
                }
                else
                {
                    bool recon = v == "recon";
                    float range = recon ? 350f : 38f, up = recon ? 130f : 1.7f;
                    Vector3 dir = Quaternion.Euler(0f, recon ? 35f : -20f, 0f) * side;
                    Vector3 p = centre + dir * Mathf.Sqrt(range * range - up * up);
                    if (Physics.Raycast(p + Vector3.up * 500f, Vector3.down, out var g, 1000f, ModernDrone.Core.GameLayers.TerrainMask)) p.y = g.point.y + up;
                    var go = new GameObject("FxReview_" + v);
                    var cam = go.AddComponent<Camera>();
                    cam.CopyFrom(fpvCam);
                    var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    var src = fpvCam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (src != null) { data.renderPostProcessing = src.renderPostProcessing; data.antialiasing = src.antialiasing; }
                    go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(centre + Vector3.up * (recon ? 4f : 2.5f) - p));
                    // Recon: about 45 m of frame width at the target; ground: a wide lens.
                    cam.fieldOfView = recon ? 2f * Mathf.Atan(45f * 0.5f / ((float)width / height) / Vector3.Distance(p, centre)) * Mathf.Rad2Deg : 55f;
                    cam.enabled = false;
                    Views.Add((v, cam, false));
                }
            }
            _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "FxReview" };
            Time.captureDeltaTime = Step;
            _frame = 0;
            _next = 0;
            Busy = true;
            Status = "running";
            EditorApplication.update += Tick;
            return $"capturing {scenario} -> {_dir}";
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { Cleanup(); Status = "stopped"; return; }
            _frame++;
            if (_frame == 15) { Trigger(); _t0 = Time.time; }
            if (_frame < 15) return;
            float t = Time.time - _t0;
            while (_next < Times.Count && t >= Times[_next] - 1e-4f)
            {
                foreach (var v in Views) Capture(v.name, v.cam, v.fpv, Times[_next]);
                _next++;
            }
            if (_next >= Times.Count) { Cleanup(); Status = "done " + _dir; }
        }

        static void Trigger()
        {
            switch (_scenario)
            {
                case "side":
                case "ground":
                {
                    var w = AssetDatabase.LoadAssetAtPath<WarheadData>(_scenario == "side"
                        ? "Assets/_Project/Data/Warheads/ShapedCharge_Basic.asset" : "Assets/_Project/Data/Warheads/Fragmentation_Basic.asset");
                    Vector3 c = _target.transform.position + Vector3.up * 1.1f;
                    Vector3 from = c - _target.transform.right * 6f;
                    if (_scenario == "ground") { from = c - _target.transform.right * 6f + Vector3.up * 3f; c -= _target.transform.right * 5f + Vector3.up * 2f; }
                    _dir3 = (c - from).normalized;
                    _point = Physics.Raycast(from, _dir3, out var hit, 20f, ModernDrone.Core.GameLayers.FuzeTriggerMask) ? hit.point : c;
                    var report = DamageSolver.Detonate(w, _point, _dir3, new System.Random(7));
                    if (!report.dud) SimpleVfx.Explosion(_point, 1f);
                    break;
                }
                case "blast":
                case "blastsmall":
                    // The effect alone beside the vehicle (no damage): for tuning the look.
                    SimpleVfx.Explosion(_target.transform.position - _target.transform.right * 7f + Vector3.up * 1.5f,
                        _scenario == "blast" ? 2.5f : 1f, _scenario == "blast" ? ExplosionKind.Ammo : ExplosionKind.Air);
                    break;
                case "ammo":
                    typeof(VehicleDamageModel).GetMethod("Destroy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(_target, new object[] { DestructionCause.AmmoDetonation });
                    break;
                case "fire":
                case "cookoff":
                {
                    foreach (var m in _target.Modules)
                        if (m.Type == (_scenario == "fire" ? ModuleType.FuelTank : ModuleType.AmmoRack)) { _target.SetAmmoConfinement(0f); _target.Ignite(m); break; }
                    break;
                }
            }
        }

        static readonly int FpvEnabled = Shader.PropertyToID("_FpvEnabled");

        static void Capture(string view, Camera cam, bool fpv, float time)
        {
            var prevTarget = cam.targetTexture;
            float prevFpv = Shader.GetGlobalFloat(FpvEnabled);
            Shader.SetGlobalFloat(FpvEnabled, fpv ? 1f : 0f);
            var hidden = new List<Renderer>();
            if (!string.IsNullOrEmpty(Only))
                foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>())
                    if (r.enabled && !r.name.Contains(Only)) { r.enabled = false; hidden.Add(r); }
            cam.targetTexture = _rt;
            cam.Render();
            foreach (var r in hidden) r.enabled = true;
            cam.targetTexture = prevTarget;
            Shader.SetGlobalFloat(FpvEnabled, prevFpv);
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_dir, view + "_" + time.ToString("00.00", System.Globalization.CultureInfo.InvariantCulture) + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void Cleanup()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            foreach (var v in Views) if (!v.fpv && v.cam != null) Object.Destroy(v.cam.gameObject);
            Views.Clear();
            if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); _rt = null; }
            Busy = false;
        }
    }
}
