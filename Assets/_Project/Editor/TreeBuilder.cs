using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Procedural windbreak trees: a tapered trunk, first-order branches laid out inside a species crown envelope, and
    /// leaf-spray cards (Tools/foliage_textures.py) with normals pointing out of the crown so the canopy shades as one
    /// volume. Three LODs with cross-fade (switch heights profiled: 0.25 / 0.08 cut the FPV view's triangles by ~40 %); planted as
    /// terrain trees (capsule collider on the root) and drawn by TerrainTreeInstancer through the GPU Resident Drawer.
    /// Species after Ukrainian steppe shelterbelts - oak, poplar, black locust ("acacia"), Siberian elm; mature belts
    /// reach 19-23 m (pressto.amu.edu.pl/index.php/biorc/article/view/41622; europeancorrespondent.com, windbreak
    /// forests). Per-species heights, crown sizes and branch angles are illustrative, not measured.
    /// Materials use ModernDrone/Vegetation Lit (URP Lit plus sway in the scene's wind and in blast waves).
    /// </summary>
    public static class TreeBuilder
    {
        const string Dir = "Assets/_Project/Art/Environment/Plants/Trees/";
        public const int Variants = 3;

        public enum Kind { Poplar, Locust, Elm, Oak }

        sealed class Species
        {
            public Kind kind;
            public float heightMin, heightMax;  // m
            public float crownBase;             // crown starts at this fraction of the height
            public float radiusMin, radiusMax;  // widest crown radius, m
            public float shapeBias, shapePower; // envelope r = R * sin(pi * h^bias)^power over the crown height h
            public float branchAngle;           // lower branches, degrees from vertical
            public int branches, cards;
            public float cardSize;              // m
        }

        static readonly Species[] All =
        {
            // Lombardy-type black poplar: narrow column, steep branches.
            new Species { kind = Kind.Poplar, heightMin = 18f, heightMax = 24f, crownBase = 0.06f, radiusMin = 1.6f, radiusMax = 2.3f, shapeBias = 0.8f, shapePower = 0.6f, branchAngle = 22f, branches = 34, cards = 420, cardSize = 1.3f },
            // Black locust: open, irregular crown.
            new Species { kind = Kind.Locust, heightMin = 12f, heightMax = 18f, crownBase = 0.25f, radiusMin = 3.5f, radiusMax = 5f, shapeBias = 0.9f, shapePower = 0.5f, branchAngle = 50f, branches = 16, cards = 360, cardSize = 1.8f },
            // Siberian elm: dense round crown.
            new Species { kind = Kind.Elm, heightMin = 10f, heightMax = 15f, crownBase = 0.15f, radiusMin = 3.2f, radiusMax = 4.5f, shapeBias = 1f, shapePower = 0.6f, branchAngle = 50f, branches = 20, cards = 420, cardSize = 1.6f },
            // Pedunculate oak: broad dome.
            new Species { kind = Kind.Oak, heightMin = 15f, heightMax = 21f, crownBase = 0.2f, radiusMin = 5.5f, radiusMax = 7.5f, shapeBias = 0.75f, shapePower = 0.5f, branchAngle = 60f, branches = 18, cards = 520, cardSize = 1.9f },
        };

        static bool _built;
        static GameObject[] _snags;
        static readonly Dictionary<Kind, GameObject[]> Prefabs = new Dictionary<Kind, GameObject[]>();

        /// <summary>Prefab variants of a species; generated once per editor session (assets are overwritten in place).</summary>
        public static GameObject[] Get(Kind kind)
        {
            if (!_built) BuildAll();
            return Prefabs[kind];
        }

        static void BuildAll()
        {
            var bark = BarkMaterial();
            var leaves = LeafMaterial();
            foreach (var sp in All)
            {
                var list = new GameObject[Variants];
                for (int v = 0; v < Variants; v++) list[v] = BuildPrefab(sp, v, bark, leaves);
                Prefabs[sp.kind] = list;
            }
            AssetDatabase.SaveAssets();
            _built = true;
        }

        // ------------------------------------------------------------------ geometry

        sealed class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> bark = new List<int>(), leaves = new List<int>();

            public void Tube(IList<Vector3> pts, IList<float> radii, int sides)
            {
                int start = v.Count;
                float along = 0f;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) along += Vector3.Distance(pts[i], pts[i - 1]);
                    var dir = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                    var side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                    var up = Vector3.Cross(side, dir);
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        var nrm = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        v.Add(pts[i] + nrm * radii[i]);
                        n.Add(nrm);
                        uv.Add(new Vector2(s / (float)sides * Mathf.Max(1f, Mathf.Round(radii[0] * 6f)), along / 1.5f));
                    }
                }
                int ring = sides + 1;
                for (int i = 0; i < pts.Count - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + i * ring + s, b = a + ring;
                    bark.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }

            public void Card(Vector3 attach, Vector3 up, Vector3 normalHint, Vector3 shading, float size, int tile)
            {
                var right = Vector3.Cross(up, normalHint).normalized;
                var centre = attach + up * size * 0.45f;
                int start = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float x = k == 1 || k == 2 ? 0.5f : -0.5f, y = k >= 2 ? 0.5f : -0.5f;
                    v.Add(centre + right * x * size + up * y * size);
                    n.Add(shading);
                    uv.Add(new Vector2((tile + x + 0.5f) / 4f, y + 0.5f));
                }
                leaves.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            public Mesh Into(Mesh mesh)
            {
                mesh.Clear();
                mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(bark, 0);
                mesh.SetTriangles(leaves, 1);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        struct Branch { public Vector3[] pts; public float baseRadius, length; }
        struct CardSpot { public Vector3 attach, up, normalHint, shading; public float size; }

        /// <summary>One tree: skeleton and card spots are generated once, then emitted at three levels of detail.</summary>
        static Mesh[] Generate(Species sp, int variant)
        {
            var rng = new System.Random(1000 * (int)sp.kind + variant * 17 + 3);
            float R() => (float)rng.NextDouble();
            float height = Mathf.Lerp(sp.heightMin, sp.heightMax, (variant + 0.5f) / Variants + (R() - 0.5f) * 0.2f);
            float radius = Mathf.Lerp(sp.radiusMin, sp.radiusMax, R());
            float yBase = height * sp.crownBase, yTop = height;
            float Envelope(float y)
            {
                float h = Mathf.Clamp01((y - yBase) / (yTop - yBase));
                return radius * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(h, sp.shapeBias)), sp.shapePower);
            }
            var crownCentre = new Vector3(0f, (yBase + yTop) * 0.5f, 0f);
            Vector3 Shading(Vector3 p)
            {
                var d = p - crownCentre;
                d = new Vector3(d.x / radius, d.y / ((yTop - yBase) * 0.5f), d.z / radius);
                return (d.normalized + Vector3.up * 0.35f).normalized;
            }

            // Trunk with a gentle lean and bend.
            float trunkR = height * 0.0125f;
            var lean = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.04f;
            var trunk = new List<Vector3>();
            var trunkRadii = new List<float>();
            const int segs = 10;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs, y = t * height * 0.93f;
                trunk.Add(new Vector3(lean.x * y + Mathf.Sin(t * 5f + variant) * 0.12f, y, lean.z * y + Mathf.Cos(t * 4f + variant) * 0.12f));
                trunkRadii.Add(Mathf.Lerp(trunkR, 0.03f, t) * (t < 0.05f ? 1.3f : 1f));
            }
            Vector3 TrunkAt(float y) => trunk[Mathf.Clamp(Mathf.RoundToInt(y / (height * 0.93f) * segs), 0, segs)];
            float TrunkRadiusAt(float y) => Mathf.Lerp(trunkR, 0.03f, y / height);

            // First-order branches, spiralling up (golden angle), steeper towards the top.
            var branches = new List<Branch>();
            for (int i = 0; i < sp.branches; i++)
            {
                float y = Mathf.Lerp(yBase, yTop * 0.9f, (i + R() * 0.8f) / sp.branches);
                float h = (y - yBase) / (yTop - yBase);
                float az = (i * 137.5f + R() * 40f) * Mathf.Deg2Rad;
                float angle = sp.branchAngle * (1f - 0.4f * h) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle) * Mathf.Cos(az), Mathf.Cos(angle), Mathf.Sin(angle) * Mathf.Sin(az));
                float len = Envelope(y + 1f) / Mathf.Max(0.35f, Mathf.Sin(angle)) * Mathf.Lerp(0.75f, 1.05f, R());
                if (sp.kind == Kind.Locust) len *= Mathf.Lerp(0.6f, 1.15f, R()); // lumpy, open crown
                var pts = new Vector3[4];
                pts[0] = TrunkAt(y);
                for (int k = 1; k < 4; k++)
                {
                    var bend = Vector3.up * (0.08f * k) + new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.15f;
                    pts[k] = pts[k - 1] + (dir + bend).normalized * len / 3f;
                }
                branches.Add(new Branch { pts = pts, baseRadius = TrunkRadiusAt(y) * 0.5f, length = len });
            }

            // Leaf cards: along the outer part of the branches, then filling the crown shell.
            var spots = new List<CardSpot>();
            void Spot(Vector3 p, Vector3 outward)
            {
                var up = (outward * 0.8f + Vector3.up * 0.5f + new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 0.9f).normalized;
                var hint = (outward + new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f) * 1.2f).normalized;
                if (Mathf.Abs(Vector3.Dot(up, hint)) > 0.95f) hint = Vector3.Cross(up, Vector3.right).normalized;
                spots.Add(new CardSpot { attach = p, up = up, normalHint = hint, shading = Shading(p), size = sp.cardSize * Mathf.Lerp(0.8f, 1.2f, R()) });
            }
            int onBranches = sp.cards * 2 / 5;
            for (int c = 0; c < onBranches; c++)
            {
                var b = branches[c % branches.Count];
                float t = Mathf.Lerp(0.35f, 1f, R());
                int seg = Mathf.Min(2, (int)(t * 3f));
                var p = Vector3.Lerp(b.pts[seg], b.pts[seg + 1], t * 3f - seg);
                var outward = new Vector3(p.x, 0f, p.z).normalized;
                Spot(p, (outward + Vector3.up * 0.2f).normalized);
            }
            while (spots.Count < sp.cards)
            {
                float y = Mathf.Lerp(yBase, yTop, Mathf.Pow(R(), 0.8f));
                float az = R() * Mathf.PI * 2f;
                float r = Envelope(y) * Mathf.Lerp(0.5f, 0.95f, Mathf.Sqrt(R()));
                var outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                var p = TrunkAt(Mathf.Min(y, height * 0.93f)) + outward * r;
                p.y = y;
                float h = (y - yBase) / (yTop - yBase);
                Spot(p, (outward + Vector3.up * Mathf.Lerp(-0.2f, 1.2f, h)).normalized);
            }

            // LOD 0: full; LOD 1: a third of the cards, larger, main branches; LOD 2: a twelfth, larger still, trunk only.
            var lods = new Mesh[3];
            int tile = (int)sp.kind;
            for (int lod = 0; lod < 3; lod++)
            {
                var b = new Builder();
                b.Tube(trunk, trunkRadii, lod == 0 ? 8 : lod == 1 ? 5 : 3);
                if (lod < 2)
                    for (int i = 0; i < branches.Count; i += lod == 0 ? 1 : 2)
                    {
                        var br = branches[i];
                        b.Tube(br.pts, new[] { br.baseRadius, br.baseRadius * 0.6f, br.baseRadius * 0.35f, 0.015f }, lod == 0 ? 5 : 3);
                    }
                int step = lod == 0 ? 1 : lod == 1 ? 3 : 12;
                float grow = lod == 0 ? 1f : lod == 1 ? 1.75f : 3.3f; // keeps the covered area about the same
                for (int i = 0; i < spots.Count; i += step)
                {
                    var s = spots[i];
                    b.Card(s.attach, s.up, s.normalHint, s.shading, s.size * grow, tile);
                }
                lods[lod] = b.Into(MeshAsset($"{sp.kind}_{variant}_LOD{lod}"));
            }
            return lods;
        }

        // ------------------------------------------------------------------ assets

        static Mesh MeshAsset(string name)
        {
            string path = Dir + "Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            System.IO.Directory.CreateDirectory(Dir + "Meshes");
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// Shell-damaged trees ("snags"): the trunk snapped off at 35-75 % of its height with a splinter on top, a few
        /// bare, broken branches, charred bark. Generated once per session like the living trees.
        /// </summary>
        public static GameObject[] Snags()
        {
            if (_snags != null && _snags[0] != null) return _snags;
            var bark = CharredBarkMaterial();
            var leaves = LeafMaterial();
            _snags = new GameObject[Variants];
            for (int v = 0; v < Variants; v++)
            {
                var sp = All[1 + v % 3]; // locust, elm, oak
                var rng = new System.Random(500 + v * 31);
                float R() => (float)rng.NextDouble();
                float height = Mathf.Lerp(sp.heightMin, sp.heightMax, R());
                float cut = height * Mathf.Lerp(0.35f, 0.75f, R());
                float trunkR = height * 0.0125f;
                var lean = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.06f;
                var trunk = new List<Vector3>();
                var radii = new List<float>();
                for (int i = 0; i <= 6; i++)
                {
                    float t = i / 6f, y = t * cut;
                    trunk.Add(new Vector3(lean.x * y, y, lean.z * y));
                    radii.Add(Mathf.Lerp(trunkR, trunkR * 0.6f, t) * (i == 0 ? 1.3f : 1f));
                }
                // Splinter where it snapped.
                trunk.Add(trunk[6] + new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.3f + Vector3.up * Mathf.Lerp(0.5f, 1.2f, R()));
                radii.Add(trunkR * 0.12f);
                var branches = new List<Vector3[]>();
                var branchR = new List<float>();
                int nb = 3 + rng.Next(4);
                for (int i = 0; i < nb; i++)
                {
                    float y = Mathf.Lerp(cut * 0.4f, cut * 0.95f, R());
                    float az = R() * Mathf.PI * 2f, ang = Mathf.Lerp(40f, 80f, R()) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(ang) * Mathf.Cos(az), Mathf.Cos(ang), Mathf.Sin(ang) * Mathf.Sin(az));
                    float len = Mathf.Lerp(0.8f, 3f, R());
                    var p0 = new Vector3(lean.x * y, y, lean.z * y);
                    branches.Add(new[] { p0, p0 + dir * len * 0.5f, p0 + (dir + Vector3.down * 0.1f).normalized * len });
                    branchR.Add(trunkR * Mathf.Lerp(0.25f, 0.45f, R()));
                }
                var lods = new Mesh[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var b = new Builder();
                    b.Tube(trunk, radii, lod == 0 ? 7 : lod == 1 ? 5 : 3);
                    if (lod < 2)
                        for (int i = 0; i < branches.Count; i += lod == 0 ? 1 : 2)
                            b.Tube(branches[i], new[] { branchR[i], branchR[i] * 0.6f, branchR[i] * 0.3f }, lod == 0 ? 4 : 3);
                    lods[lod] = b.Into(MeshAsset($"Snag_{v}_LOD{lod}"));
                }
                _snags[v] = SaveLodPrefab($"Snag_{v}", lods, bark, leaves, trunkR * 1.6f);
            }
            AssetDatabase.SaveAssets();
            return _snags;
        }

        static Material CharredBarkMaterial()
        {
            var mat = Lit(Dir + "BarkCharred.mat");
            mat.SetTexture("_BaseMap", Texture(Dir + "Bark.png", false));
            mat.SetTexture("_BumpMap", Texture(Dir + "Bark_nor.png", true));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetColor("_BaseColor", new Color(0.32f, 0.3f, 0.28f));
            mat.SetFloat("_Smoothness", 0.05f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject BuildPrefab(Species sp, int variant, Material bark, Material leaves)
        {
            var lods = Generate(sp, variant);
            var b0 = lods[0].bounds;
            return SaveLodPrefab($"{sp.kind}_{variant}", lods, bark, leaves, Mathf.Min(b0.extents.x, b0.extents.z) * 0.6f);
        }

        /// <summary>Prefab with a cross-fading LOD group over three meshes and a capsule collider on the root.</summary>
        static GameObject SaveLodPrefab(string name, Mesh[] lods, Material bark, Material leaves, float colliderRadius)
        {
            foreach (var m in lods) EditorUtility.SetDirty(m);
            var root = new GameObject(name);
            var renderers = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("LOD" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = lods[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { bark, leaves };
                mr.shadowCastingMode = ShadowCastingMode.Off;
                renderers[i] = mr;
            }
            // Shadow proxies: each LOD casts its shadow from the next, coarser mesh (LOD 0 from LOD 1's, LOD 1 from
            // LOD 2's); the last LOD casts none. Tree shadows were 1.24 M of 2.3 M shadow triangles on the heaviest view.
            var proxies = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("ShadowLOD" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = lods[i + 1];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { bark, leaves };
                mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                proxies[i] = mr;
            }
            var group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            // Screen-relative heights: the recon camera's zoom keeps full detail on what it looks at.
            group.SetLODs(new[] { new LOD(0.25f, new[] { renderers[0], proxies[0] }), new LOD(0.08f, new[] { renderers[1], proxies[1] }), new LOD(0.004f, new[] { renderers[2] }) });
            group.RecalculateBounds();

            // Drone-stopping crown or trunk (terrain tree colliders must be one capsule on the root).
            var b = lods[0].bounds;
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = colliderRadius;
            col.height = b.size.y;
            col.center = new Vector3(0f, b.center.y, 0f);

            root.layer = ModernDrone.Core.GameLayers.Environment;
            foreach (Transform t in root.transform) t.gameObject.layer = root.layer;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Dir + root.name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Material LeafMaterial()
        {
            var tex = Texture(Dir + "LeafAtlas.png", false);
            var mat = Lit(Dir + "Leaves.mat");
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material BarkMaterial()
        {
            var mat = Lit(Dir + "Bark.mat");
            mat.SetTexture("_BaseMap", Texture(Dir + "Bark.png", false));
            mat.SetTexture("_BumpMap", Texture(Dir + "Bark_nor.png", true));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", 0.05f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Lit(string path)
        {
            var shader = Shader.Find("ModernDrone/Vegetation Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                if (mat.shader != shader) mat.shader = shader; // same properties as URP Lit
                return mat;
            }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Texture2D Texture(string path, bool normal)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool alpha = !normal && path.EndsWith("LeafAtlas.png");
            bool dirty = false;
            if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (alpha && !imp.mipMapsPreserveCoverage)
            {
                // Keep the alpha-clipped coverage in the mips, or distant crowns thin out to nothing.
                imp.alphaIsTransparency = true;
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.5f;
                imp.wrapMode = TextureWrapMode.Clamp;
                dirty = true;
            }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ placement

        /// <summary>
        /// Windbreak belts along the tree rows: four staggered rows about 4 m apart (belt about 12-16 m wide), a tree
        /// every 3-5 m, a missing tree now and then and a field-crossing gap every few hundred metres. Each belt has
        /// its own species mix. Saplings between the rows and a shrub edge on both sides close the belt below the
        /// crowns, as in a real windbreak you cannot see through. Appended to the terrain's tree prototypes; the
        /// bush prototypes already there are reused for the shrub edge.
        /// </summary>
        public static void PlantBelts(Terrain terrain, float[] rows, float start, float end, int seed)
        {
            var data = terrain.terrainData;
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var protos = new List<TreePrototype>(data.treePrototypes);
            var shrubs = new List<int>();
            for (int i = 0; i < protos.Count; i++)
                if (protos[i].prefab != null && protos[i].prefab.name.StartsWith("shrub")) shrubs.Add(i);
            var index = new Dictionary<Kind, int>();
            foreach (var sp in All)
            {
                index[sp.kind] = protos.Count;
                foreach (var p in Get(sp.kind)) protos.Add(new TreePrototype { prefab = p });
            }
            data.treePrototypes = protos.ToArray();

            Kind[][] mixes =
            {
                new[] { Kind.Locust, Kind.Locust, Kind.Elm, Kind.Oak },
                new[] { Kind.Poplar, Kind.Poplar, Kind.Elm, Kind.Locust },
                new[] { Kind.Oak, Kind.Oak, Kind.Locust, Kind.Elm },
                new[] { Kind.Elm, Kind.Elm, Kind.Poplar, Kind.Locust },
            };
            var trees = new List<TreeInstance>(data.treeInstances);
            float size = data.size.x;
            void Add(float x, float z, int proto, float scale)
            {
                var p = new Vector3(x / size, 0f, z / size);
                p.y = data.GetInterpolatedHeight(p.x, p.z) / data.size.y;
                trees.Add(new TreeInstance
                {
                    position = p, prototypeIndex = proto, widthScale = scale * (0.9f + 0.2f * R()), heightScale = scale,
                    rotation = R() * Mathf.PI * 2f, color = Color.white, lightmapColor = Color.white
                });
            }
            for (int r = 0; r < rows.Length; r++)
            {
                var mix = mixes[r % mixes.Length];
                var gaps = new List<float>();
                for (float g = start + 150f + R() * 300f; g < end; g += 250f + R() * 300f) gaps.Add(g);
                bool InGap(float x)
                {
                    foreach (float g in gaps) if (Mathf.Abs(x - g) < 15f) return true;
                    return false;
                }
                for (int line = 0; line < 4; line++)
                {
                    float offset = (line - 1.5f) * 4f;
                    for (float x = start + line * 1.2f; x < end; x += 3f + R() * 2f)
                    {
                        if (R() < 0.06f || InGap(x)) continue;
                        float z = rows[r] + offset + (R() - 0.5f) * 1.6f;
                        var kind = mix[rng.Next(mix.Length)];
                        Add(x, z, index[kind] + rng.Next(Variants), 0.85f + 0.3f * R());
                        // Sapling between this row and the next.
                        if (R() < 0.85f)
                            Add(x + (R() - 0.5f) * 2f, z + 2f + (R() - 0.5f), index[R() < 0.5f ? Kind.Elm : Kind.Locust] + rng.Next(Variants), 0.22f + 0.28f * R());
                    }
                }
                // Shrub edge on both sides.
                if (shrubs.Count > 0)
                    foreach (float side in new[] { -1f, 1f })
                        for (float x = start; x < end; x += 1.5f + R() * 2f)
                            if (!InGap(x)) Add(x, rows[r] + side * (7.5f + R() * 1.5f), shrubs[rng.Next(shrubs.Count)], 1.8f + 1.4f * R());
            }
            data.SetTreeInstances(trees.ToArray(), true);
            terrain.treeDistance = 2500f;
        }
    }
}
