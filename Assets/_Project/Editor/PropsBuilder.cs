using System.Collections.Generic;
using ModernDrone.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Editor
{
    /// <summary>
    /// The war-torn landscape around the tree belts, built after them:
    /// - positions beside the belts: a zigzag trench with sandbag bays, a camouflage net over a dugout, supplies
    ///   (crates, ammo boxes, jerrycans, barrels, tyres, rubbish), sometimes a car under a tarp or concrete blocks;
    ///   craters cluster there and the trees around are broken and charred;
    /// - anti-tank lines of dragon's teeth and Czech hedgehogs across fields;
    /// - rural power lines on wooden poles with sagging wires, trees cleared under them;
    /// - burnt-out vehicle hulks from the vehicle models with a charred material;
    /// - deadwood (stumps, fallen trunks, branches) and nettles along the belts;
    /// - a front-line village on the east side (Sketchfab CC BY models, Art/Environment/Village).
    /// Props are Poly Haven CC0 models (Art/Environment/Props, see CREDITS.md); fortifications, wires and
    /// textures are procedural. Layouts, counts and sizes are illustrative.
    /// </summary>
    public static class PropsBuilder
    {
        const string Dir = "Assets/_Project/Art/Environment/Props/";
        const string Models = "Assets/_Project/Art/Models/Vehicles/";
        static readonly string[] WreckModels =
        {
            Models + "T72B3/t72b3.glb", Models + "BMP3M/bmp3m.glb", Models + "BTR82/btr82.glb", Models + "MTLBM/mtlbm.glb",
            Models + "Ural4320/ural4320.glb", Models + "Typhoon/typhoon.glb", Models + "ZIL157/zil157.glb",
        };

        static System.Random _rng;
        static float R() => (float)_rng.NextDouble();
        static float Range(float a, float b) => Mathf.Lerp(a, b, R());

        // ------------------------------------------------------------------ entry points

        /// <summary>Extra bush variety for the belts' shrub edge (TreeBuilder picks every "shrub" prototype).</summary>
        public static void AddShrubVariety(Terrain terrain)
        {
            var data = terrain.terrainData;
            var protos = new List<TreePrototype>(data.treePrototypes);
            foreach (var node in new[] { "shrub_03_a", "shrub_03_b", "shrub_03_c", "shrub_03_d" })
                protos.Add(new TreePrototype { prefab = WorldBuilder.NodePrefab(Dir + "shrub_03/shrub_03.gltf", node, Dir + "shrub_03/", true) });
            data.treePrototypes = protos.ToArray();
        }

        public static void Populate(Terrain terrain, int seed)
        {
            _rng = new System.Random(seed);
            var root = new GameObject("Props").transform;
            var sites = Sites(terrain);
            var village = Village(terrain, root);
            var hotspots = sites.ConvertAll(s => new Vector2(s.pos.x, s.pos.z));
            hotspots.Add(new Vector2(village.center.x, village.center.z));
            WorldBuilder.Scar(terrain, seed, hotspots.ToArray());
            PowerLine(terrain, root, new Vector3(60f, 0f, WorldBuilder.TreeRows[0] + 26f), new Vector3(1940f, 0f, WorldBuilder.TreeRows[0] + 26f));
            PowerLine(terrain, root, new Vector3(1520f, 0f, 40f), new Vector3(1520f, 0f, 1960f));
            BeltEdges(terrain, root);
            foreach (var s in sites) Position(terrain, root, s);
            AntiTankLines(terrain, root);
            Wrecks(terrain, root, sites, village);
            Snags(terrain, sites);
        }

        struct Site { public Vector3 pos; public float side; public float row; }

        /// <summary>Positions beside the belts, away from the map centre where most mission targets stand.</summary>
        static List<Site> Sites(Terrain terrain)
        {
            float size = terrain.terrainData.size.x;
            var sites = new List<Site>();
            for (int tries = 0; sites.Count < 8 && tries < 100; tries++)
            {
                float row = WorldBuilder.TreeRows[_rng.Next(WorldBuilder.TreeRows.Length)];
                float x = Range(180f, size - 180f);
                if (Mathf.Abs(x - size * 0.5f) < 350f) continue;
                if (sites.Exists(s => Vector3.Distance(s.pos, new Vector3(x, 0f, s.pos.z)) < 250f && s.row == row)) continue;
                float side = R() < 0.5f ? -1f : 1f;
                sites.Add(new Site { pos = new Vector3(x, 0f, row + side * 14f), side = side, row = row });
            }
            return sites;
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Ground(Terrain t, Vector3 p)
        {
            p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }

        static Quaternion OnGround(Terrain t, Vector3 p, float yaw)
        {
            var d = t.terrainData;
            var n = d.GetInterpolatedNormal(p.x / d.size.x, p.z / d.size.z);
            return Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>A prop model; the heavy scans have a decimated "_low.glb" (Blender, 2.5-5k triangles), used when present.</summary>
        static GameObject Model(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(Dir + $"{name}/{name}_low.glb") ?? AssetDatabase.LoadAssetAtPath<GameObject>(Dir + $"{name}/{name}.gltf");

        static GameObject Place(Terrain t, Transform parent, GameObject asset, Vector3 at, float yaw, float scale = 1f, bool collider = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            var p = Ground(t, at);
            go.transform.SetPositionAndRotation(p, OnGround(t, p, yaw));
            go.transform.localScale = Vector3.one * scale;
            go.isStatic = true;
            if (collider) AddBoxCollider(go);
            return go;
        }

        static void AddBoxCollider(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            var s = go.transform.lossyScale;
            box.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z) * 0.9f;
            go.layer = GameLayers.Environment;
        }

        static Material Lit(string name, Color color, Texture tex = null, float smooth = 0.1f)
        {
            string path = Dir + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", smooth);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D Tex(string path, bool alpha = false)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp != null && alpha && !imp.alphaIsTransparency)
            {
                imp.alphaIsTransparency = true;
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.5f;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = Dir + "Meshes/" + name + ".asset";
            System.IO.Directory.CreateDirectory(Dir + "Meshes");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ positions

        static void Position(Terrain t, Transform root, Site s)
        {
            var group = new GameObject("Position").transform;
            group.SetParent(root, false);
            float across = s.side;

            // Zigzag trench along the belt, on its field side.
            var pts = new List<Vector3>();
            float half = Range(35f, 60f);
            for (float x = -half; x <= half; x += 8f)
                pts.Add(new Vector3(s.pos.x + x, 0f, s.row + across * (13f + ((int)((x + half) / 8f) % 2 == 0 ? 0f : 2.5f))));
            Trench(t, group, pts);
            for (int i = 1; i < pts.Count - 1; i += 3) SandbagBay(t, group, pts[i] + new Vector3(0f, 0f, across * 1.6f), across > 0 ? 0f : 180f);

            // Dugout under a camouflage net between the trench and the belt.
            var dug = new Vector3(s.pos.x + Range(-20f, 20f), 0f, s.row + across * 9.5f);
            CamoNet(t, group, dug, Range(0f, 30f));
            WorldBuilder.ClearAround(t, dug, 4f);

            // Supplies around the dugout.
            string[] stuff = { "old_military_crate", "ammo_box", "metal_jerrycan_green", "barrel_03", "old_tyre", "trashbag", "ammo_box", "metal_jerrycan_green" };
            int n = 4 + _rng.Next(6);
            for (int i = 0; i < n; i++)
                Place(t, group, Model(stuff[_rng.Next(stuff.Length)]), dug + new Vector3(Range(-6f, 6f), 0f, across * Range(-1.5f, 4f)), R() * 360f);
            if (R() < 0.4f)
            {
                var car = dug + new Vector3(Range(10f, 18f) * (R() < 0.5f ? -1f : 1f), 0f, across * 1f);
                WorldBuilder.ClearAround(t, car, 4f);
                Place(t, group, Model("covered_car"), car, 90f + Range(-10f, 10f), 1f, true);
            }
            if (R() < 0.35f)
                for (int i = 0; i < 3; i++)
                    Place(t, group, Model("concrete_road_barrier_02"), s.pos + new Vector3(-half - 6f + i * 1.7f, 0f, across * 4f), Range(-6f, 6f), 1f, true);
        }

        static Material _trenchMat;

        /// <summary>Trench as decal strips: dark floor between spoil heaps, reading as a zigzag line from the air.</summary>
        static void Trench(Terrain t, Transform parent, List<Vector3> pts)
        {
            if (_trenchMat == null)
            {
                _trenchMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Decals/Trench.mat");
                if (_trenchMat == null)
                {
                    _trenchMat = new Material(Shader.Find("Shader Graphs/Decal"));
                    AssetDatabase.CreateAsset(_trenchMat, "Assets/_Project/Art/Decals/Trench.mat");
                }
                var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Art/Decals/Trench.png");
                if (imp != null && !imp.alphaIsTransparency) { imp.alphaIsTransparency = true; imp.wrapModeV = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
                _trenchMat.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Decals/Trench.png"));
                _trenchMat.enableInstancing = true;
                EditorUtility.SetDirty(_trenchMat);
            }
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = Ground(t, pts[i - 1]), b = Ground(t, pts[i]);
                var dir = (b - a); dir.y = 0f;
                float len = dir.magnitude + 1.5f; // overlap at the corners
                var across = Vector3.Cross(Vector3.up, dir.normalized);
                var go = new GameObject("Trench");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation((a + b) * 0.5f + Vector3.up * 0.6f, Quaternion.LookRotation(Vector3.down, across));
                var dp = go.AddComponent<DecalProjector>();
                dp.material = _trenchMat;
                dp.size = new Vector3(len, 3.6f, 1.2f);
                dp.pivot = new Vector3(0f, 0f, 0.6f);
                dp.uvScale = new Vector2(len / 14f, 1f);
                dp.drawDistance = 900f;
                WorldBuilder.ClearAround(t, (a + b) * 0.5f, len * 0.5f);
            }
        }

        static Mesh _bag;
        static Material _burlap;

        /// <summary>A horseshoe of sandbags two layers high.</summary>
        static void SandbagBay(Terrain t, Transform parent, Vector3 at, float yaw)
        {
            _bag ??= SaveMesh(LowSphere(8, 4, new Vector3(0.28f, 0.08f, 0.16f)), "Sandbag");
            _burlap ??= Lit("Burlap", new Color(0.52f, 0.46f, 0.34f), null, 0.05f);
            var bay = new GameObject("Sandbags").transform;
            bay.SetParent(parent, false);
            var p = Ground(t, at);
            bay.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            for (int layer = 0; layer < 2; layer++)
                for (int i = 0; i < 9; i++)
                {
                    float a = Mathf.Lerp(-100f, 100f, (i + (layer % 2) * 0.5f) / 8.5f) * Mathf.Deg2Rad;
                    var local = new Vector3(Mathf.Sin(a) * 1.4f, 0.08f + layer * 0.15f, Mathf.Cos(a) * 1.4f);
                    var go = MeshObject("Bag", bay, _bag, _burlap);
                    go.transform.localPosition = local;
                    go.transform.localRotation = Quaternion.Euler(Range(-4f, 4f), a * Mathf.Rad2Deg + 90f, Range(-4f, 4f));
                }
        }

        static Mesh LowSphere(int seg, int rings, Vector3 scale)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= seg; s++)
            {
                float th = Mathf.PI * r / rings, ph = 2f * Mathf.PI * s / seg;
                var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                v.Add(Vector3.Scale(d, scale));
                n.Add(new Vector3(d.x / scale.x, d.y / scale.y, d.z / scale.z).normalized);
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = r * (seg + 1) + s, b = a + seg + 1;
                tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
            var m = new Mesh { name = "LowSphere" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        static Material _net;

        /// <summary>Camouflage net draped over four poles about 1.8 m high, sagging between them, edges to the ground.</summary>
        static void CamoNet(Terrain t, Transform parent, Vector3 at, float yaw)
        {
            if (_net == null)
            {
                _net = Lit("CamoNet", Color.white, Tex(Dir + "CamoNet.png", true), 0.05f);
                _net.SetFloat("_AlphaClip", 1f);
                _net.SetFloat("_Cutoff", 0.5f);
                _net.EnableKeyword("_ALPHATEST_ON");
                _net.SetFloat("_Cull", 0f);
                _net.renderQueue = (int)RenderQueue.AlphaTest;
                _net.SetOverrideTag("RenderType", "TransparentCutout");
            }
            const int nx = 10, nz = 8;
            float w = 8f, d = 6f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                float u = x / (float)nx, q = z / (float)nz;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(q, 1f - q));
                float h = Mathf.Lerp(0.1f, 1.8f, Mathf.Clamp01(edge * 5f)) - 0.35f * Mathf.Sin(u * Mathf.PI * 2f) * Mathf.Sin(q * Mathf.PI * 2f);
                v.Add(new Vector3((u - 0.5f) * w, h, (q - 0.5f) * d));
                uv.Add(new Vector2(u * w / 3f, q * d / 3f));
            }
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + nx + 1;
                tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
            var m = new Mesh { name = "CamoNet" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var go = MeshObject("CamoNet", parent, SaveMesh(m, "CamoNet"), _net);
            var p = Ground(t, at);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
        }

        // ------------------------------------------------------------------ obstacles

        static void AntiTankLines(Terrain t, Transform root)
        {
            float size = t.terrainData.size.x;
            var teethMat = ConcreteMaterial();
            var tooth = SaveMesh(Frustum(1.1f, 0.3f, 0.95f), "DragonTooth");
            var rust = Lit("RustySteel", new Color(0.75f, 0.6f, 0.5f), AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Charred.png"), 0.25f);
            var hedgehog = SaveMesh(Hedgehog(), "Hedgehog");
            for (int line = 0; line < 4; line++)
            {
                var group = new GameObject(line % 2 == 0 ? "DragonsTeeth" : "Hedgehogs").transform;
                group.SetParent(root, false);
                int band = 1 + _rng.Next(WorldBuilder.TreeRows.Length - 1);
                float z = WorldBuilder.TreeRows[band] - Range(40f, 90f);
                float x0 = Range(150f, size - 450f);
                if (Mathf.Abs(x0 + 150f - size * 0.5f) < 400f) x0 = x0 < size * 0.5f ? x0 - 300f : x0 + 300f;
                x0 = Mathf.Clamp(x0, 120f, size - 420f);
                float len = Range(150f, 280f);
                if (line % 2 == 0)
                {
                    for (float x = 0; x < len; x += 1.9f)
                        for (int r = 0; r < 3; r++)
                        {
                            var p = new Vector3(x0 + x + (r % 2) * 0.95f, 0f, z + r * 1.9f);
                            var go = MeshObject("Tooth", group, tooth, teethMat);
                            var g = Ground(t, p);
                            go.transform.SetPositionAndRotation(g - Vector3.up * 0.1f, OnGround(t, g, Range(-4f, 4f)));
                            go.AddComponent<BoxCollider>().size = new Vector3(1.1f, 0.95f, 1.1f);
                            go.GetComponent<BoxCollider>().center = new Vector3(0f, 0.47f, 0f);
                            go.layer = GameLayers.Environment;
                        }
                }
                else
                {
                    for (float x = 0; x < len; x += Range(3f, 5f))
                    {
                        var p = new Vector3(x0 + x, 0f, z + Range(-1.5f, 1.5f));
                        var go = MeshObject("Hedgehog", group, hedgehog, rust);
                        go.transform.SetPositionAndRotation(Ground(t, p), Quaternion.Euler(0f, R() * 360f, 0f));
                        var c = go.AddComponent<BoxCollider>();
                        c.size = new Vector3(1.6f, 1.2f, 1.6f); c.center = new Vector3(0f, 0.6f, 0f);
                        go.layer = GameLayers.Environment;
                    }
                }
            }
        }

        static Material ConcreteMaterial()
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(Dir + "concrete_road_barrier_02/concrete_road_barrier_02.gltf"))
                if (a is Material m) return m;
            return Lit("Concrete", new Color(0.6f, 0.6f, 0.58f));
        }

        /// <summary>Square frustum (dragon's tooth), base on the ground.</summary>
        static Mesh Frustum(float bottom, float top, float height)
        {
            float b = bottom * 0.5f, t = top * 0.5f;
            var corners = new[] { new Vector3(-b, 0, -b), new Vector3(b, 0, -b), new Vector3(b, 0, b), new Vector3(-b, 0, b),
                                  new Vector3(-t, height, -t), new Vector3(t, height, -t), new Vector3(t, height, t), new Vector3(-t, height, t) };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Quad(int a, int bb, int c, int d)
            {
                int s = v.Count;
                v.Add(corners[a]); v.Add(corners[bb]); v.Add(corners[c]); v.Add(corners[d]);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                tri.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            Quad(0, 1, 5, 4); Quad(1, 2, 6, 5); Quad(2, 3, 7, 6); Quad(3, 0, 4, 7); Quad(4, 5, 6, 7);
            var m = new Mesh { name = "Frustum" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Czech hedgehog: three 1.8 m steel beams crossed at the middle, standing on three of their ends.</summary>
        static Mesh Hedgehog()
        {
            var combine = new List<CombineInstance>();
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            // A cube's three axes rotated so its body diagonal is vertical: each beam 54.7 deg from vertical.
            var stand = Quaternion.FromToRotation(new Vector3(1, 1, 1).normalized, Vector3.up);
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                var rot = stand * Quaternion.FromToRotation(Vector3.right, axis);
                combine.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(Vector3.zero, rot, new Vector3(1.8f, 0.12f, 0.12f)) });
            }
            var m = new Mesh { name = "Hedgehog" };
            m.CombineMeshes(combine.ToArray(), true, true);
            var verts = m.vertices;
            float minY = float.MaxValue;
            foreach (var p in verts) minY = Mathf.Min(minY, p.y);
            for (int i = 0; i < verts.Length; i++) verts[i].y -= minY;
            m.vertices = verts;
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ power lines

        static GameObject[] _poles;
        static Material _wire;

        /// <summary>Wooden poles of Poly Haven's modular pole kit (its assembled presets), one every 40-50 m.</summary>
        static GameObject[] Poles()
        {
            if (_poles != null && _poles[0] != null) return _poles;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "modular_electricity_poles/modular_electricity_poles.gltf");
            _poles = new GameObject[2];
            string[] presets = { "preset_02_", "preset_03_" };
            for (int i = 0; i < 2; i++)
            {
                var inst = Object.Instantiate(src);
                var root = new GameObject("Pole_" + i);
                Transform pole = null;
                var parts = new List<Transform>();
                foreach (Transform c in inst.transform)
                    if (c.name.StartsWith(presets[i])) { parts.Add(c); if (c.name == presets[i] + "pole") pole = c; }
                var pr = pole.GetComponent<Renderer>().bounds;
                var baseAt = new Vector3(pr.center.x, pr.min.y, pr.center.z);
                foreach (var c in parts) c.SetParent(root.transform, true);
                foreach (Transform c in root.transform) c.position -= baseAt;
                Object.DestroyImmediate(inst);
                var col = root.AddComponent<CapsuleCollider>();
                col.radius = 0.15f; col.height = pr.size.y; col.center = new Vector3(0f, pr.size.y * 0.5f, 0f);
                foreach (var tr in root.GetComponentsInChildren<Transform>()) tr.gameObject.layer = GameLayers.Environment;
                _poles[i] = PrefabUtility.SaveAsPrefabAsset(root, Dir + $"Pole_{i}.prefab");
                Object.DestroyImmediate(root);
            }
            return _poles;
        }

        /// <summary>
        /// A 0.4-10 kV style rural line: poles every 40-50 m, three wires with catenary-like sag (~1 m) as thin tubes,
        /// trees and bushes cleared under it. Wire attachment is approximated from the crossarm's size.
        /// </summary>
        static void PowerLine(Terrain t, Transform root, Vector3 from, Vector3 to)
        {
            var poles = Poles();
            _wire ??= Lit("Wire", new Color(0.18f, 0.18f, 0.2f), null, 0.4f);
            var group = new GameObject("PowerLine").transform;
            group.SetParent(root, false);
            var dir = (to - from); dir.y = 0f;
            float total = dir.magnitude;
            dir.Normalize();
            var across = Vector3.Cross(Vector3.up, dir);
            var tops = new List<Vector3>();
            float yaw = Quaternion.LookRotation(across).eulerAngles.y; // crossarm (model X) across the line
            for (float s = 0f; s <= total; s += Range(40f, 50f))
            {
                var p = Ground(t, from + dir * s);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(poles[R() < 0.15f ? 1 : 0], group);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(Range(-1.5f, 1.5f), yaw + 90f, Range(-1.5f, 1.5f)));
                tops.Add(p + Vector3.up * 5.75f);
            }
            WorldBuilder.ClearVegetationAlong(t, new[] { from, to }, 5f);

            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            float[] offsets = { -0.55f, 0f, 0.55f };
            float[] lift = { 0f, 0.25f, 0f };
            for (int i = 1; i < tops.Count; i++)
                for (int w = 0; w < 3; w++)
                {
                    var a = tops[i - 1] + across * offsets[w] + Vector3.up * lift[w];
                    var b = tops[i] + across * offsets[w] + Vector3.up * lift[w];
                    var pts = new Vector3[11];
                    for (int k = 0; k <= 10; k++)
                    {
                        float u = k / 10f;
                        pts[k] = Vector3.Lerp(a, b, u) - Vector3.up * (4f * 1.0f * u * (1f - u));
                    }
                    WireTube(pts, 0.013f, v, n, tri);
                }
            var m = new Mesh { name = "Wires", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            MeshObject("Wires", group, SaveMesh(m, $"Wires_{(int)from.x}_{(int)from.z}"), _wire, false);
        }

        static void WireTube(Vector3[] pts, float r, List<Vector3> v, List<Vector3> n, List<int> tri)
        {
            int start = v.Count;
            for (int i = 0; i < pts.Length; i++)
            {
                var d = (pts[Mathf.Min(i + 1, pts.Length - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                var s = Vector3.Cross(d, Vector3.up).normalized;
                var u = Vector3.Cross(s, d);
                for (int k = 0; k < 3; k++)
                {
                    float a = k / 3f * Mathf.PI * 2f;
                    var nrm = s * Mathf.Cos(a) + u * Mathf.Sin(a);
                    v.Add(pts[i] + nrm * r); n.Add(nrm);
                }
            }
            for (int i = 0; i < pts.Length - 1; i++)
                for (int k = 0; k < 3; k++)
                {
                    int a = start + i * 3 + k, b = start + i * 3 + (k + 1) % 3;
                    tri.AddRange(new[] { a, a + 3, b, b, a + 3, b + 3 });
                }
        }

        // ------------------------------------------------------------------ belt edges

        /// <summary>Deadwood along the belts (stumps, fallen trunks, branches) and nettles at their edges.</summary>
        static void BeltEdges(Terrain t, Transform root)
        {
            var group = new GameObject("Deadwood").transform;
            group.SetParent(root, false);
            float size = t.terrainData.size.x;
            string[] wood = { "tree_stump_01", "tree_stump_02", "dead_tree_trunk", "dead_tree_trunk_02", "dry_branches_medium_01", "dry_branches_medium_01" };
            foreach (float row in WorldBuilder.TreeRows)
                for (float x = 110f; x < size - 110f; x += Range(25f, 60f))
                {
                    string m = wood[_rng.Next(wood.Length)];
                    Place(t, group, Model(m), new Vector3(x, 0f, row + (R() < 0.5f ? -1f : 1f) * Range(5f, 9f)), R() * 360f, Range(0.8f, 1.2f), m.StartsWith("dead") || m.StartsWith("tree"));
                }

            // Nettles as terrain trees (cleared with the other vegetation where things are placed).
            var data = t.terrainData;
            var protos = new List<TreePrototype>(data.treePrototypes);
            int first = protos.Count;
            string[] nodes = { "nettle_plant_medium_a_LOD0", "nettle_plant_medium_b_LOD0", "nettle_plant_tall_a_LOD0", "nettle_plant_tall_b_LOD0" };
            foreach (var node in nodes)
                protos.Add(new TreePrototype { prefab = WorldBuilder.NodePrefab(Dir + "nettle_plant/nettle_plant.gltf", node, Dir + "nettle_plant/", false) });
            data.treePrototypes = protos.ToArray();
            var trees = new List<TreeInstance>(data.treeInstances);
            foreach (float row in WorldBuilder.TreeRows)
                for (float x = 105f; x < size - 105f; x += Range(4f, 12f))
                {
                    var p = new Vector3(x, 0f, row + (R() < 0.5f ? -1f : 1f) * Range(8.5f, 11f));
                    var tp = new Vector3(p.x / size, 0f, p.z / size);
                    tp.y = data.GetInterpolatedHeight(tp.x, tp.z) / data.size.y;
                    float sc = Range(0.8f, 1.3f);
                    trees.Add(new TreeInstance { position = tp, prototypeIndex = first + _rng.Next(nodes.Length), widthScale = sc, heightScale = sc, rotation = R() * 6.28f, color = Color.white, lightmapColor = Color.white });
                }
            data.SetTreeInstances(trees.ToArray(), true);
        }

        // ------------------------------------------------------------------ village

        const string VillageDir = "Assets/_Project/Art/Environment/Village/";

        static GameObject VillageModel(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(VillageDir + name + "_game.glb");

        /// <summary>
        /// A front-line village on the east edge between two belts: a dirt street with houses on both sides - intact
        /// khatas and log houses, shelled shells and a scanned war-damaged house - barns behind the yards, garages and
        /// a water tower at the far end, a grain elevator and a bus stop at the entrance, burnt-out cars in the street.
        /// Buildings stand upright, slightly sunk into slopes, with mesh colliders (they stop drones and block the
        /// radio link like the tree belts). Returns the village's footprint. Layout illustrative.
        /// </summary>
        static Bounds Village(Terrain t, Transform root)
        {
            var group = new GameObject("Village").transform;
            group.SetParent(root, false);
            Vector3 a = new Vector3(1600f, 0f, 1000f), b = new Vector3(1950f, 0f, 1045f);
            var street = new[] { a, Vector3.Lerp(a, b, 0.5f) + new Vector3(0f, 0f, 6f), b };
            // Yards and gardens: a grass band over the fields, then the dirt street through it.
            WorldBuilder.PaintMeadow(t, new[] { a - (b - a).normalized * 60f, street[1], b + (b - a).normalized * 45f }, 70f);
            WorldBuilder.PaintRoad(t, street, 6f);
            var dir = (b - a).normalized;
            var across = Vector3.Cross(Vector3.up, dir);
            float streetYaw = Quaternion.LookRotation(dir).eulerAngles.y;
            float length = Vector3.Distance(a, b);

            GameObject Building(string name, Vector3 at, float yaw, float clear)
            {
                var model = VillageModel(name);
                if (model == null) return null;
                WorldBuilder.ClearAround(t, at, clear);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, group);
                var g = Ground(t, at);
                go.transform.SetPositionAndRotation(g - Vector3.up * 0.3f, Quaternion.Euler(0f, yaw, 0f));
                go.isStatic = true;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    mf.gameObject.layer = GameLayers.Environment;
                    mf.gameObject.isStatic = true;
                }
                return go;
            }

            var gardens = new List<Vector3>();
            string[] houses = { "house_khata", "house_wooden", "house_khata", "ruin_shell", "house_wooden", "ruin_lypivka", "ruin_shell", "house_khata" };
            int h = 0;
            for (float s = 45f; s < length - 50f; s += Range(30f, 38f))
                foreach (float side in new[] { -1f, 1f })
                {
                    if (R() < 0.12f) continue; // an empty plot
                    var plot = a + dir * (s + Range(-4f, 4f)) + across * side * Range(15f, 19f);
                    string name = houses[(h++ + _rng.Next(3)) % houses.Length];
                    Building(name, plot, streetYaw + (side > 0f ? 90f : -90f) + Range(-6f, 6f), 9f);
                    if (R() < 0.45f)
                        Building("barn", plot + across * side * Range(18f, 24f) + dir * Range(-6f, 6f), streetYaw + Range(-10f, 10f) + (R() < 0.5f ? 0f : 90f), 7f);
                    if (R() < 0.6f)
                        Building("fences_wood", a + dir * (s + Range(-8f, 8f)) + across * side * 8.5f, streetYaw + 90f, 3f);
                    // Garden behind the house: a few small fruit-tree-sized trees and bushes.
                    int n = 3 + _rng.Next(5);
                    for (int i = 0; i < n; i++)
                        gardens.Add(plot + across * side * Range(10f, 30f) + dir * Range(-14f, 14f));
                }

            // Far end: garages behind a concrete fence, the water tower.
            var end = b + dir * 25f;
            Building("garages", end + across * 18f, streetYaw + 90f, 12f);
            Building("fence_concrete", end + across * 8f + dir * 4f, streetYaw, 5f);
            Building("water_tower", end - across * 20f + dir * 8f, Range(0f, 360f), 6f);
            // Entrance: the elevator along the road north of the street, a bus stop.
            var elevator = Building("elevator", a - dir * 40f + across * 40f, streetYaw, 50f);
            if (elevator != null)
            {
                var concrete = Lit("ElevatorConcrete", new Color(0.55f, 0.53f, 0.5f), AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "concrete_road_barrier_02/textures/concrete_road_barrier_02_diff_1k.jpg"), 0.1f);
                foreach (var r in elevator.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = concrete;
                    r.sharedMaterials = mats;
                }
            }
            Building("bus_stop", a + dir * 12f - across * 7f, streetYaw, 3f);
            Gardens(t, gardens);
            // Burnt-out cars in the street.
            for (int i = 0; i < 3; i++)
            {
                var car = Building(i % 2 == 0 ? "burnt_cars_a" : "burnt_cars_b", a + dir * Range(40f, length - 40f) + across * Range(-2.5f, 2.5f), streetYaw + Range(-30f, 30f), 4f);
                if (car != null) car.transform.position += Vector3.up * 0.2f;
            }

            var box = new Bounds((a + b) * 0.5f, Vector3.zero);
            box.Encapsulate(a - dir * 90f + across * 60f);
            box.Encapsulate(b + dir * 50f - across * 60f);
            box.Encapsulate(a - across * 60f);
            box.Encapsulate(b + across * 60f);
            box.Expand(new Vector3(0f, 1000f, 0f));
            return box;
        }

        /// <summary>Garden trees as terrain trees: elm and locust at 30-55 % scale (fruit-tree size), some shelled.</summary>
        static void Gardens(Terrain t, List<Vector3> spots)
        {
            var data = t.terrainData;
            var protos = new List<TreePrototype>(data.treePrototypes);
            var kinds = new List<int>();
            for (int i = 0; i < protos.Count; i++)
            {
                var n = protos[i].prefab != null ? protos[i].prefab.name : "";
                if (n.StartsWith("Elm") || n.StartsWith("Locust") || n.StartsWith("Oak")) kinds.Add(i);
            }
            if (kinds.Count == 0) return;
            float size = data.size.x;
            var trees = new List<TreeInstance>(data.treeInstances);
            foreach (var p in spots)
            {
                var tp = new Vector3(p.x / size, 0f, p.z / size);
                tp.y = data.GetInterpolatedHeight(tp.x, tp.z) / data.size.y;
                float sc = Range(0.3f, 0.55f);
                trees.Add(new TreeInstance { position = tp, prototypeIndex = kinds[_rng.Next(kinds.Count)], widthScale = sc * Range(1.1f, 1.5f), heightScale = sc, rotation = R() * 6.28f, color = Color.white, lightmapColor = Color.white });
            }
            data.SetTreeInstances(trees.ToArray(), true);
        }

        // ------------------------------------------------------------------ war damage

        static Material _charred;

        /// <summary>
        /// Burnt-out hulks at half the positions, out on the field, plus a few on their own. Tracked vehicles lose
        /// their turret - blown off by the ammunition cooking off, it lies upside down, on its side or askew several
        /// metres away (a T-72's furthest) - and their hull settles; trucks sit low on burnt-out wheels at a list.
        /// Scorched ground and charred debris around each. Distances and angles are illustrative.
        /// </summary>
        static void Wrecks(Terrain t, Transform root, List<Site> sites, Bounds village)
        {
            _charred ??= Lit("Charred", Color.white, AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Charred.png"), 0.15f);
            var group = new GameObject("Wrecks").transform;
            group.SetParent(root, false);
            var spots = new List<Vector3>();
            foreach (var s in sites)
                if (R() < 0.5f) spots.Add(s.pos + new Vector3(Range(-30f, 30f), 0f, s.side * Range(20f, 45f)));
            float size = t.terrainData.size.x;
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(Range(150f, size - 150f), 0f, Range(150f, size - 150f));
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(size, size) * 0.5f) > 400f && !village.Contains(new Vector3(p.x, village.center.y, p.z))) spots.Add(p);
            }
            var scorch = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Decals/Crater.mat");
            var tyre = Model("old_tyre");
            var debrisMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            foreach (var p in spots)
            {
                string path = WreckModels[_rng.Next(WreckModels.Length)];
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                WorldBuilder.ClearAround(t, p, 8f);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, group);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = "Wreck_" + model.name;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = Charred(mats[k]);
                    r.sharedMaterials = mats;
                }
                var turret = System.Array.Find(go.GetComponentsInChildren<Transform>(), x => x.name == "Turret");
                bool truck = turret == null;
                var g = Ground(t, p);
                float yaw = R() * 360f;
                go.transform.SetPositionAndRotation(g, OnGround(t, g, yaw) * Quaternion.Euler(Range(-4f, 4f), 0f, truck ? Range(5f, 9f) * (R() < 0.5f ? -1f : 1f) : Range(-4f, 4f)));
                // Sit on the ground (pivots differ), lower for settled suspension or burnt tyres.
                SitOnGround(t, go, truck ? 0.35f : 0.15f);

                if (turret != null)
                {
                    turret.SetParent(group, true);
                    turret.name = go.name + "_Turret";
                    bool t72 = model.name.Contains("t72");
                    float dist = t72 ? Range(6f, 15f) : Range(3f, 8f);
                    float dir = R() * Mathf.PI * 2f;
                    var tp = Ground(t, g + new Vector3(Mathf.Cos(dir), 0f, Mathf.Sin(dir)) * dist);
                    float roll = R() < 0.5f ? 180f + Range(-12f, 12f) : Range(-25f, 25f); // upside down or askew
                    turret.SetPositionAndRotation(tp, OnGround(t, tp, R() * 360f) * Quaternion.Euler(Range(-15f, 15f), 0f, roll));
                    SitOnGround(t, turret.gameObject, 0.05f);
                    AddBoxCollider(turret.gameObject);
                    WorldBuilder.ClearAround(t, tp, 3f);
                }
                AddBoxCollider(go);

                // Scorched ground under the hulk.
                if (scorch != null)
                {
                    var sc = new GameObject("Scorch");
                    sc.transform.SetParent(group, false);
                    // Shallow box: a deeper one smears the texture down the hulk's sides.
                    sc.transform.SetPositionAndRotation(g + Vector3.up * 0.4f, Quaternion.Euler(90f, R() * 360f, 0f));
                    var dp = sc.AddComponent<DecalProjector>();
                    dp.material = scorch;
                    dp.size = new Vector3(14f, 14f, 0.8f);
                    dp.pivot = new Vector3(0f, 0f, 0.4f);
                    dp.fadeFactor = 0.9f;
                    dp.drawDistance = 900f;
                }
                // Charred debris: tyres and plates thrown around.
                int n = 4 + _rng.Next(6);
                for (int i = 0; i < n; i++)
                {
                    float a = R() * Mathf.PI * 2f, d = Range(3f, 12f);
                    var dpos = g + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                    if (R() < 0.35f && tyre != null)
                    {
                        var ty = Place(t, group, tyre, dpos, R() * 360f, Range(1.4f, 1.9f));
                        ty.transform.rotation *= Quaternion.Euler(90f, 0f, 0f);
                        foreach (var r in ty.GetComponentsInChildren<Renderer>()) r.sharedMaterial = _charred;
                    }
                    else
                    {
                        var plate = MeshObject("Debris", group, debrisMesh, _charred);
                        var dg = Ground(t, dpos);
                        plate.transform.SetPositionAndRotation(dg + Vector3.up * 0.05f, Quaternion.Euler(Range(-20f, 20f), R() * 360f, Range(-20f, 20f)));
                        plate.transform.localScale = new Vector3(Range(0.4f, 1.4f), Range(0.03f, 0.08f), Range(0.3f, 1f));
                    }
                }
            }
        }

        static readonly Dictionary<Material, Material> CharredCopies = new Dictionary<Material, Material>();

        /// <summary>
        /// Burnt version of a model's own material: blackened and matt, keeping its texture detail (panel lines,
        /// rivets) so nothing stretches the way a shared soot texture does over the models' UVs.
        /// </summary>
        static Material Charred(Material original)
        {
            if (original == null) return _charred;
            if (CharredCopies.TryGetValue(original, out var copy) && copy != null) return copy;
            string path = Dir + "Charred/" + original.name.Replace("/", "_") + "_charred.mat";
            copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                System.IO.Directory.CreateDirectory(Dir + "Charred");
                copy = new Material(original);
                AssetDatabase.CreateAsset(copy, path);
            }
            else copy.CopyPropertiesFromMaterial(original);
            var soot = new Color(0.24f, 0.19f, 0.16f); // soot with a rust-brown cast
            foreach (var prop in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                if (copy.HasProperty(prop)) copy.SetColor(prop, copy.GetColor(prop) * soot);
            foreach (var prop in new[] { "roughnessFactor" }) if (copy.HasProperty(prop)) copy.SetFloat(prop, 1f);
            foreach (var prop in new[] { "_Smoothness", "_Glossiness" }) if (copy.HasProperty(prop)) copy.SetFloat(prop, 0.05f);
            foreach (var prop in new[] { "emissiveFactor", "_EmissionColor" }) if (copy.HasProperty(prop)) copy.SetColor(prop, Color.black);
            EditorUtility.SetDirty(copy);
            CharredCopies[original] = copy;
            return copy;
        }

        /// <summary>Moves an object so its lowest point is <paramref name="sink"/> below the ground under it.</summary>
        static void SitOnGround(Terrain t, GameObject go, float sink)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float ground = Ground(t, b.center).y;
            go.transform.position += Vector3.up * (ground - b.min.y - sink);
        }

        /// <summary>Trees around the positions are broken and charred.</summary>
        static void Snags(Terrain t, List<Site> sites)
        {
            var data = t.terrainData;
            var snags = TreeBuilder.Snags();
            var protos = new List<TreePrototype>(data.treePrototypes);
            int first = protos.Count;
            foreach (var s in snags) protos.Add(new TreePrototype { prefab = s });
            data.treePrototypes = protos.ToArray();
            var trees = data.treeInstances;
            float size = data.size.x;
            for (int i = 0; i < trees.Length; i++)
            {
                var proto = protos[trees[i].prototypeIndex].prefab;
                if (proto == null || proto.GetComponent<LODGroup>() == null || proto.name.StartsWith("Snag")) continue;
                var w = new Vector3(trees[i].position.x * size, 0f, trees[i].position.z * size);
                foreach (var s in sites)
                {
                    float d = Vector2.Distance(new Vector2(w.x, w.z), new Vector2(s.pos.x, s.row));
                    if (d < 60f && R() < Mathf.Lerp(0.9f, 0.3f, d / 60f))
                    {
                        trees[i].prototypeIndex = first + _rng.Next(snags.Length);
                        break;
                    }
                }
            }
            data.SetTreeInstances(trees, true);
        }
    }
}
