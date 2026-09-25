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
            rb.mass = config.TotalMass;
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
            var arr = so.FindProperty("discs");
            arr.arraySize = props.Length;
            for (int i = 0; i < props.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = props[i];
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
        // Prop discs in Visual space, measured from the scaled model: hub centres, prop radius, prop plane height.
        static readonly Vector3[] PropHubs =
        {
            new Vector3(0.101f, 0.012f, 0.106f), new Vector3(-0.101f, 0.012f, 0.106f),
            new Vector3(0.101f, 0.012f, -0.119f), new Vector3(-0.101f, 0.012f, -0.119f),
        };
        const float PropRadius = 0.078f;

        /// <summary>
        /// "FPV Drone" by Jeyhun1985 (Sketchfab, CC BY), see CREDITS.md. Scaled to the 0.3 m reference diagonal that
        /// DroneController rescales per class. Its props are baked into the frame mesh, so blur discs are added per motor.
        /// </summary>
        static Renderer[] BuildVisual(Transform root, DroneConfig config)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);

            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name = "FPV_Drone_Model";
            model.transform.SetParent(visual, false);
            model.transform.localScale = Vector3.one * (0.3f / ModelMotorDiagonal);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Drone;

            var discMat = GetDiscMaterial();
            var discs = new Renderer[PropHubs.Length];
            for (int i = 0; i < discs.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = $"PropDisc_{i}";
                go.layer = GameLayers.Drone;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(visual, false);
                go.transform.localPosition = PropHubs[i];
                go.transform.localScale = new Vector3(PropRadius * 2f, 0.0005f, PropRadius * 2f);
                discs[i] = go.GetComponent<Renderer>();
                discs[i].sharedMaterial = discMat;
                discs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                discs[i].enabled = false;
            }
            return discs;
        }

        static Material GetDiscMaterial()
        {
            string path = $"{MatDir}/PropDisc.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1f); // transparent, alpha blend
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(0.06f, 0.06f, 0.06f, 0.35f));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
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
