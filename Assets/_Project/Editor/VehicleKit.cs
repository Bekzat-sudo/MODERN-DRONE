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
        public void Wheels(float[] zs, float halfTrack, float radius, float width, float hp = 90f, float x = 0f)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (float z in zs)
                {
                    Visual("Wheel", new Vector3(x + side * halfTrack, radius, z), new Vector3(radius * 2f, width * 0.5f, radius * 2f), Dark,
                        rot: Quaternion.Euler(0f, 0f, 90f), type: PrimitiveType.Cylinder);
                    minZ = Mathf.Min(minZ, z);
                    maxZ = Mathf.Max(maxZ, z);
                }
                Module(side < 0 ? "Module_Wheels_L" : "Module_Wheels_R", ModuleType.Wheels, hp, 20f,
                    new Vector3(x + side * halfTrack, radius, (minZ + maxZ) * 0.5f), new Vector3(width, radius * 1.8f, maxZ - minZ + radius * 2f));
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

        /// <summary>
        /// Puts an imported model over the damage boxes: the boxes keep their colliders (armor, modules, hull) but stop
        /// rendering, the model draws the vehicle. A model node named <paramref name="movingName"/> is re-parented to
        /// <paramref name="moving"/> (turret, rotor) so it moves and is tossed with it.
        /// </summary>
        public static void AttachModel(Transform root, string path, Vector3 offset, Transform moving = null, string movingName = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            var lod0 = Place(root, asset, "Visual", offset, moving, movingName);
            var lod1Asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace(".glb", "_lod1.glb"));
            var lod2Asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace(".glb", "_lod2.glb"));
            if (lod1Asset == null || lod2Asset == null)
            {
                SetupHatches(root);
                return;
            }

            // Decimated copies (Blender, 25 % / 6 %, materials without images) take the full model's materials by
            // material name. The near LOD casts its shadow from LOD 1's mesh: a T-72 was 247 k triangles in every cascade.
            var materials = FullMaterials(lod0);
            Renderer[] Copy(string name, UnityEngine.Rendering.ShadowCastingMode cast)
            {
                var rs = Place(root, name == "Visual_Shadow" ? lod1Asset : name == "Visual_LOD1" ? lod1Asset : lod2Asset, name, offset, moving, movingName);
                foreach (var r in rs)
                {
                    UseFullMaterials(r, materials);
                    r.shadowCastingMode = cast;
                }
                return rs;
            }
            var lod1 = Copy("Visual_LOD1", UnityEngine.Rendering.ShadowCastingMode.On);
            var lod2 = Copy("Visual_LOD2", UnityEngine.Rendering.ShadowCastingMode.On);
            var shadow = Copy("Visual_Shadow", UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly);
            foreach (var r in lod0) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var near = new Renderer[lod0.Length + shadow.Length];
            lod0.CopyTo(near, 0);
            shadow.CopyTo(near, lod0.Length);

            var group = root.GetComponent<LODGroup>();
            if (group == null) group = root.gameObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            // Screen-relative: the recon zoom keeps full detail; the last LOD is never culled (targets stay visible).
            group.SetLODs(new[] { new LOD(0.15f, near), new LOD(0.04f, lod1), new LOD(0f, lod2) });
            group.RecalculateBounds();
            SetupHatches(root);
        }

        /// <summary>
        /// Model nodes named Hatch_* (one per LOD copy) become one lid on a hinge: they are gathered under a pivot on the
        /// lid's own origin in the model, its hinge (at the hull's axes) carrying a <see cref="Hatch"/>, with a <see cref="FireVent"/> in the
        /// opening and a dark plate inside it that glows while the vehicle burns. Lid mass ~30 kg (estimate).
        /// </summary>
        static void SetupHatches(Transform root)
        {
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Transform>>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Hatch_") || t.GetComponentInParent<Hatch>(true) != null) continue;
                if (!groups.TryGetValue(t.name, out var list)) groups[t.name] = list = new System.Collections.Generic.List<Transform>();
                list.Add(t);
            }
            foreach (var kv in groups)
            {
                var parts = kv.Value;
                var parent = CommonParent(parts);
                var renderers = parts[0].GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                // Lid outline in the hull's frame.
                var b = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
                foreach (var r in renderers)
                {
                    var rb = r.bounds;
                    for (int i = 0; i < 8; i++)
                        b.Encapsulate(root.InverseTransformPoint(rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                }
                // The model's own origin of the lid is its hinge (Tools/Blender/t72_hatches.py puts it on the front edge).
                var pivot = new GameObject(kv.Key + "_Hinge").transform;
                pivot.SetParent(parent, false);
                pivot.SetPositionAndRotation(parts[0].position, root.rotation);
                foreach (var t in parts) t.SetParent(pivot, true);
                pivot.gameObject.AddComponent<Hatch>().Configure(115f, 30f, 0.7f, Vector3.right);

                Vector3 centre = root.TransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                var glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                glow.name = kv.Key + "_Inside";
                Object.DestroyImmediate(glow.GetComponent<Collider>());
                glow.transform.SetParent(parent, false);
                glow.transform.SetPositionAndRotation(centre - root.up * 0.08f, root.rotation);
                glow.transform.localScale = new Vector3(b.size.x * 0.85f, 0.005f, b.size.z * 0.85f);
                var gr = glow.GetComponent<Renderer>();
                gr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Resources/VFX/VFX_HatchGlow.mat");
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var vent = new GameObject(kv.Key + "_Vent");
                vent.transform.SetParent(parent, false);
                vent.transform.SetPositionAndRotation(centre + root.up * 0.05f, root.rotation);
                vent.AddComponent<FireVent>().Configure(VentKind.Hatch, Mathf.PI * 0.25f * b.size.x * b.size.z, pivot.GetComponent<Hatch>(), gr);
            }
        }

        static Transform CommonParent(System.Collections.Generic.List<Transform> parts)
        {
            var p = parts[0].parent;
            while (p != null && !parts.TrueForAll(t => t.IsChildOf(p))) p = p.parent;
            return p;
        }

        /// <summary>The full model's materials by name (the decimated LODs carry the same names, no textures).</summary>
        public static System.Collections.Generic.Dictionary<string, Material> FullMaterials(System.Collections.Generic.IEnumerable<Renderer> renderers)
        {
            var map = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var r in renderers)
                foreach (var m in r.sharedMaterials)
                    if (m != null && !map.ContainsKey(m.name)) map[m.name] = m;
            return map;
        }

        public static void UseFullMaterials(Renderer r, System.Collections.Generic.Dictionary<string, Material> full)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && full.TryGetValue(mats[i].name, out var m)) mats[i] = m;
            r.sharedMaterials = mats;
        }

        /// <summary>Instantiates a model under the root, strips colliders, moves its moving part; returns its renderers.</summary>
        static Renderer[] Place(Transform root, GameObject asset, string name, Vector3 offset, Transform moving, string movingName)
        {
            var visual = Object.Instantiate(asset, root);
            visual.name = name;
            visual.transform.localPosition = offset;
            visual.transform.localRotation = Quaternion.identity;
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (moving != null && movingName != null)
            {
                var part = System.Array.Find(visual.GetComponentsInChildren<Transform>(true), t => t.name == movingName);
                if (part != null) part.SetParent(moving, true);
            }
            return renderers;
        }

        public void AttachModel(string path, Vector3 offset = default, Transform moving = null, string movingName = null) =>
            AttachModel(T, path, offset, moving, movingName);

        /// <summary>
        /// Physical hull that follows the attached model: one hull-layer box per <paramref name="step"/> metres of length,
        /// sized to the model's outline in that slice. Percentiles drop thin whips and antennas. The contact fuze fires
        /// on these; the jet ignores them. Use where hand-placed boxes cannot follow the shape (articulated rigs).
        /// </summary>
        public void SliceHull(float step = 1f, float percentile = 0.97f)
        {
            var visual = T.Find("Visual");
            if (visual == null) return;
            var pts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in visual.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices) pts.Add(T.InverseTransformPoint(mf.transform.TransformPoint(v)));
            if (pts.Count == 0) return;
            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var p in pts) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); }

            var group = Group("HullSlices", Vector3.zero);
            var xs = new System.Collections.Generic.List<float>();
            var ys = new System.Collections.Generic.List<float>();
            for (float z = zMin; z < zMax; z += step)
            {
                xs.Clear(); ys.Clear();
                foreach (var p in pts)
                    if (p.z >= z && p.z < z + step) { xs.Add(p.x); ys.Add(p.y); }
                if (xs.Count < 20) continue;
                xs.Sort(); ys.Sort();
                float x0 = xs[(int)(xs.Count * (1f - percentile))], x1 = xs[(int)(xs.Count * percentile) - 1];
                float y0 = ys[0], y1 = ys[(int)(ys.Count * 0.995f) - 1]; // height keeps all but the thinnest whips
                var go = new GameObject("Slice");
                go.layer = GameLayers.VehicleHull;
                go.transform.SetParent(group, false);
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z + step * 0.5f);
                box.size = new Vector3(x1 - x0, y1 - y0, step);
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
