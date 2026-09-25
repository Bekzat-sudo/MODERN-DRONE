using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Vehicles;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Small toolkit for building placeholder vehicles from primitives: armor plates, reactive blocks,
    /// cages, damage modules, wheels and plain visuals. Armor values are abstract gameplay points.
    /// </summary>
    internal sealed class VehicleKit
    {
        const string MatDir = "Assets/_Project/Art/Materials";

        public readonly GameObject Root;
        public readonly Material Hull, Dark, Metal, ModuleMat, EraMat;

        public Transform T => Root.transform;

        public VehicleKit(string name, Color hull, string hullMaterialName = "VehicleGreen", float smoothness = 0.15f)
        {
            Root = new GameObject(name) { tag = "Vehicle" };
            Hull = Material(hullMaterialName, hull, smoothness);
            Dark = Material("VehicleTrack", new Color(0.12f, 0.12f, 0.11f));
            Metal = Material("EwAntenna", new Color(0.75f, 0.75f, 0.7f));
            ModuleMat = Material("VehicleModule", new Color(0.5f, 0.5f, 0.5f));
            EraMat = Material("VehicleEra", new Color(0.33f, 0.36f, 0.25f));
        }

        /// <summary>Wraps an existing vehicle object (e.g. an unpacked prefab) to add parts to it.</summary>
        public VehicleKit(GameObject existingRoot)
        {
            Root = existingRoot;
            Hull = Material("VehicleGreen", new Color(0.29f, 0.33f, 0.22f));
            Dark = Material("VehicleTrack", new Color(0.12f, 0.12f, 0.11f));
            Metal = Material("EwAntenna", new Color(0.75f, 0.75f, 0.7f));
            ModuleMat = Material("VehicleModule", new Color(0.5f, 0.5f, 0.5f));
            EraMat = Material("VehicleEra", new Color(0.33f, 0.36f, 0.25f));
        }

        public Transform Group(string name, Vector3 localPos, Transform parent = null, Quaternion? rot = null)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent != null ? parent : T, false);
            g.localPosition = localPos;
            g.localRotation = rot ?? Quaternion.identity;
            return g;
        }

        /// <summary>Visible, physical part with no armor meaning (hull layer: the drone collides, the jet ignores it).</summary>
        public GameObject Visual(string name, Vector3 pos, Vector3 size, Material mat = null, Transform parent = null,
            Quaternion? rot = null, PrimitiveType type = PrimitiveType.Cube, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = GameLayers.VehicleHull;
            go.transform.SetParent(parent != null ? parent : T, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat != null ? mat : Hull;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            else if (type == PrimitiveType.Cylinder)
            {
                // Flattened capsule colliders degenerate; use a box instead.
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.AddComponent<BoxCollider>();
            }
            return go;
        }

        public GameObject Plate(string name, Vector3 pos, Vector3 size, float armor, Transform parent = null, Quaternion? rot = null, Material mat = null)
        {
            var go = Visual(name, pos, size, mat, parent, rot);
            go.AddComponent<ArmorElement>().Configure(ArmorKind.Plate, armor);
            return go;
        }

        public GameObject Era(string name, Vector3 pos, Vector3 size, Transform parent = null, Quaternion? rot = null)
        {
            var go = Visual(name, pos, size, EraMat, parent, rot);
            go.AddComponent<ArmorElement>().Configure(ArmorKind.Reactive, 0.5f);
            return go;
        }

        /// <summary>
        /// Anti-drone cage ("mangal"): a lattice of thin bars with one solid collider on the Screen layer, so the drone
        /// detonates on it and the jet may be disrupted.
        /// </summary>
        public GameObject Cage(string name, Vector3 center, Vector3 size, float disruptChance, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : T, false);
            go.transform.localPosition = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            go.AddComponent<ArmorElement>().Configure(ArmorKind.Screen, disruptChance);

            const float bar = 0.04f;
            int nx = Mathf.Max(2, Mathf.RoundToInt(size.x / 0.35f));
            int nz = Mathf.Max(2, Mathf.RoundToInt(size.z / 0.35f));
            for (int i = 0; i <= nx; i++)
            {
                float x = -size.x * 0.5f + size.x * i / nx;
                Visual("Bar", new Vector3(x, size.y * 0.5f, 0f), new Vector3(bar, bar, size.z), Metal, go.transform, collider: false);
            }
            for (int i = 0; i <= nz; i++)
            {
                float z = -size.z * 0.5f + size.z * i / nz;
                Visual("Bar", new Vector3(0f, size.y * 0.5f, z), new Vector3(size.x, bar, bar), Metal, go.transform, collider: false);
            }
            foreach (float x in new[] { -size.x * 0.5f, size.x * 0.5f })
            foreach (float z in new[] { -size.z * 0.5f, size.z * 0.5f })
                Visual("Post", new Vector3(x, 0f, z), new Vector3(bar, size.y, bar), Metal, go.transform, collider: false);
            go.layer = GameLayers.Screen;
            return go;
        }

        public GameObject Module(string name, ModuleType type, float hp, float absorb, Vector3 pos, Vector3 size,
            Transform parent = null, bool visible = false, Material mat = null)
        {
            GameObject go;
            if (visible)
            {
                go = Visual(name, pos, size, mat != null ? mat : ModuleMat, parent);
            }
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(parent != null ? parent : T, false);
                go.transform.localPosition = pos;
                go.transform.localScale = size;
                go.AddComponent<BoxCollider>();
            }
            go.layer = GameLayers.Module;
            go.AddComponent<VehicleModule>().Configure(type, hp, absorb);
            return go;
        }

        /// <summary>Wheels along both sides; each side is one "Wheels" damage module.</summary>
        public void Wheels(float[] zs, float halfTrack, float radius, float width, float hp = 90f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (float z in zs)
                {
                    Visual("Wheel", new Vector3(side * halfTrack, radius, z), new Vector3(radius * 2f, width * 0.5f, radius * 2f), Dark,
                        rot: Quaternion.Euler(0f, 0f, 90f), type: PrimitiveType.Cylinder);
                    minZ = Mathf.Min(minZ, z);
                    maxZ = Mathf.Max(maxZ, z);
                }
                Module(side < 0 ? "Module_Wheels_L" : "Module_Wheels_R", ModuleType.Wheels, hp, 20f,
                    new Vector3(side * halfTrack, radius, (minZ + maxZ) * 0.5f), new Vector3(width, radius * 1.8f, maxZ - minZ + radius * 2f));
            }
        }

        /// <summary>Two track runs with a damage module each.</summary>
        public void Tracks(float halfTrack, float width, float height, float length, float z = 0f, float hp = 110f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                var track = Visual(side < 0 ? "Track_L" : "Track_R", new Vector3(side * halfTrack, height * 0.5f, z), new Vector3(width, height, length), Dark);
                Module(side < 0 ? "Module_Track_L" : "Module_Track_R", ModuleType.Track, hp, 30f, Vector3.zero, Vector3.one * 0.95f, track.transform);
            }
        }

        public VehicleDamageModel Finish(string displayName, bool decoy = false)
        {
            var model = Root.AddComponent<VehicleDamageModel>();
            model.SetDisplayName(displayName);
            model.SetDecoy(decoy);
            Root.AddComponent<VehicleEffects>();
            if (decoy) Root.tag = "Decoy";
            return model;
        }

        public GameObject Save(string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(Root, path);
            Object.DestroyImmediate(Root);
            return prefab;
        }

        public static Material Material(string name, Color color, float smoothness = 0.15f)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.enableInstancing = true; // many identical parts (trees, plates) batch on the GPU
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
