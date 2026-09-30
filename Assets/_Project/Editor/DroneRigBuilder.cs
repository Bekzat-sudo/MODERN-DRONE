using System.Collections.Generic;
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
            // Improvised contact fuze taped to the PG-7VL's nose cap (tip found on the 7" mesh). The NonStop model
            // comes with its own fuze probe ahead of its charge.
            if (model == Model.Fpv7)
            {
                var fuze = new GameObject("FuzeWhiskers");
                fuze.transform.SetParent(root.transform, false);
                fuze.AddComponent<MeshFilter>().sharedMesh = FuzeMesh("FPV7", new Vector3(0f, -0.053f, 0.169f), 0.011f);
                fuze.AddComponent<MeshRenderer>().sharedMaterials = new[]
                {
                    GetMaterial("FuzeSteel", new Color(0.72f, 0.73f, 0.75f)), GetMaterial("FuzeTape", new Color(0.04f, 0.04f, 0.045f)),
                };
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Drone;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Improvised electronic contact fuze as fitted to strike FPVs: two spring-steel "whisker" wires (1.8 mm)
        /// taped to the warhead's nose, reaching 10-12 cm ahead and slightly apart, that are crushed together at
        /// impact and close the detonator circuit; a thin lead runs back along the warhead to the fuze board on the
        /// frame, which the flight controller arms remotely (x.com/GrandpaRoy2, recovered Ukrainian FPV fuze, 2024;
        /// docs.obriy.airforce: contact fuzes fire on impact above ~7 m/s). Dimensions are illustrative.
        /// Submesh 0 steel, 1 tape and lead.
        /// </summary>
        static Mesh FuzeMesh(string name, Vector3 nose, float noseRadius)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>();
            var steel = new List<int>(); var tape = new List<int>();
            void Tube(IList<Vector3> pts, float r, int sides, List<int> tri)
            {
                int start = v.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    var d = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                    var s1 = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                    var s2 = Vector3.Cross(s1, d);
                    for (int k = 0; k < sides; k++)
                    {
                        float a = k / (float)sides * Mathf.PI * 2f;
                        var nrm = s1 * Mathf.Cos(a) + s2 * Mathf.Sin(a);
                        v.Add(pts[i] + nrm * r); n.Add(nrm);
                    }
                }
                for (int i = 0; i < pts.Count - 1; i++)
                    for (int k = 0; k < sides; k++)
                    {
                        int a0 = start + i * sides + k, a1 = start + i * sides + (k + 1) % sides;
                        tri.AddRange(new[] { a0, a0 + sides, a1, a1, a0 + sides, a1 + sides });
                    }
            }
            // Tape band round the nose cap.
            Tube(new[] { nose + new Vector3(0f, 0f, -0.035f), nose + new Vector3(0f, 0f, -0.012f) }, noseRadius + 0.0025f, 10, tape);
            // Two whiskers: along the cap under the tape, out past the tip, spreading, a small inward hook at the end.
            foreach (float side in new[] { -1f, 1f })
            {
                var pts = new[]
                {
                    nose + new Vector3(side * (noseRadius + 0.002f), 0f, -0.03f),
                    nose + new Vector3(side * (noseRadius * 0.7f), -0.001f, 0.004f),
                    nose + new Vector3(side * 0.02f, -0.004f, 0.055f),
                    nose + new Vector3(side * 0.032f, -0.009f, 0.105f),
                    nose + new Vector3(side * 0.026f, -0.011f, 0.117f),
                };
                Tube(pts, 0.0009f, 5, steel);
            }
            // Lead back along the top of the warhead to the fuze board under the frame.
            Tube(new[] { nose + new Vector3(0f, noseRadius + 0.003f, -0.025f), nose + new Vector3(0f, noseRadius + 0.012f, -0.07f),
                         new Vector3(0f, nose.y + 0.04f, nose.z - 0.14f), new Vector3(0f, -0.012f, nose.z - 0.19f) }, 0.0008f, 4, tape);
            var mesh = new Mesh { name = "FuzeWhiskers_" + name };
            mesh.SetVertices(v); mesh.SetNormals(n);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(steel, 0); mesh.SetTriangles(tape, 1);
            mesh.RecalculateBounds();
            string path = $"Assets/_Project/Art/Models/Weapons/FuzeWhiskers_{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
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
