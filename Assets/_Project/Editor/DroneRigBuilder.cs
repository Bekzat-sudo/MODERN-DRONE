using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.UI;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Builds the default FPV drone prefab. The "Visual" child is placeholder geometry made of primitives:
    /// replace its contents with an imported model; physics lives on the root and does not depend on it.
    /// </summary>
    public static class DroneRigBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Drones/FPV_Default.prefab";
        const string ConfigPath = "Assets/_Project/Data/Drones/FPV_Default.asset";
        const string MatDir = "Assets/_Project/Art/Materials";

        [MenuItem("Modern Drone/Setup/Build Drone Prefab")]
        public static GameObject BuildPrefab()
        {
            var config = AssetDatabase.LoadAssetAtPath<DroneConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DroneConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

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
            camGo.transform.localPosition = new Vector3(0f, 0.04f, 0.1f);
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
            var arr = so.FindProperty("props");
            arr.arraySize = props.Length;
            for (int i = 0; i < props.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = props[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[ModernDrone] Drone prefab built: {PrefabPath}");
            return prefab;
        }

        static Transform[] BuildVisual(Transform root, DroneConfig config)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);

            var frameMat = GetMaterial("DroneFrame", new Color(0.12f, 0.12f, 0.13f));
            var motorMat = GetMaterial("DroneMotor", new Color(0.55f, 0.55f, 0.58f));
            var propMat = GetMaterial("DroneProp", new Color(0.05f, 0.05f, 0.05f));
            var podMat = GetMaterial("DronePayload", new Color(0.30f, 0.34f, 0.24f));

            Prim(PrimitiveType.Cube, "Body", visual, frameMat, new Vector3(0f, 0.01f, 0f), new Vector3(0.07f, 0.035f, 0.16f));
            Prim(PrimitiveType.Cylinder, "PayloadPod", visual, podMat, new Vector3(0f, -0.035f, 0.03f), new Vector3(0.07f, 0.12f, 0.07f), Quaternion.Euler(90f, 0f, 0f));

            float a = config.ArmOffset;
            float armLen = a * Mathf.Sqrt(2f);
            var props = new Transform[QuadMixer.MotorCount];
            for (int i = 0; i < QuadMixer.MotorCount; i++)
            {
                Vector3 p = QuadMixer.MotorPosition(i, a);
                float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                Prim(PrimitiveType.Cube, $"Arm_{i}", visual, frameMat, p * 0.5f, new Vector3(0.022f, 0.008f, armLen), Quaternion.Euler(0f, yaw, 0f));
                Prim(PrimitiveType.Cylinder, $"Motor_{i}", visual, motorMat, p + Vector3.up * 0.015f, new Vector3(0.035f, 0.013f, 0.035f));
                var prop = Prim(PrimitiveType.Cube, $"Prop_{i}", visual, propMat, p + Vector3.up * 0.032f, new Vector3(0.17f, 0.002f, 0.014f));
                props[i] = prop;
            }
            return props;
        }

        static Transform Prim(PrimitiveType type, string name, Transform parent, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = GameLayers.Drone;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
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
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
