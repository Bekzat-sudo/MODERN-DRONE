using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.Missions;
using ModernDrone.UI;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Builds the FPV drone prefab and the per-model visual prefabs. The "Visual" child holds the current model's visual
    /// (swapped by DroneController when a different drone model is chosen); physics lives on the root.
    /// </summary>
    public static class DroneRigBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Drones/FPV_Default.prefab";
        const string MatDir = "Assets/_Project/Art/Materials";

        [MenuItem("Modern Drone/Setup/Build Drone Prefab")]
        public static GameObject BuildPrefab()
        {
            VisualPrefab(Model.Fpv7, true);
            VisualPrefab(Model.NonStop, true);
            var config = DroneModels.LoadAll()[0];

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
            camGo.transform.localPosition = config.cameraPosition; // at the model's FPV camera lens
            var cam = camGo.AddComponent<Camera>();
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam).renderPostProcessing = true;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 3000f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ModernDrone.Video.AutoExposure>(); // fast, overshooting CMOS-camera defaults
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

        public enum Model { Fpv7, NonStop }

        const string VisualDir = "Assets/_Project/Prefabs/Drones/";
        // Prop meshes split out of each model in Blender, pivots on the motor axes. Order matches QuadMixer.
        static readonly string[] PropNames = { "Prop_FR", "Prop_FL", "Prop_RR", "Prop_RL" };

        /// <summary>
        /// Visual prefab for a drone model: the imported model under one root, colliders stripped, Drone layer.
        /// "FPV Drone" by Jeyhun1985 is scaled to a 0.3 m motor diagonal (7"); "FPV-dron NonStop" by Viktor_ is already
        /// at 10" scale from Blender. See CREDITS.md.
        /// </summary>
        public static GameObject VisualPrefab(Model model, bool rebuild = false)
        {
            string path = VisualDir + (model == Model.NonStop ? "Visual_NonStop.prefab" : "Visual_FPV7.prefab");
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild) return existing;
            string glb = model == Model.NonStop
                ? "Assets/_Project/Art/Models/FPV_NonStop/nonstop.glb"
                : "Assets/_Project/Art/Models/FPV_Drone/fpv_drone.glb";
            float scale = model == Model.NonStop ? 1f : 0.3f / 0.42f; // 0.42 m: diagonal measured on the 7" mesh

            var root = new GameObject(model == Model.NonStop ? "Visual_NonStop" : "Visual_FPV7");
            var inst = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(glb), root.transform);
            inst.name = "Model";
            inst.transform.localScale = Vector3.one * scale;
            // Contact fuze wire ahead of the warhead nose, as seen at the bottom of strike feeds. Mounts are drone
            // space, measured from each model's camera (just below it) so the loop shows in the lower picture.
            var fuze = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Weapons/fuze_wire.glb");
            if (fuze != null)
            {
                var wire = Object.Instantiate(fuze, root.transform);
                wire.name = "FuzeWire";
                wire.transform.localPosition = model == Model.NonStop ? new Vector3(0f, -0.075f, 0.2f) : new Vector3(0f, -0.085f, 0.13f);
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Drone;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform[] BuildVisual(Transform root, DroneConfig config)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            var model = Object.Instantiate(config.visualPrefab, visual);
            model.name = config.visualPrefab.name;
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
