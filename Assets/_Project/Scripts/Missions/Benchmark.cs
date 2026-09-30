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
    /// (Modern Drone/Build/Benchmark Player sets it).
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        const int Frames = 300;

        struct Case { public string name; public Vector3 pos; public Vector3 euler; public float height; public bool trees; }

        // Poses over Mission_01: tree belt at z = 1300 with the targets in front of it.
        static readonly Case[] Cases =
        {
            new Case { name = "fpv over belts", pos = new Vector3(930f, 0f, 1130f), height = 22f, euler = new Vector3(12f, 10f, 0f), trees = true },
            new Case { name = "fpv over belts, trees hidden", pos = new Vector3(930f, 0f, 1130f), height = 22f, euler = new Vector3(12f, 10f, 0f), trees = false },
            new Case { name = "low along belt", pos = new Vector3(700f, 0f, 1275f), height = 4f, euler = new Vector3(3f, 90f, 0f), trees = true },
            new Case { name = "high overview", pos = new Vector3(960f, 0f, 950f), height = 120f, euler = new Vector3(25f, 0f, 0f), trees = true },
        };

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

            var sb = new StringBuilder();
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  {SystemInfo.graphicsDeviceName}  {SystemInfo.processorType}  {Screen.width}x{Screen.height}  {Application.unityVersion}");
            sb.AppendLine("case | frame ms | CPU main ms | CPU render ms | GPU ms | fps");
            var timings = new FrameTiming[1];
            foreach (var c in Cases)
            {
                var p = c.pos;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y + c.height;
                drone.transform.SetPositionAndRotation(p, Quaternion.Euler(c.euler));
                if (trees != null) trees.SetActive(c.trees);
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

            string path = Arg("-benchmarkOut") ?? System.IO.Path.Combine(Application.persistentDataPath, "benchmark.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log("[Benchmark]\n" + sb);
            Application.Quit();
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
