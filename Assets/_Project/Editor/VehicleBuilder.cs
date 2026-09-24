using ModernDrone.Core;
using ModernDrone.Damage;
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
                var track = Box($"Track_{n}", t, _trackMat, GameLayers.VehicleHull, new Vector3(side * 1.85f, 0.43f, 0.3f), new Vector3(0.55f, 0.85f, 6.9f));
                Module($"Module_Track_{n}", track.transform, ModuleType.Track, 120f, 30f, Vector3.zero, Vector3.one * 0.95f);
            }

            // Internal hull modules
            Module("Module_ControlStation", t, ModuleType.ControlStation, 100f, 30f, new Vector3(0f, 0.9f, 2.3f), new Vector3(0.9f, 0.6f, 0.9f));
            Module("Module_Fuel_FR", t, ModuleType.FuelTank, 80f, 20f, new Vector3(1.0f, 0.9f, 2.3f), new Vector3(0.6f, 0.75f, 1.0f));
            Module("Module_Fuel_RL", t, ModuleType.FuelTank, 80f, 20f, new Vector3(-1.1f, 0.9f, -0.6f), new Vector3(0.5f, 0.7f, 0.9f));
            Module("Module_AmmoRack", t, ModuleType.AmmoRack, 90f, 20f, new Vector3(0f, 0.65f, 0.5f), new Vector3(2.0f, 0.3f, 2.0f));
            Module("Module_Engine", t, ModuleType.Engine, 200f, 60f, new Vector3(0f, 0.95f, -1.5f), new Vector3(2.4f, 0.8f, 1.6f));
            Module("Module_Transmission", t, ModuleType.Transmission, 150f, 50f, new Vector3(0f, 0.8f, -2.6f), new Vector3(2.6f, 0.6f, 0.7f));

            // Turret
            var turret = new GameObject("Turret").transform;
            turret.SetParent(t, false);
            turret.localPosition = new Vector3(0f, 1.8f, 0.5f);
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

            var barrel = Box("Gun", turret, _hullMat, GameLayers.VehicleHull, new Vector3(0f, 0.05f, 3.4f), new Vector3(0.18f, 0.18f, 4.6f));
            Module("Module_Gun", barrel.transform, ModuleType.Gun, 120f, 30f, Vector3.zero, Vector3.one * 1.05f);
            Module("Module_Breech", turret, ModuleType.Breech, 120f, 40f, new Vector3(0f, 0.05f, 0.2f), new Vector3(0.5f, 0.45f, 1.1f));
            Module("Module_TurretDrive", turret, ModuleType.TurretDrive, 70f, 20f, new Vector3(0.75f, -0.1f, 0.2f), new Vector3(0.4f, 0.4f, 0.4f));
            Module("Module_Optics", turret, ModuleType.Optics, 40f, 5f, new Vector3(-0.6f, 0.5f, 0.7f), new Vector3(0.3f, 0.25f, 0.3f), visible: true);

            var model = root.AddComponent<VehicleDamageModel>();
            model.SetDisplayName("Танк (заглушка)");
            var fx = root.AddComponent<VehicleEffects>();
            var so = new SerializedObject(fx);
            so.FindProperty("turret").objectReferenceValue = turret;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, TankPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ModernDrone] Vehicle prefab built: {TankPrefabPath}");
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
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
