using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Generates the ProvingGround test scene: 2x2 km terrain with gentle hills,
    /// tree lines, flight gates and a placeholder target vehicle.
    /// The scene is fully procedural and can be regenerated from the menu at any time.
    /// </summary>
    public static class ProvingGroundBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/ProvingGround.unity";
        const string TerrainDataPath = "Assets/_Project/Scenes/ProvingGround_Terrain.asset";
        const string MatDir = "Assets/_Project/Art/Materials";

        const float Size = 2000f;
        const float MaxHeight = 60f;
        const int Resolution = 513;

        internal static Vector3 Center => new Vector3(Size * 0.5f, 0f, Size * 0.5f);

        [MenuItem("Modern Drone/Setup/Build Proving Ground Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var terrain = CreateTerrain(TerrainDataPath);
            CreateLighting();
            CreateTreeLines(terrain);
            CreateFlightGates(terrain);
            // Target row: front, side and rear aspects toward the launch site.
            PlaceTank(terrain, Center + new Vector3(0f, 0f, 150f), 180f, "Tank_Front");
            PlaceTank(terrain, Center + new Vector3(-45f, 0f, 170f), 90f, "Tank_Side");
            PlaceTank(terrain, Center + new Vector3(45f, 0f, 170f), 0f, "Tank_Rear");
            CreateVehiclePark(terrain);
            CreateSpawn(terrain);
            CreateMissionRig("Полигон", "", 0);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath, 1);
            Debug.Log($"[ModernDrone] Proving ground built: {ScenePath}");
        }

        internal static Terrain CreateTerrain(string terrainDataPath)
        {
            AssetDatabase.DeleteAsset(terrainDataPath);
            var data = new TerrainData { heightmapResolution = Resolution };
            data.size = new Vector3(Size, MaxHeight, Size);

            var heights = new float[Resolution, Resolution];
            var rng = new System.Random(1337);
            float ox = rng.Next(0, 10000), oz = rng.Next(0, 10000);
            var mid = new Vector2(Resolution * 0.5f, Resolution * 0.5f);

            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                float u = x / (float)Resolution, v = z / (float)Resolution;
                float h = Mathf.PerlinNoise(ox + u * 3f, oz + v * 3f) * 0.6f
                        + Mathf.PerlinNoise(ox + u * 12f, oz + v * 12f) * 0.12f;
                // Flatten a field around the centre for the launch site and target area.
                float distMeters = Vector2.Distance(new Vector2(x, z), mid) / Resolution * Size;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 400f, distMeters));
                heights[z, x] = Mathf.Lerp(0.25f, h, blend);
            }
            data.SetHeights(0, 0, heights);

            // Procedural grass / dry grass / dirt layers mixed by noise patches and slope.
            data.alphamapResolution = 512;
            AssetDatabase.CreateAsset(data, terrainDataPath);
            data.terrainLayers = TerrainTextures.LoadOrCreateLayers();
            TerrainTextures.Paint(data, 7);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.layer = GameLayers.Terrain;
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            if (GraphicsSettings.currentRenderPipeline != null)
                terrain.materialTemplate = GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
            return terrain;
        }

        internal static void CreateLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            RenderSettings.sun = sun;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.66f, 0.70f, 0.74f);
            RenderSettings.fogDensity = 0.0009f; // haze: ~50% visibility loss at 1 km
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.52f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.20f);
        }

        /// <summary>Windbreak tree lines along field edges: the main cover in steppe terrain.</summary>
        internal static void CreateTreeLines(Terrain terrain)
        {
            var root = new GameObject("TreeLines");
            var mat = GetMaterial("TreeLine", new Color(0.18f, 0.26f, 0.12f));
            var rng = new System.Random(42);
            float[] rows = { 350f, 700f, 1300f, 1650f };
            foreach (float z in rows)
            {
                for (float x = 100f; x < Size - 100f; x += 6f)
                {
                    if (rng.NextDouble() < 0.08) continue; // gaps in the tree line
                    float h = 8f + (float)rng.NextDouble() * 7f;
                    float w = 4f + (float)rng.NextDouble() * 3f;
                    var p = new Vector3(x, 0f, z + (float)(rng.NextDouble() - 0.5) * 8f);
                    p.y = terrain.SampleHeight(p) + h * 0.5f;
                    var tree = Box("Tree", root.transform, mat, GameLayers.Environment);
                    tree.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                    tree.transform.localScale = new Vector3(w, h, w);
                    tree.isStatic = true;
                }
            }
        }

        static void CreateFlightGates(Terrain terrain)
        {
            var root = new GameObject("FlightGates");
            var mat = GetMaterial("Gate", new Color(1f, 0.45f, 0.05f));
            const int count = 8;
            const float radius = 90f;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                var pos = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                pos.y = terrain.SampleHeight(pos);
                var gate = new GameObject($"Gate_{i:00}");
                gate.transform.SetParent(root.transform);
                gate.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))));

                float lift = i % 2 == 0 ? 0f : 4f; // alternate ground-level and elevated gates
                float top = lift + 2.5f;
                Bar(gate.transform, mat, new Vector3(-1.6f, top * 0.5f, 0f), new Vector3(0.2f, top, 0.2f));
                Bar(gate.transform, mat, new Vector3(1.6f, top * 0.5f, 0f), new Vector3(0.2f, top, 0.2f));
                Bar(gate.transform, mat, new Vector3(0f, top, 0f), new Vector3(3.4f, 0.2f, 0.2f));
                if (lift > 0f)
                    Bar(gate.transform, mat, new Vector3(0f, lift, 0f), new Vector3(3.4f, 0.2f, 0.2f));
            }
        }

        static void Bar(Transform parent, Material mat, Vector3 localPos, Vector3 scale)
        {
            var bar = Box("Bar", parent, mat, GameLayers.Environment);
            bar.transform.localPosition = localPos;
            bar.transform.localScale = scale;
            bar.isStatic = true;
        }

        internal static GameObject PlacePrefab(Terrain terrain, string prefabPath, Vector3 pos, float yaw, string name = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (name != null) go.name = name;
            pos.y = terrain.SampleHeight(pos);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        /// <summary>One of every catalog vehicle in a row facing the launch site, for free-flight practice.</summary>
        static void CreateVehiclePark(Terrain terrain)
        {
            string[] park =
            {
                VehicleCatalog.Ifv, VehicleCatalog.Apc, VehicleCatalog.Spg, VehicleCatalog.TankCage, VehicleCatalog.DecoyTank,
                VehicleCatalog.Howitzer, VehicleCatalog.Mlrs, VehicleCatalog.Truck, VehicleCatalog.SamLauncher,
                VehicleCatalog.SamRadar, VehicleCatalog.Spaag, VehicleCatalog.InterceptorSite
            };
            for (int i = 0; i < park.Length; i++)
            {
                var pos = Center + new Vector3(-160f + i * 32f, 0f, 260f);
                PlacePrefab(terrain, park[i], pos, 180f);
            }
        }

        /// <summary>Dirt road strip following the terrain along the given points (visual only, no collider).</summary>
        internal static void CreateRoad(Terrain terrain, Vector3[] points)
        {
            var root = new GameObject("Road");
            var mat = GetMaterial("Dirt", new Color(0.36f, 0.31f, 0.22f));
            const float step = 8f;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = points[i - 1], b = points[i];
                Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                int n = Mathf.CeilToInt(flat.magnitude / step);
                for (int k = 0; k < n; k++)
                {
                    Vector3 p0 = Vector3.Lerp(a, b, k / (float)n), p1 = Vector3.Lerp(a, b, (k + 1) / (float)n);
                    Vector3 mid = (p0 + p1) * 0.5f;
                    mid.y = terrain.SampleHeight(mid) + 0.03f;
                    float h0 = terrain.SampleHeight(p0), h1 = terrain.SampleHeight(p1);
                    Vector3 dir = new Vector3(p1.x - p0.x, h1 - h0, p1.z - p0.z);
                    var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(seg.GetComponent<Collider>());
                    seg.name = "RoadSegment";
                    seg.transform.SetParent(root.transform);
                    seg.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(dir));
                    seg.transform.localScale = new Vector3(5f, 0.05f, dir.magnitude + 0.5f);
                    seg.GetComponent<Renderer>().sharedMaterial = mat;
                    seg.isStatic = true;
                }
            }
        }

        internal static GameObject PlaceTank(Terrain terrain, Vector3 pos, float yaw, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.TankPrefabPath) ?? VehicleBuilder.BuildTank();
            var tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            tank.name = name;
            pos.y = terrain.SampleHeight(pos);
            tank.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return tank;
        }

        internal static void CreateSpawn(Terrain terrain)
        {
            var pos = Center + new Vector3(0f, 0f, -40f);
            pos.y = terrain.SampleHeight(pos) + 0.1f;
            new GameObject("DroneSpawn") { tag = "Respawn" }.transform.position = pos;

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "LaunchPad";
            pad.layer = GameLayers.Environment;
            pad.transform.position = pos - Vector3.up * 0.05f;
            pad.transform.localScale = new Vector3(1.5f, 0.05f, 1.5f);
            pad.GetComponent<Renderer>().sharedMaterial = GetMaterial("Pad", new Color(0.35f, 0.35f, 0.35f));
            // A flattened capsule collider degenerates into a sphere; use a box instead.
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            pad.AddComponent<BoxCollider>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DroneRigBuilder.PrefabPath) ?? DroneRigBuilder.BuildPrefab();
            var drone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // Rest on the pad: collider half-height above the surface.
            drone.transform.SetPositionAndRotation(pos + Vector3.up * 0.1f, Quaternion.identity);
        }

        static GameObject Box(string name, Transform parent, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Killcam camera plus mission controller. Zero sorties = free flight.</summary>
        internal static MissionController CreateMissionRig(string name, string briefing, int sorties, int fiberSorties = 0)
        {
            var camGo = new GameObject("Killcam");
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 55f;
            cam.enabled = false; // enabled by the killcam only while replaying
            camGo.AddComponent<AudioListener>().enabled = false;
            var killcam = camGo.AddComponent<Killcam>();
            killcam.SetAssets(MissionAssets.LoadKillcamAssets());

            var mission = new GameObject("Mission").AddComponent<MissionController>();
            mission.Configure(name, briefing, sorties, fiberSorties);
            mission.SetWarheads(new[]
            {
                WarheadAssets.Load(WarheadAssets.ShapedChargePath),
                WarheadAssets.Load(WarheadAssets.TandemPath),
                WarheadAssets.Load(WarheadAssets.FragmentationPath)
            });
            mission.SetDroneClasses(DroneClasses.LoadAll());
            mission.gameObject.AddComponent<ModernDrone.UI.PauseMenu>();
            return mission;
        }

        /// <summary>Adds the scene at the given build index (0 = the scene the game starts in).</summary>
        internal static void AddToBuildSettings(string path, int index)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path || s.path == "Assets/Scenes/SampleScene.unity");
            scenes.Insert(Mathf.Min(index, scenes.Count), new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
