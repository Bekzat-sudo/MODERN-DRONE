using System;
using System.Collections;
using System.Text;
using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Repeatable performance benchmark for player builds (editor timings are not representative for the GPU Resident
    /// Drawer). Start the build with <c>-benchmark [-benchmarkOut path]</c>: no mission is flown, the drone is parked at
    /// fixed poses over the scene and frame timings (FrameTimingManager: CPU main / render thread, GPU) are averaged
    /// per case, written to a text report, then the player quits. Needs "Frame Timing Stats" in the build
    /// (Modern Drone/Build/Benchmark Player sets it). With <c>-benchmarkParts</c> it instead measures what each part
    /// of the world costs: the heaviest poses with trees, props, village, grass, decals or sun shadows switched off
    /// one at a time.
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        const int Frames = 300;

        struct Case { public string name; public Vector3 pos; public Vector3 euler; public float height; public bool trees; public string off; }

        // Poses over Mission_01: tree belt at z = 1300 with the targets in front of it.
        static readonly Case[] Cases =
        {
            new Case { name = "fpv over belts", pos = new Vector3(930f, 0f, 1130f), height = 22f, euler = new Vector3(12f, 10f, 0f), trees = true },
            new Case { name = "fpv over belts, trees hidden", pos = new Vector3(930f, 0f, 1130f), height = 22f, euler = new Vector3(12f, 10f, 0f), trees = false },
            new Case { name = "low along belt", pos = new Vector3(700f, 0f, 1275f), height = 4f, euler = new Vector3(3f, 90f, 0f), trees = true },
            new Case { name = "high overview", pos = new Vector3(960f, 0f, 950f), height = 120f, euler = new Vector3(25f, 0f, 0f), trees = true },
            new Case { name = "village flyover", pos = new Vector3(1560f, 0f, 960f), height = 25f, euler = new Vector3(15f, 70f, 0f), trees = true },
        };

        static readonly string[] Parts = { "", "trees", "props", "village", "grass", "decals", "shadows", "post" };

        // Shadow variants (-benchmarkShadows): cascade count, distance, which trees / plants cast.
        static readonly string[] ShadowVariants = { "", "shadows", "small cull", "small cull strong", "cascades 3", "small cull + cascades 3", "cascades 3 + res 2048" };

        static Case[] PartCases(string[] parts)
        {
            var list = new System.Collections.Generic.List<Case>();
            foreach (var pose in new[] { Cases[0], Cases[2], Cases[4] })
                foreach (var part in parts)
                {
                    var c = pose;
                    c.off = part;
                    c.name = pose.name + (part == "" ? "" : parts == Parts || part == "shadows" ? ", no " + part : ", " + part);
                    list.Add(c);
                }
            return list.ToArray();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmark") >= 0)
                new GameObject("Benchmark").AddComponent<Benchmark>();
        }

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            yield return new WaitForSecondsRealtime(3f);

            var mission = FindAnyObjectByType<MissionController>();
            if (mission != null) mission.enabled = false;
            var drone = FindAnyObjectByType<DroneController>();
            drone.GetComponent<Rigidbody>().isKinematic = true;
            var trees = GameObject.Find("Trees"); // TerrainTreeInstancer's root
            var terrain = Terrain.activeTerrain;

            var village = GameObject.Find("Village");
            var decals = FindObjectsByType<UnityEngine.Rendering.Universal.DecalProjector>(FindObjectsSortMode.None);
            var volumes = FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None);
            var props = GameObject.Find("Props");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmarkVeg") >= 0)
            {
                yield return Vegetation(drone, terrain);
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmarkFx") >= 0)
            {
                yield return Effects(drone, terrain);
                yield break;
            }
            bool parts = Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmarkParts") >= 0;
            bool shadows = Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmarkShadows") >= 0;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            int cascades0 = urp != null ? urp.shadowCascadeCount : 4;
            float distance0 = urp != null ? urp.shadowDistance : 350f;
            int res0 = urp != null ? urp.mainLightShadowmapResolution : 4096;
            var small0 = urp != null ? urp.shadowSmallMeshScreenPercentages : Vector4.zero;
            var proxies = new System.Collections.Generic.List<GameObject>();
            var castModes = new System.Collections.Generic.Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
            if (trees != null) foreach (var r in trees.GetComponentsInChildren<Renderer>(true)) castModes[r] = r.shadowCastingMode;
            var sb = new StringBuilder();
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  {SystemInfo.graphicsDeviceName}  {SystemInfo.processorType}  {Screen.width}x{Screen.height}  {Application.unityVersion}");
            sb.AppendLine("case | frame ms | CPU main ms | CPU render ms | GPU ms | fps");
            var timings = new FrameTiming[1];
            foreach (var c0 in parts ? PartCases(Parts) : shadows ? PartCases(ShadowVariants) : Cases)
            {
                var c = c0;
                c.off ??= "";
                if (urp != null)
                {
                    urp.shadowCascadeCount = c.off.StartsWith("cascades 3") ? 3 : c.off == "cascades 2" ? 2 : cascades0;
                    urp.mainLightShadowmapResolution = c.off.EndsWith("res 2048") ? 2048 : res0;
                    urp.shadowCascadeCount = c.off.Contains("cascades 3") ? 3 : urp.shadowCascadeCount;
                    urp.shadowSmallMeshScreenPercentages = c.off.StartsWith("small cull strong") ? new Vector4(0f, 1f, 2f, 4f)
                        : c.off.StartsWith("small cull") ? new Vector4(0f, 0.5f, 1f, 2f) : small0;
                    urp.shadowDistance = c.off == "distance 250" ? 250f : c.off == "distance 200" ? 200f : distance0;
                }
                foreach (var kv in castModes) if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
                foreach (var pr in proxies) if (pr != null) Destroy(pr);
                proxies.Clear();
                if (c.off.EndsWith("proxy") && trees != null)
                    foreach (var g in trees.GetComponentsInChildren<LODGroup>(true))
                    {
                        var lods = g.GetLODs();
                        if (lods.Length < 2 || lods[0].renderers.Length == 0 || lods[1].renderers.Length == 0) continue;
                        var r0 = lods[0].renderers[0]; var r1 = lods[1].renderers[0];
                        if (r0 == null || r1 == null) continue;
                        r0.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        var pgo = new GameObject("ShadowProxy");
                        pgo.transform.SetParent(r0.transform, false);
                        pgo.AddComponent<MeshFilter>().sharedMesh = r1.GetComponent<MeshFilter>().sharedMesh;
                        var pmr = pgo.AddComponent<MeshRenderer>();
                        pmr.sharedMaterials = r1.sharedMaterials;
                        pmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                        var nl = lods[0].renderers; System.Array.Resize(ref nl, nl.Length + 1); nl[nl.Length - 1] = pmr; lods[0].renderers = nl;
                        g.SetLODs(lods);
                        proxies.Add(pgo);
                    }
                if ((c.off == "tree lod1 no cast" || c.off == "plants no cast") && trees != null)
                    foreach (var g in trees.GetComponentsInChildren<LODGroup>(true))
                    {
                        var lods = g.GetLODs();
                        if (c.off == "tree lod1 no cast" && lods.Length >= 2)
                            foreach (var r in lods[1].renderers) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        if (c.off == "plants no cast" && lods.Length == 1)
                            foreach (var r in lods[0].renderers) if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                // "props" = everything under Props except the village, which is its own part.
                if (props != null) foreach (Transform ch in props.transform) ch.gameObject.SetActive(c.off != "props" || ch.gameObject == village);
                if (village != null) village.SetActive(c.off != "village");
                terrain.detailObjectDistance = c.off == "grass" ? 0f : 150f;
                foreach (var d in decals) if (d != null) d.enabled = c.off != "decals";
                if (RenderSettings.sun != null) RenderSettings.sun.shadows = c.off == "shadows" ? LightShadows.None : LightShadows.Soft;
                foreach (var v in volumes) if (v != null) v.enabled = c.off != "post";
                var p = c.pos;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y + c.height;
                drone.transform.SetPositionAndRotation(p, Quaternion.Euler(c.euler));
                if (trees != null) trees.SetActive(c.trees && c.off != "trees");
                yield return new WaitForSecondsRealtime(1.5f);

                double frame = 0, main = 0, render = 0, gpu = 0, dt = 0;
                int n = 0;
                for (int i = 0; i < Frames; i++)
                {
                    yield return null;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) == 0) continue;
                    frame += timings[0].cpuFrameTime; main += timings[0].cpuMainThreadFrameTime;
                    render += timings[0].cpuRenderThreadFrameTime; gpu += timings[0].gpuFrameTime;
                    dt += Time.unscaledDeltaTime;
                    n++;
                }
                if (n == 0) { sb.AppendLine($"{c.name} | no frame timings"); continue; }
                sb.AppendLine($"{c.name} | {frame / n:F2} | {main / n:F2} | {render / n:F2} | {gpu / n:F2} | {n / dt:F0}");
            }

            if (urp != null) { urp.shadowCascadeCount = cascades0; urp.shadowDistance = distance0; urp.mainLightShadowmapResolution = res0; urp.shadowSmallMeshScreenPercentages = small0; }
            string path = Arg("-benchmarkOut") ?? System.IO.Path.Combine(Application.persistentDataPath, "benchmark.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log("[Benchmark]\n" + sb);
            Application.Quit();
        }

        /// <summary>
        /// -benchmarkFx: the cost of a big strike. The drone watches tank No. 1 from 30 m (FPV) and from 350 m with
        /// a narrow lens (recon zoom); the tank's ammunition is set off and the frame timings are averaged over the
        /// fireball (0-3 s), the cook-off (3-12 s) and the long burn (20-35 s), against the same view with nothing burning.
        /// </summary>
        IEnumerator Effects(DroneController drone, Terrain terrain)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  effects");
            sb.AppendLine("case | frame ms | CPU main ms | CPU render ms | GPU ms | fps | particles | GPU fx ms");
            var cam = drone.GetComponentInChildren<DroneCamera>().GetComponent<Camera>();
            foreach (var (view, range, fov) in new[] { ("fpv 30 m", 30f, 0f), ("recon 350 m", 350f, 4f) })
            {
                ModernDrone.Damage.VehicleDamageModel tank = null;
                foreach (var v in FindObjectsByType<ModernDrone.Damage.VehicleDamageModel>(FindObjectsSortMode.InstanceID))
                    if (tank == null || v.name.Contains("1")) tank = v;
                if (view != "fpv 30 m")
                {
                    // A fresh tank for the second view: the first is a wreck by now.
                    foreach (var v in FindObjectsByType<ModernDrone.Damage.VehicleDamageModel>(FindObjectsSortMode.InstanceID))
                        if (v.name.Contains("2")) tank = v;
                }
                Vector3 centre = tank.transform.position + Vector3.up * 1.4f;
                Vector3 p = centre - tank.transform.right * range + Vector3.up * range * 0.35f;
                drone.transform.SetPositionAndRotation(p, Quaternion.LookRotation(centre + Vector3.up * 3f - p) * Quaternion.Euler(drone.GetComponentInChildren<DroneCamera>().TiltDeg, 0f, 0f));
                if (fov > 0f) { cam.GetComponent<DroneCamera>().enabled = false; cam.transform.rotation = Quaternion.LookRotation(centre + Vector3.up * 3f - p); cam.fieldOfView = fov; }
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Measure(sb, view + ", idle", 4f);
                UnityEngine.Random.InitState(1);
                // The shells in the rack go: the effects follow from what the tank carries (substances and amounts).
                ModernDrone.Damage.VehicleModule rack = null;
                foreach (var m in tank.Modules) if (m.HighExplosive) { rack = m; break; }
                tank.Detonate(rack);
                yield return Measure(sb, view + ", fireball 0-3 s", 3f);
                yield return Measure(sb, view + ", cook-off 3-12 s", 9f);
                yield return new WaitForSeconds(8f);
                yield return Measure(sb, view + ", burning 20-35 s", 15f);
            }
            string path = Arg("-benchmarkOut") ?? System.IO.Path.Combine(Application.persistentDataPath, "benchmark.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log("[Benchmark]\n" + sb);
            Application.Quit();
        }

        /// <summary>
        /// Averages frame timings over <paramref name="seconds"/>. The effects' renderers and lights (everything under
        /// a "VFX_" root) are hidden every other 30 frames, so "GPU fx" is the effects' own GPU cost measured A/B in the
        /// same view (their smoke hides scenery, so comparing with an idle view would undercount them).
        /// </summary>
        /// <summary>-benchmarkVeg: the swaying tree shader against plain URP Lit on the same poses.</summary>
        IEnumerator Vegetation(DroneController drone, Terrain terrain)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  vegetation");
            sb.AppendLine("case | frame ms | CPU main ms | CPU render ms | GPU ms | fps | particles | GPU fx ms");
            var mats = new System.Collections.Generic.List<Material>();
            foreach (var r in FindObjectsByType<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader.name == "ModernDrone/Vegetation Lit" && !mats.Contains(m)) mats.Add(m);
            var sway = Shader.Find("ModernDrone/Vegetation Lit");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var c in new[] { Cases[0], Cases[2] })
            {
                var p = c.pos;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y + c.height;
                drone.transform.SetPositionAndRotation(p, Quaternion.Euler(c.euler));
                foreach (var shader in new[] { sway, lit, sway })
                {
                    foreach (var m in mats) m.shader = shader;
                    yield return new WaitForSecondsRealtime(1f);
                    yield return Measure(sb, $"{c.name}, {(shader == sway ? "sway" : "URP Lit")}", 5f);
                }
            }
            string path = Arg("-benchmarkOut") ?? System.IO.Path.Combine(Application.persistentDataPath, "benchmark.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Application.Quit();
        }

        static IEnumerator Measure(StringBuilder sb, string name, float seconds)
        {
            var timings = new FrameTiming[1];
            double frame = 0, main = 0, render = 0, dt = 0;
            double[] gpu = new double[2];
            int[] count = new int[2];
            int n = 0, particles = 0, i = 0;
            bool hidden = false;
            for (float t = 0f; t < seconds; t += Time.deltaTime, i++)
            {
                if (i % 30 == 0)
                {
                    hidden = (i / 30) % 2 == 1;
                    ShowEffects(!hidden, ref particles);
                }
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) == 0) continue;
                frame += timings[0].cpuFrameTime; main += timings[0].cpuMainThreadFrameTime;
                render += timings[0].cpuRenderThreadFrameTime;
                dt += Time.unscaledDeltaTime;
                n++;
                // GPU timings arrive a few frames late: skip the frames right after a switch.
                if (i % 30 < 6) continue;
                gpu[hidden ? 1 : 0] += timings[0].gpuFrameTime;
                count[hidden ? 1 : 0]++;
            }
            ShowEffects(true, ref particles);
            double on = count[0] > 0 ? gpu[0] / count[0] : 0, off = count[1] > 0 ? gpu[1] / count[1] : on;
            sb.AppendLine(n == 0 ? $"{name} | no frame timings"
                : $"{name} | {frame / n:F2} | {main / n:F2} | {render / n:F2} | {on:F2} | {n / dt:F0} | {particles} | GPU fx {on - off:F2}");
        }

        static void ShowEffects(bool show, ref int particles)
        {
            int total = 0;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("VFX_")) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !show;
                    foreach (var l in t.GetComponentsInChildren<Light>(true)) l.enabled = show;
                    foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true)) total += ps.particleCount;
                }
            particles = Mathf.Max(particles, total);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
