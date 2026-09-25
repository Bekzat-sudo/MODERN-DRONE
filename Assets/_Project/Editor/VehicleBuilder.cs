using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Missions;
using ModernDrone.Video;
using ModernDrone.Vehicles;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Builds the placeholder MBT prefab: an armor shell of plates (abstract armor points), reactive blocks on
    /// the front, and internal modules. Layout is loosely MBT-like so that "where to hit" matters:
    /// the sloped front is effectively immune, sides / rear / engine deck / turret roof are weak spots.
    /// </summary>
    public static class VehicleBuilder
    {
        public const string TankPrefabPath = "Assets/_Project/Prefabs/Vehicles/Tank_Placeholder.prefab";
        const string TankModelPath = "Assets/_Project/Art/Models/Vehicles/T72B3/t72b3.glb";
        public const string EwStationPrefabPath = "Assets/_Project/Prefabs/Vehicles/EwStation_Placeholder.prefab";
        const string MatDir = "Assets/_Project/Art/Materials";

        static Material _hullMat, _trackMat, _eraMat, _moduleMat;

        [MenuItem("Modern Drone/Setup/Build Vehicle Prefabs")]
        public static GameObject BuildTank()
        {
            _hullMat = GetMaterial("VehicleGreen", new Color(0.29f, 0.33f, 0.22f));
            _trackMat = GetMaterial("VehicleTrack", new Color(0.12f, 0.12f, 0.11f));
            _eraMat = GetMaterial("VehicleEra", new Color(0.33f, 0.36f, 0.25f));
            _moduleMat = GetMaterial("VehicleModule", new Color(0.5f, 0.5f, 0.5f));

            var root = new GameObject("Tank_Placeholder") { tag = "Vehicle" };
            var t = root.transform;

            // Hull shell
            Plate("UpperFront", t, new Vector3(0f, 1.2f, 3.6f), new Vector3(3.0f, 1.3f, 0.1f), 450f, Quaternion.Euler(-68f, 0f, 0f));
            Plate("LowerFront", t, new Vector3(0f, 0.7f, 3.9f), new Vector3(3.0f, 0.8f, 0.1f), 250f, Quaternion.Euler(50f, 0f, 0f));
            Plate("Side_L", t, new Vector3(-1.54f, 0.95f, 0.3f), new Vector3(0.08f, 1.0f, 6.6f), 80f);
            Plate("Side_R", t, new Vector3(1.54f, 0.95f, 0.3f), new Vector3(0.08f, 1.0f, 6.6f), 80f);
            Plate("Rear", t, new Vector3(0f, 0.95f, -3.0f), new Vector3(3.0f, 1.0f, 0.08f), 45f);
            Plate("RoofFront", t, new Vector3(0f, 1.45f, 2.1f), new Vector3(3.0f, 0.06f, 1.8f), 40f);
            Plate("RoofRing_L", t, new Vector3(-1.3f, 1.45f, 0.4f), new Vector3(0.4f, 0.06f, 1.6f), 40f);
            Plate("RoofRing_R", t, new Vector3(1.3f, 1.45f, 0.4f), new Vector3(0.4f, 0.06f, 1.6f), 40f);
            Plate("EngineDeck", t, new Vector3(0f, 1.45f, -1.7f), new Vector3(3.0f, 0.05f, 2.6f), 20f);
            Plate("Floor", t, new Vector3(0f, 0.45f, 0.3f), new Vector3(3.0f, 0.06f, 6.6f), 25f);

            // Reactive blocks on the upper front plate, laid on its surface.
            var ufpRot = Quaternion.Euler(-68f, 0f, 0f);
            var ufpCenter = new Vector3(0f, 1.2f, 3.6f);
            for (int ix = 0; ix < 6; ix++)
            for (int iy = -1; iy <= 1; iy++)
            {
                var local = new Vector3(-1.2f + ix * 0.48f, iy * 0.4f, 0.1f);
                Era($"Era_UFP_{ix}_{iy + 1}", t, ufpCenter + ufpRot * local, new Vector3(0.44f, 0.36f, 0.08f), ufpRot);
            }

            // Tracks: physical box on the hull layer (the drone can bump into it) + damage module inside.
            foreach (float side in new[] { -1f, 1f })
            {
                string n = side < 0 ? "L" : "R";
                // Overall width over tracks 3.4-3.59 m, hull 6.67-6.86 m, height 2.23 m (en.wikipedia.org/wiki/T-72).
                var track = Box($"Track_{n}", t, _trackMat, GameLayers.VehicleHull, new Vector3(side * 1.45f, 0.43f, 0.3f), new Vector3(0.55f, 0.85f, 6.9f));
                Module($"Module_Track_{n}", track.transform, ModuleType.Track, 120f, 30f, Vector3.zero, Vector3.one * 0.95f);
            }

            // Internal hull modules
            Module("Module_ControlStation", t, ModuleType.ControlStation, 100f, 30f, new Vector3(0f, 0.9f, 2.3f), new Vector3(0.9f, 0.6f, 0.9f));
            Module("Module_Fuel_FR", t, ModuleType.FuelTank, 80f, 20f, new Vector3(1.0f, 0.9f, 2.3f), new Vector3(0.6f, 0.75f, 1.0f));
            Module("Module_Fuel_RL", t, ModuleType.FuelTank, 80f, 20f, new Vector3(-1.1f, 0.9f, -0.6f), new Vector3(0.5f, 0.7f, 0.9f));
            Module("Module_AmmoRack", t, ModuleType.AmmoRack, 90f, 20f, new Vector3(0f, 0.65f, 0.5f), new Vector3(2.0f, 0.3f, 2.0f));
            // Spare rounds outside the carousel (front hull rack): the usual cause of turret-toss cook-offs (en.wikipedia.org/wiki/T-72).
            Module("Module_AmmoSpare", t, ModuleType.AmmoRack, 90f, 20f, new Vector3(-1.0f, 0.9f, 2.3f), new Vector3(0.5f, 0.7f, 0.9f));
            Module("Module_Engine", t, ModuleType.Engine, 200f, 60f, new Vector3(0f, 0.95f, -1.5f), new Vector3(2.4f, 0.8f, 1.6f));
            Module("Module_Transmission", t, ModuleType.Transmission, 150f, 50f, new Vector3(0f, 0.8f, -2.6f), new Vector3(2.6f, 0.6f, 0.7f));

            // Turret
            var turret = new GameObject("Turret").transform;
            turret.SetParent(t, false);
            // Turret ring of the T-72B3 model once it is shifted onto the hull boxes (see AttachModel below).
            turret.localPosition = new Vector3(0f, 1.85f, 0.225f);
            Plate("Turret_Front", turret, new Vector3(0f, 0f, 1.1f), new Vector3(2.2f, 0.7f, 0.3f), 550f);
            Plate("Turret_Side_L", turret, new Vector3(-1.1f, 0f, 0f), new Vector3(0.1f, 0.7f, 2.2f), 130f);
            Plate("Turret_Side_R", turret, new Vector3(1.1f, 0f, 0f), new Vector3(0.1f, 0.7f, 2.2f), 130f);
            Plate("Turret_Rear", turret, new Vector3(0f, 0f, -1.1f), new Vector3(2.2f, 0.7f, 0.1f), 70f);
            Plate("Turret_Roof", turret, new Vector3(0f, 0.35f, 0f), new Vector3(2.2f, 0.06f, 2.3f), 35f);
            for (int ix = 0; ix < 4; ix++)
            for (int iy = 0; iy < 2; iy++)
            {
                float x = new[] { -0.9f, -0.45f, 0.45f, 0.9f }[ix];
                Era($"Era_Turret_{ix}_{iy}", turret, new Vector3(x, -0.17f + iy * 0.34f, 1.32f), new Vector3(0.4f, 0.3f, 0.12f), Quaternion.identity);
            }

            var barrel = Box("Gun", turret, _hullMat, GameLayers.VehicleHull, new Vector3(0f, -0.12f, 3.4f), new Vector3(0.18f, 0.18f, 4.6f));
            Module("Module_Gun", barrel.transform, ModuleType.Gun, 120f, 30f, Vector3.zero, Vector3.one * 1.05f);
            Module("Module_Breech", turret, ModuleType.Breech, 120f, 40f, new Vector3(0f, 0.05f, 0.2f), new Vector3(0.5f, 0.45f, 1.1f));
            Module("Module_TurretDrive", turret, ModuleType.TurretDrive, 70f, 20f, new Vector3(0.75f, -0.1f, 0.2f), new Vector3(0.4f, 0.4f, 0.4f));
            Module("Module_Optics", turret, ModuleType.Optics, 40f, 5f, new Vector3(-0.6f, 0.5f, 0.7f), new Vector3(0.3f, 0.25f, 0.3f), visible: true);

            AttachModel(t, turret, TankModelPath, new Vector3(0f, 0f, 0.4f));

            var model = root.AddComponent<VehicleDamageModel>();
            model.SetDisplayName("Танк Т-72Б3");
            var fx = root.AddComponent<VehicleEffects>();
            var so = new SerializedObject(fx);
            so.FindProperty("turret").objectReferenceValue = turret;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, TankPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ModernDrone] Vehicle prefab built: {TankPrefabPath}");
            return prefab;
        }

        /// <summary>
        /// Puts an imported model over the damage boxes: the boxes keep their colliders (armor, modules, hull) but stop
        /// rendering, the model draws the vehicle. A model node named "Turret" moves with the turret transform.
        /// </summary>
        static void AttachModel(Transform root, Transform turret, string path, Vector3 offset)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            var visual = Object.Instantiate(asset, root);
            visual.name = "Visual";
            visual.transform.localPosition = offset;
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var modelTurret = visual.transform.Find("Turret");
            if (modelTurret != null && turret != null) modelTurret.SetParent(turret, true);
        }

        /// <summary>
        /// Truck-mounted EW station: thin-skinned cab and shelter, generator inside, antenna panels on a mast.
        /// Jams while an antenna and the generator survive.
        /// </summary>
        [MenuItem("Modern Drone/Setup/Build EW Station Prefab")]
        public static GameObject BuildEwStation()
        {
            _hullMat = GetMaterial("VehicleGreen", new Color(0.29f, 0.33f, 0.22f));
            _trackMat = GetMaterial("VehicleTrack", new Color(0.12f, 0.12f, 0.11f));
            _moduleMat = GetMaterial("VehicleModule", new Color(0.5f, 0.5f, 0.5f));
            var antennaMat = GetMaterial("EwAntenna", new Color(0.75f, 0.75f, 0.7f));

            var root = new GameObject("EwStation_Placeholder") { tag = "Vehicle" };
            var t = root.transform;

            // Chassis and wheels: physical hull layer (not armor).
            Box("Chassis", t, _trackMat, GameLayers.VehicleHull, new Vector3(0f, 0.85f, 0f), new Vector3(2.3f, 0.45f, 7.2f));
            foreach (float z in new[] { 2.4f, -1.2f, -2.6f })
            foreach (float x in new[] { -1.05f, 1.05f })
                Box("Wheel", t, _trackMat, GameLayers.VehicleHull, new Vector3(x, 0.55f, z), new Vector3(0.4f, 1.1f, 1.1f));

            // Cab (front) — thin plates.
            Plate("Cab_Front", t, new Vector3(0f, 1.9f, 3.55f), new Vector3(2.4f, 1.8f, 0.06f), 8f);
            Plate("Cab_Side_L", t, new Vector3(-1.2f, 1.9f, 2.7f), new Vector3(0.06f, 1.8f, 1.7f), 8f);
            Plate("Cab_Side_R", t, new Vector3(1.2f, 1.9f, 2.7f), new Vector3(0.06f, 1.8f, 1.7f), 8f);
            Plate("Cab_Roof", t, new Vector3(0f, 2.8f, 2.7f), new Vector3(2.4f, 0.06f, 1.7f), 8f);
            Plate("Cab_Back", t, new Vector3(0f, 1.9f, 1.85f), new Vector3(2.4f, 1.8f, 0.06f), 8f);

            // Shelter (kung) with the electronics.
            Plate("Shelter_Side_L", t, new Vector3(-1.25f, 2.25f, -0.9f), new Vector3(0.06f, 2.3f, 4.8f), 10f);
            Plate("Shelter_Side_R", t, new Vector3(1.25f, 2.25f, -0.9f), new Vector3(0.06f, 2.3f, 4.8f), 10f);
            Plate("Shelter_Front", t, new Vector3(0f, 2.25f, 1.5f), new Vector3(2.5f, 2.3f, 0.06f), 10f);
            Plate("Shelter_Rear", t, new Vector3(0f, 2.25f, -3.3f), new Vector3(2.5f, 2.3f, 0.06f), 10f);
            Plate("Shelter_Roof", t, new Vector3(0f, 3.4f, -0.9f), new Vector3(2.5f, 0.06f, 4.8f), 10f);

            Module("Module_Engine", t, ModuleType.Engine, 120f, 30f, new Vector3(0f, 1.3f, 3.0f), new Vector3(1.4f, 0.6f, 1.0f));
            Module("Module_ControlStation", t, ModuleType.ControlStation, 60f, 10f, new Vector3(0f, 1.9f, 2.6f), new Vector3(1.8f, 0.9f, 1.0f));
            Module("Module_Fuel", t, ModuleType.FuelTank, 60f, 10f, new Vector3(1.0f, 0.85f, 0.8f), new Vector3(0.5f, 0.5f, 1.2f));
            Module("Module_Generator", t, ModuleType.Generator, 80f, 25f, new Vector3(0f, 1.8f, -2.5f), new Vector3(1.4f, 1.1f, 1.2f), visible: true);
            Module("Module_Electronics", t, ModuleType.ControlStation, 60f, 10f, new Vector3(0f, 2.2f, -0.4f), new Vector3(2.0f, 1.4f, 1.6f));

            // Mast with two antenna panels: exposed, lightly built.
            var mast = Box("Mast", t, antennaMat, GameLayers.VehicleHull, new Vector3(0f, 6.2f, -1.4f), new Vector3(0.15f, 5.6f, 0.15f));
            Object.DestroyImmediate(mast.GetComponent<Collider>()); // too thin to matter; keeps fly-through fair
            var antennaA = Box("Antenna_A", t, antennaMat, GameLayers.Module, new Vector3(0f, 9.1f, -1.4f), new Vector3(1.6f, 0.9f, 0.12f));
            antennaA.AddComponent<VehicleModule>().Configure(ModuleType.Antenna, 50f, 5f);
            var antennaB = Box("Antenna_B", t, antennaMat, GameLayers.Module, new Vector3(0f, 8.0f, -1.4f), new Vector3(0.12f, 0.9f, 1.6f));
            antennaB.AddComponent<VehicleModule>().Configure(ModuleType.Antenna, 50f, 5f);

            var model = root.AddComponent<VehicleDamageModel>();
            model.SetDisplayName("Станция РЭБ (заглушка)");
            root.AddComponent<VehicleEffects>();
            var jammer = root.AddComponent<SignalJammer>();
            jammer.Configure(280f, 0.92f, JamBands.Both, antennaA.transform);
            root.AddComponent<JammerStation>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, EwStationPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ModernDrone] EW station prefab built: {EwStationPrefabPath}");
            return prefab;
        }

        static void Plate(string name, Transform parent, Vector3 pos, Vector3 size, float armor, Quaternion? rot = null)
        {
            var go = Box(name, parent, _hullMat, GameLayers.Armor, pos, size, rot);
            go.AddComponent<ArmorElement>().Configure(ArmorKind.Plate, armor);
        }

        static void Era(string name, Transform parent, Vector3 pos, Vector3 size, Quaternion rot)
        {
            var go = Box(name, parent, _eraMat, GameLayers.ERA, pos, size, rot);
            go.AddComponent<ArmorElement>().Configure(ArmorKind.Reactive, 0.5f);
        }

        static void Module(string name, Transform parent, ModuleType type, float hp, float absorb, Vector3 pos, Vector3 size, bool visible = false)
        {
            // When parented to a scaled box (track, barrel) the size is relative to that box.
            GameObject go;
            if (visible)
            {
                go = Box(name, parent, _moduleMat, GameLayers.Module, pos, size);
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.AddComponent<BoxCollider>();
            }
            go.layer = GameLayers.Module;
            go.AddComponent<VehicleModule>().Configure(type, hp, absorb);
        }

        static GameObject Box(string name, Transform parent, Material mat, int layer, Vector3 pos, Vector3 size, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.enableInstancing = true; // many identical parts (trees, plates) batch on the GPU
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
