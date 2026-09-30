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

            // Photoscanned grass / dry grass / mud / forest floor / rock, ~1 m per splat texel.
            data.alphamapResolution = 2048;
            AssetDatabase.CreateAsset(data, terrainDataPath);
            data.terrainLayers = WorldBuilder.Layers();
            WorldBuilder.Paint(data, 7);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.layer = GameLayers.Terrain;
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            if (GraphicsSettings.currentRenderPipeline != null)
                terrain.materialTemplate = GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
            WorldBuilder.Vegetate(terrain, 11);
            AssetDatabase.SaveAssets();
            return terrain;
        }

        internal static void CreateLighting()
        {
            WorldBuilder.ConfigurePipeline();
            WorldBuilder.CreateLighting();
        }

        /// <summary>Windbreak tree lines along field edges: the main cover in steppe terrain.</summary>
        internal static void CreateTreeLines(Terrain terrain)
        {
            var root = new GameObject("TreeLines");
            var mat = GetMaterial("TreeLine", new Color(0.18f, 0.26f, 0.12f));
            var rng = new System.Random(42);
            foreach (float z in WorldBuilder.TreeRows)
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
                    // ponytail: box canopy until tree models arrive (Quaternius CC0 pack); bushes grow underneath.
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
            // No bushes or grass growing through the vehicle.
            var bounds = new Bounds(pos, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            WorldBuilder.ClearAround(terrain, pos, Mathf.Max(bounds.extents.x, bounds.extents.z) + 2f);
            return go;
        }

        /// <summary>One of every catalog vehicle in a row facing the launch site, for free-flight practice.</summary>
        static void CreateVehiclePark(Terrain terrain)
        {
            string[] park =
            {
                VehicleCatalog.Ifv, VehicleCatalog.IfvEra, VehicleCatalog.Mtlb, VehicleCatalog.Mrap, VehicleCatalog.Apc, VehicleCatalog.Spg, VehicleCatalog.TankCage, VehicleCatalog.DecoyTank,
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
        /// <summary>Dirt road painted into the terrain along the given points.</summary>
        internal static void CreateRoad(Terrain terrain, Vector3[] points) => WorldBuilder.PaintRoad(terrain, points, 5f);

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
            mat.enableInstancing = true; // many identical parts (trees, plates) batch on the GPU
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Killcam camera plus mission controller. Zero sorties = free flight.</summary>
        /// <summary>Warheads offered in the loadout.</summary>
        internal static ModernDrone.Damage.WarheadData[] Warheads() => new[]
            {
                WarheadAssets.Load(WarheadAssets.ShapedChargePath),
                WarheadAssets.Load(WarheadAssets.TandemPath),
                WarheadAssets.Load(WarheadAssets.FragmentationPath)
            };

        /// <summary>Accent outline for the hangar (inverted hull).</summary>
        internal static Material OutlineMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/HangarOutline.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("ModernDrone/Outline"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        internal static MissionController CreateMissionRig(string name, string briefing, int sorties, int fiberSorties = 0)
        {
            var camGo = new GameObject("Killcam");
            var cam = camGo.AddComponent<Camera>();
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 55f;
            cam.enabled = false; // enabled by the killcam only while replaying
            camGo.AddComponent<AudioListener>().enabled = false;
            camGo.AddComponent<ModernDrone.Video.AutoExposure>().Configure(1.2f, 1f); // gimbal zoom camera: slow, no overshoot
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).renderPostProcessing = true;
            var killcam = camGo.AddComponent<Killcam>();
            killcam.SetAssets(MissionAssets.LoadKillcamAssets());

            var mission = new GameObject("Mission").AddComponent<MissionController>();
            mission.Configure(name, briefing, sorties, fiberSorties);
            mission.SetWarheads(Warheads());
            mission.SetDroneModels(DroneModels.LoadAll());
            mission.SetParts(DroneParts.LoadAll());
            mission.gameObject.AddComponent<HangarPreview>().SetOutline(OutlineMaterial());
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
