using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.Missions;
using ModernDrone.UI;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Builds the default FPV drone prefab. The "Visual" child holds the imported model;
    /// physics lives on the root and does not depend on it.
    /// </summary>
    public static class DroneRigBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Drones/FPV_Default.prefab";
        const string MatDir = "Assets/_Project/Art/Materials";

        [MenuItem("Modern Drone/Setup/Build Drone Prefab")]
        public static GameObject BuildPrefab()
        {
            var config = DroneClasses.Load(DroneClasses.Standard);

            var root = new GameObject("FPV_Default");
            root.layer = GameLayers.Drone;

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = config.frameMass;
            var col = root.AddComponent<BoxCollider>();
            col.size = config.colliderSize;

            var controller = root.AddComponent<DroneController>();
            SetRef(controller, "config", config);

            var props = BuildVisual(root.transform, config);

            var camGo = new GameObject("FpvCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, -0.003f, 0.095f); // at the model's FPV camera lens
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 3000f;
            camGo.AddComponent<AudioListener>();
            var droneCam = camGo.AddComponent<DroneCamera>();
            SetRef(droneCam, "drone", controller);

            var input = root.AddComponent<DroneInput>();
            SetRef(input, "drone", controller);
            SetRef(input, "droneCamera", droneCam);

            var osd = root.AddComponent<FpvOsd>();
            SetRef(osd, "drone", controller);
            SetRef(osd, "input", input);

            var propVisuals = root.AddComponent<PropVisuals>();
            SetRef(propVisuals, "drone", controller);
            var so = new SerializedObject(propVisuals);
            SetArray(so.FindProperty("props"), props);
            so.ApplyModifiedPropertiesWithoutUndo();

            var warhead = root.AddComponent<DroneWarhead>();
            SetRef(warhead, "warhead", WarheadAssets.Load(WarheadAssets.ShapedChargePath));
            SetRef(warhead, "visual", root.transform.Find("Visual"));

            root.AddComponent<AudioSource>();
            SetRef(root.AddComponent<MotorAudio>(), "drone", controller);

            var link = root.AddComponent<DroneLink>();
            SetRef(link, "cableMaterial", GetMaterial("FiberCable", new Color(0.05f, 0.05f, 0.05f)));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ModernDrone] Drone prefab built: {PrefabPath}");
            return prefab;
        }

        const string ModelPath = "Assets/_Project/Art/Models/FPV_Drone/fpv_drone.glb";
        // Motor-to-motor diagonal of the imported model (measured from the frame mesh), metres.
        const float ModelMotorDiagonal = 0.42f;
        // Prop meshes split out of the model in Blender, pivots on the motor axes. Order matches QuadMixer.
        static readonly string[] PropNames = { "Prop_FR", "Prop_FL", "Prop_RR", "Prop_RL" };

        /// <summary>
        /// "FPV Drone" by Jeyhun1985 (Sketchfab, CC BY), see CREDITS.md. Scaled to the 0.3 m reference diagonal that
        /// DroneController rescales per class. Its props are separate meshes (split in Blender) and spin.
        /// </summary>
        static Transform[] BuildVisual(Transform root, DroneConfig config)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);

            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name = "FPV_Drone_Model";
            model.transform.SetParent(visual, false);
            model.transform.localScale = Vector3.one * (0.3f / ModelMotorDiagonal);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Drone;

            var all = model.GetComponentsInChildren<Transform>();
            return System.Array.ConvertAll(PropNames, n => System.Array.Find(all, t => t.name == n));
        }

        static void SetArray(SerializedProperty arr, Object[] values)
        {
            arr.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.enableInstancing = true; // many identical parts (trees, plates) batch on the GPU
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
