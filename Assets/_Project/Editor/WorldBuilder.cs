using System.Collections.Generic;
using ModernDrone.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Ground, vegetation, sky and light for the generated scenes. Photoscanned CC0 assets from Poly Haven (see CREDITS.md):
    /// ground textures, partly cloudy and overcast HDRI skies, bushes and a grass sprite rendered from a grass model in Blender.
    /// Steppe with windbreak tree lines: crop fields between the belts (ploughed, stubble, winter wheat, burnt, fallow
    /// steppe), grass with dry patches, bare mud, forest floor under the tree lines, rock on steep slopes, shell craters.
    /// Field textures and the crater decal are procedural (Tools/ground_textures.py).
    /// </summary>
    public static class WorldBuilder
    {
        const string Dir = "Assets/_Project/Art/Environment/";
        const string HdrPath = Dir + "Sky/kloofendal_48d_partly_cloudy_puresky_2k.hdr";
        const string OvercastHdrPath = Dir + "Sky/kloofendal_overcast_puresky_2k.hdr";

        // Sun position measured on the HDRI (brightest texel, u 0.595 / v 0.766 in the equirectangular map):
        // 47.9 deg elevation; the Unity panoramic skybox maps it to this light direction.
        static readonly Quaternion SunRotation = Quaternion.Euler(47.9f, -55.8f, 0f);
        // Sky colour at the horizon, averaged over the HDRI's horizon rows: linear (0.43, 0.46, 0.56), i.e. this in sRGB,
        // so far terrain fades into the sky behind it.
        static readonly Color Horizon = new Color(0.69f, 0.71f, 0.78f);
        // Same measurement on the overcast HDRI: linear (0.63, 0.66, 0.73).
        static readonly Color OvercastHorizon = new Color(0.82f, 0.84f, 0.87f);

        // Haze as Beer-Lambert extinction from a meteorological visibility V (Koschmieder: sigma = 3.912 / V, contrast
        // threshold 2 %). 8 km is a hazy summer day over dusty steppe - a scene choice, not a measurement:
        // 84 % contrast left at 350 m (recon drone range), 61 % at 1 km, 38 % at 2 km.
        const float VisibilityMetres = 8000f;
        // Humid air under a low cloud deck: 5 km (scene choice) - 76 % contrast at 350 m, 46 % at 1 km.
        const float OvercastVisibilityMetres = 5000f;

        // The first four layers (base terrain pass) are sampled without visible repetition (TerrainNoTile shader).
        public const int Grass = 0, DryGrass = 1, GrassWide = 2, Mud = 3, ForestFloor = 4, Rock = 5;
        public const int Plowed = 6, Stubble = 7, Wheat = 8, Burnt = 9;
        const int LayerCount = 10;

        /// <summary>Tree line rows (z, m) shared by the painter, the vegetation and the tree lines themselves.</summary>
        public static readonly float[] TreeRows = { 350f, 700f, 1300f, 1650f };

        // ------------------------------------------------------------------ ground

        public static TerrainLayer[] Layers() => new[]
        {
            Layer("Grass", "aerial_grass_rock", 27f, new Color(0.9f, 1f, 0.84f)),
            // Same scan, sun-bleached: dry-grass patches of a steppe field.
            Layer("DryGrass", "aerial_grass_rock", 23f, new Color(1.08f, 1.02f, 0.84f)),
            // The grass scan again at a different scale and a greener tone, blended in by noise to break the tiling.
            Layer("GrassWide", "aerial_grass_rock", 71f, new Color(0.8f, 0.97f, 0.76f)),
            Layer("Mud", "brown_mud_02", 5f, Color.white),
            Layer("ForestFloor", "forrest_ground_01", 5f, Color.white),
            Layer("Rock", "rocky_trail", 6f, Color.white),
            // Crop fields; rows run along X, the long side of the fields between the belts.
            Layer("Plowed", "field_plowed", 8f, Color.white),
            Layer("Stubble", "field_stubble", 6f, Color.white),
            Layer("Wheat", "field_wheat", 4f, Color.white),
            Layer("Burnt", "field_burnt", 10f, Color.white),
        };

        struct Field { public float x0, x1, z0, z1; public int crop; }

        /// <summary>
        /// Crop fields: the strips between the tree belts, each cut into one to three fields along X by grass field
        /// roads, with a grass headland along the belts.
        /// </summary>
        static List<Field> Fields(float size, System.Random rng)
        {
            var edges = new List<float> { 40f };
            edges.AddRange(TreeRows);
            edges.Add(size - 40f);
            int[] crops = { Plowed, Stubble, Wheat, Burnt, -1 };      // -1: fallow steppe
            float[] weights = { 0.28f, 0.25f, 0.25f, 0.08f, 0.14f };
            var fields = new List<Field>();
            for (int i = 0; i < edges.Count - 1; i++)
            {
                float z0 = edges[i] + 18f, z1 = edges[i + 1] - 18f;
                if (z1 - z0 < 60f) continue;
                var cuts = new List<float> { 40f, size - 40f };
                int n = rng.Next(3);
                for (int k = 0; k < n; k++) cuts.Add(300f + (float)rng.NextDouble() * (size - 600f));
                cuts.Sort();
                for (int k = 0; k < cuts.Count - 1; k++)
                {
                    var f = new Field { x0 = cuts[k] + 4f, x1 = cuts[k + 1] - 4f, z0 = z0, z1 = z1, crop = -1 };
                    double pick = rng.NextDouble();
                    for (int c = 0; c < crops.Length; c++) { if ((pick -= weights[c]) <= 0) { f.crop = crops[c]; break; } }
                    fields.Add(f);
                }
            }
            return fields;
        }

        static TerrainLayer Layer(string name, string scan, float tile, Color tint)
        {
            string path = Dir + $"Ground/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            bool created = layer == null;
            if (created) layer = new TerrainLayer();
            layer.diffuseTexture = Texture(Dir + $"Ground/{scan}/{scan}_diff_2k.jpg", false);
            layer.normalMapTexture = Texture(Dir + $"Ground/{scan}/{scan}_nor_gl_2k.jpg", true);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            // The grass scan is contrasty; squeezing its range hides the repeat from the air.
            float floor = scan == "aerial_grass_rock" ? 0.3f : 0f;
            layer.diffuseRemapMin = new Vector4(tint.r * floor, tint.g * floor, tint.b * floor, 0f);
            layer.diffuseRemapMax = new Vector4(tint.r * (1f - floor * 0.4f), tint.g * (1f - floor * 0.4f), tint.b * (1f - floor * 0.4f), 1f);
            if (created) AssetDatabase.CreateAsset(layer, path); else EditorUtility.SetDirty(layer);
            return layer;
        }

        static Texture2D Texture(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type || importer.anisoLevel != 8)
                {
                    importer.textureType = type;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.anisoLevel = 8;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Splat map: crop fields, grass base, dry patches, mud spots, forest floor under the tree lines, rock on slopes.</summary>
        public static void Paint(TerrainData data, int seed)
        {
            int res = data.alphamapResolution;
            var maps = new float[res, res, LayerCount];
            var rng = new System.Random(seed);
            float ox = rng.Next(0, 10000), oz = rng.Next(0, 10000);
            float size = data.size.x;
            var fields = Fields(size, rng);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1), v = z / (float)(res - 1);
                float slope = data.GetSteepness(u, v) / 90f;
                float patches = Mathf.PerlinNoise(ox + u * 7f, oz + v * 7f);
                float field = Mathf.PerlinNoise(ox + 300f + u * 25f, oz + 300f + v * 25f);
                float fine = Mathf.PerlinNoise(ox + 500f + u * 90f, oz + 500f + v * 90f);

                float wz = v * size;
                float nearRow = float.MaxValue;
                foreach (float row in TreeRows) nearRow = Mathf.Min(nearRow, Mathf.Abs(wz - row));
                float forest = Mathf.Clamp01((10f - nearRow) / 5f) * (0.7f + 0.3f * fine);

                float rock = Mathf.Clamp01((slope - 0.18f) * 5f);
                float mud = Mathf.Clamp01((fine - 0.72f) * 4f) * (1f - forest) + Mathf.Clamp01((slope - 0.1f) * 3f) * 0.5f;
                float dry = Mathf.Clamp01((patches - 0.55f) * 2.5f + (field - 0.5f) * 0.6f);
                float wide = Mathf.Clamp01(0.5f + (field - 0.5f) * 2.5f);
                float green = 1f - dry;
                float[] w = { green * (1f - wide), dry, green * wide, mud, forest * 2f, rock * 2f, 0f, 0f, 0f, 0f };

                // Crop field: soft, slightly ragged edges; a second layer in large patches breaks the tiling.
                float wx = u * size;
                foreach (var f in fields)
                {
                    if (f.crop < 0 || wx < f.x0 - 8f || wx > f.x1 + 8f || wz < f.z0 - 8f || wz > f.z1 + 8f) continue;
                    float edge = Mathf.Min(Mathf.Min(wx - f.x0, f.x1 - wx), Mathf.Min(wz - f.z0, f.z1 - wz)) + (fine - 0.5f) * 6f;
                    float c = Mathf.Clamp01(edge / 5f) * (1f - Mathf.Clamp01(forest)) * (1f - rock);
                    if (c <= 0f) break;
                    for (int l = 0; l < 6; l++) w[l] *= 1f - c;
                    int second = f.crop == Plowed ? Mud : f.crop == Stubble ? DryGrass : f.crop == Wheat ? Grass : Stubble;
                    float mix = Mathf.Clamp01((patches - 0.6f) * 2.5f) * (f.crop == Burnt ? 0.7f : 0.45f);
                    w[f.crop] += c * (1f - mix) * 3f;
                    w[second] += c * mix * 3f;
                    break;
                }
                float sum = 0f;
                foreach (float f in w) sum += f;
                for (int l = 0; l < LayerCount; l++) maps[z, x, l] = w[l] / sum;
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>
        /// Repaints a band around a polyline with grass (village yards, meadow) over whatever was there - fields
        /// included - and gives it grass billboards: blends in over the band's outer 30 %.
        /// </summary>
        public static void PaintMeadow(Terrain terrain, Vector3[] points, float halfWidth)
        {
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            var maps = data.GetAlphamaps(0, 0, res, res);
            float cell = data.size.x / (res - 1);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                var p = new Vector2(x * cell, z * cell);
                float d = float.MaxValue;
                for (int i = 1; i < points.Length; i++)
                    d = Mathf.Min(d, DistanceToSegment(p, new Vector2(points[i - 1].x, points[i - 1].z), new Vector2(points[i].x, points[i].z)));
                float k = Mathf.Clamp01((halfWidth - d) / (halfWidth * 0.3f));
                if (k <= 0f) continue;
                for (int l = 0; l < maps.GetLength(2); l++) maps[z, x, l] *= 1f - k;
                maps[z, x, (x + z) % 5 == 0 ? DryGrass : Grass] += k;
            }
            data.SetAlphamaps(0, 0, maps);

            if (data.detailPrototypes.Length == 0) return;
            int dres = data.detailResolution;
            var layer = data.GetDetailLayer(0, 0, dres, dres, 0);
            float dcell = data.size.x / dres;
            for (int z = 0; z < dres; z++)
            for (int x = 0; x < dres; x++)
            {
                var p = new Vector2(x * dcell, z * dcell);
                float d = float.MaxValue;
                for (int i = 1; i < points.Length; i++)
                    d = Mathf.Min(d, DistanceToSegment(p, new Vector2(points[i - 1].x, points[i - 1].z), new Vector2(points[i].x, points[i].z)));
                if (d < halfWidth * 0.85f) layer[z, x] = Mathf.Max(layer[z, x], 8);
            }
            data.SetDetailLayer(0, 0, 0, layer);
        }

        /// <summary>Paints a strip of mud (a dirt road) and clears grass and bushes on it.</summary>
        public static void PaintRoad(Terrain terrain, Vector3[] points, float width)
        {
            var data = terrain.terrainData;
            int res = data.alphamapResolution;
            var maps = data.GetAlphamaps(0, 0, res, res);
            float cell = data.size.x / (res - 1);
            for (int i = 1; i < points.Length; i++)
            {
                Vector2 a = new Vector2(points[i - 1].x, points[i - 1].z), b = new Vector2(points[i].x, points[i].z);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - width) / cell), 0, res - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + width) / cell), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - width) / cell), 0, res - 1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y, b.y) + width) / cell), 0, res - 1);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x * cell, z * cell);
                    float d = DistanceToSegment(p, a, b);
                    float k = Mathf.Clamp01((width * 0.5f + 1.5f - d) / 1.5f);
                    if (k <= 0f) continue;
                    for (int l = 0; l < maps.GetLength(2); l++) maps[z, x, l] *= 1f - k;
                    maps[z, x, Mud] += k;
                }
            }
            data.SetAlphamaps(0, 0, maps);
            ClearVegetation(terrain, points, width * 0.5f + 2f);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ vegetation

        static readonly string[] ShrubNodes = { "shrub_02_a", "shrub_02_b", "shrub_02_c", "shrub_02_d" };

        /// <summary>Grass billboards on the grass layers; bushes as understory of the tree lines and in small field clumps.</summary>
        public static void Vegetate(Terrain terrain, int seed)
        {
            var data = terrain.terrainData;
            var rng = new System.Random(seed);

            // Grass: dense on green grass, thinner on dry patches, none on mud, rock or under the trees.
            data.detailPrototypes = new[]
            {
                new DetailPrototype
                {
                    prototypeTexture = GrassSprite(), renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                    minWidth = 0.5f, maxWidth = 0.9f, minHeight = 0.35f, maxHeight = 0.75f, noiseSpread = 0.3f,
                    healthyColor = Color.white, dryColor = new Color(0.95f, 0.9f, 0.7f)
                }
            };
            data.SetDetailResolution(1024, 32);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode); // values are clumps per cell (~2 m)
            int dres = data.detailResolution;
            var splat = data.GetAlphamaps(0, 0, data.alphamapResolution, data.alphamapResolution);
            var density = new int[dres, dres];
            for (int z = 0; z < dres; z++)
            for (int x = 0; x < dres; x++)
            {
                int sx = x * (data.alphamapResolution - 1) / (dres - 1), sz = z * (data.alphamapResolution - 1) / (dres - 1);
                float g = splat[sz, sx, Grass] + splat[sz, sx, GrassWide] + splat[sz, sx, DryGrass] * 0.6f;
                density[z, x] = Mathf.RoundToInt(g * 10f * (0.6f + 0.4f * (float)rng.NextDouble()));
            }
            data.SetDetailLayer(0, 0, 0, density);
            terrain.detailObjectDistance = 150f;
            terrain.detailObjectDensity = 1f;

            // Bushes
            var protos = new List<TreePrototype>();
            foreach (var node in ShrubNodes) protos.Add(new TreePrototype { prefab = ShrubPrefab(node, true) });
            int bushes = ShrubNodes.Length;
            data.treePrototypes = protos.ToArray();
            var trees = new List<TreeInstance>();
            float size = data.size.x;
            void Add(float x, float z, float scale)
            {
                if (x < 5f || z < 5f || x > size - 5f || z > size - 5f) return;
                var p = new Vector3(x / size, 0f, z / size);
                p.y = data.GetInterpolatedHeight(p.x, p.z) / data.size.y;
                trees.Add(new TreeInstance
                {
                    position = p, prototypeIndex = rng.Next(bushes), widthScale = scale, heightScale = scale * (0.85f + 0.3f * (float)rng.NextDouble()),
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2), color = Color.white, lightmapColor = Color.white
                });
            }
            foreach (float row in TreeRows)
            {
                for (float x = 100f; x < size - 100f; x += 2.5f + (float)rng.NextDouble() * 4f)
                    Add(x, row + (float)(rng.NextDouble() - 0.5) * 14f, 1f + (float)rng.NextDouble() * 1.2f);
            }
            for (int c = 0; c < 60; c++)
            {
                float cx = (float)rng.NextDouble() * size, cz = (float)rng.NextDouble() * size;
                if (Vector2.Distance(new Vector2(cx, cz), new Vector2(size, size) * 0.5f) < 200f) continue; // keep the launch field open
                int ax = Mathf.Clamp((int)(cx / size * (data.alphamapResolution - 1)), 0, data.alphamapResolution - 1);
                int az = Mathf.Clamp((int)(cz / size * (data.alphamapResolution - 1)), 0, data.alphamapResolution - 1);
                if (splat[az, ax, Grass] + splat[az, ax, GrassWide] + splat[az, ax, DryGrass] < 0.5f) continue; // not in crop fields
                int n = 3 + rng.Next(8);
                for (int i = 0; i < n; i++)
                    Add(cx + (float)(rng.NextDouble() - 0.5) * 12f, cz + (float)(rng.NextDouble() - 0.5) * 12f, 0.9f + (float)rng.NextDouble());
            }
            data.SetTreeInstances(trees.ToArray(), true);
            terrain.treeDistance = 450f;
            terrain.treeBillboardDistance = 450f;
        }

        /// <summary>
        /// Shell craters as decals: clusters at the given hotspots (positions beside the belts) and at random spots
        /// beside the tree belts, single craters scattered over the fields, older ones fainter. Mortar (rim about
        /// 1.5-3 m) to artillery (4-7 m) sizes are illustrative. The launch field around the centre is left clean.
        /// </summary>
        public static void Scar(Terrain terrain, int seed, Vector2[] hotspots = null)
        {
            var data = terrain.terrainData;
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            float size = data.size.x;
            var centre = new Vector2(size, size) * 0.5f;
            var mat = CraterMaterial();
            var root = new GameObject("Craters").transform;
            void Crater(float x, float z, float diameter, float fade)
            {
                if (x < 20f || z < 20f || x > size - 20f || z > size - 20f || Vector2.Distance(new Vector2(x, z), centre) < 180f) return;
                var p = new Vector3(x, 0f, z);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                var go = new GameObject("Crater");
                go.transform.SetParent(root, false);
                // Shallow box (+-0.6 m) so trees, vehicles and props beside a crater are not smeared.
                go.transform.SetPositionAndRotation(p + Vector3.up * 0.6f, Quaternion.Euler(90f, R() * 360f, 0f));
                var dp = go.AddComponent<DecalProjector>();
                dp.material = mat;
                float s = diameter / 0.42f; // the texture's rim sits at 0.42 of its half-width
                dp.size = new Vector3(s, s, 1.2f);
                dp.pivot = new Vector3(0f, 0f, 0.6f);
                dp.fadeFactor = fade;
                dp.drawDistance = 900f;
            }
            float Diameter() => R() < 0.8f ? 1.5f + R() * 1.5f : 4f + R() * 3f;
            if (hotspots != null)
                foreach (var h in hotspots)
                {
                    int n = 6 + rng.Next(10);
                    for (int i = 0; i < n; i++)
                        Crater(h.x + (R() - 0.5f) * 70f, h.y + (R() - 0.5f) * 40f, Diameter(), 0.6f + 0.4f * R());
                }
            for (int c = 0; c < 12; c++)
            {
                float row = TreeRows[rng.Next(TreeRows.Length)];
                float cx = 150f + R() * (size - 300f), cz = row + (R() < 0.5f ? -1f : 1f) * (20f + R() * 50f);
                int n = 4 + rng.Next(11);
                for (int i = 0; i < n; i++)
                    Crater(cx + (R() - 0.5f) * 50f, cz + (R() - 0.5f) * 30f, Diameter(), 0.6f + 0.4f * R());
            }
            for (int i = 0; i < 120; i++)
                Crater(R() * size, R() * size, Diameter(), 0.35f + 0.55f * R());
        }

        static Material CraterMaterial()
        {
            const string path = "Assets/_Project/Art/Decals/Crater.mat";
            var tex = DecalTexture("Assets/_Project/Art/Decals/Crater.png", false);
            var nrm = DecalTexture("Assets/_Project/Art/Decals/Crater_nor.png", true);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("Base_Map", tex);
            mat.SetTexture("Normal_Map", nrm);
            mat.SetFloat("Normal_Blend", 1f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D DecalTexture(string path, bool normal)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp != null && (imp.wrapMode != TextureWrapMode.Clamp || (normal && imp.textureType != TextureImporterType.NormalMap) || (!normal && !imp.alphaIsTransparency)))
            {
                imp.wrapMode = TextureWrapMode.Clamp;
                if (normal) imp.textureType = TextureImporterType.NormalMap; else imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Terrain material on the TerrainNoTile shader (URP Terrain Lit with repetition-free base layers).</summary>
        public static Material TerrainMaterial()
        {
            const string path = Dir + "Ground/TerrainNoTile.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("ModernDrone/Terrain/Lit"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Removes bushes and grass around a point (a vehicle or position placed on the terrain).</summary>
        public static void ClearAround(Terrain terrain, Vector3 world, float radius) => ClearVegetation(terrain, new[] { world, world }, radius);

        /// <summary>Removes bushes, trees and grass along a polyline (under power lines).</summary>
        public static void ClearVegetationAlong(Terrain terrain, Vector3[] path, float radius) => ClearVegetation(terrain, path, radius);

        static void ClearVegetation(Terrain terrain, Vector3[] path, float radius)
        {
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            var keep = new List<TreeInstance>();
            foreach (var t in data.treeInstances)
            {
                var w = Vector3.Scale(t.position, data.size) + origin;
                float d = float.MaxValue;
                for (int i = 1; i < path.Length; i++)
                    d = Mathf.Min(d, DistanceToSegment(new Vector2(w.x, w.z), new Vector2(path[i - 1].x, path[i - 1].z), new Vector2(path[i].x, path[i].z)));
                if (d > radius) keep.Add(t);
            }
            data.SetTreeInstances(keep.ToArray(), true);

            if (data.detailPrototypes.Length == 0) return;
            int dres = data.detailResolution;
            var layer = data.GetDetailLayer(0, 0, dres, dres, 0);
            float cell = data.size.x / dres;
            for (int z = 0; z < dres; z++)
            for (int x = 0; x < dres; x++)
            {
                if (layer[z, x] == 0) continue;
                var p = new Vector2(origin.x + x * cell, origin.z + z * cell);
                for (int i = 1; i < path.Length; i++)
                    if (DistanceToSegment(p, new Vector2(path[i - 1].x, path[i - 1].z), new Vector2(path[i].x, path[i].z)) < radius) { layer[z, x] = 0; break; }
            }
            data.SetDetailLayer(0, 0, 0, layer);
        }

        static Texture2D GrassSprite()
        {
            const string path = Dir + "Plants/grass_billboard.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>One bush from the combined glTF as its own prefab (terrain trees need one prefab per prototype).</summary>
        static GameObject ShrubPrefab(string node, bool collider) =>
            NodePrefab(Dir + "Plants/shrubs.glb", node, Dir + "Plants/", collider, collider ? "" : "_canopy");

        /// <summary>
        /// One node of a model as its own prefab with the mesh baked into the root (terrain trees need one prefab per
        /// prototype with the renderer on the root), centred on the ground at the origin; optional capsule collider.
        /// </summary>
        internal static GameObject NodePrefab(string modelPath, string node, string outDir, bool collider, string suffix = "")
        {
            string path = outDir + $"{node}{suffix}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<LODGroup>() != null) return existing;
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            Transform part = null;
            foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == node) part = t;
            // Terrain trees draw only the prefab root's renderer: bake the node (with its import rotation) into a root mesh,
            // centred on the ground at the origin.
            var src = part.GetComponent<MeshFilter>().sharedMesh;
            var local = Matrix4x4.TRS(Vector3.zero, part.rotation, part.lossyScale);
            var mesh = new Mesh { name = node, indexFormat = src.indexFormat };
            mesh.CombineMeshes(new[] { new CombineInstance { mesh = src, transform = local } }, true, true);
            var mb = mesh.bounds;
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= new Vector3(mb.center.x, mb.min.y, mb.center.z);
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            string meshPath = outDir + $"{node}.mesh";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh != null) { EditorUtility.CopySerialized(mesh, existingMesh); mesh = existingMesh; }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            var materials = part.GetComponent<MeshRenderer>().sharedMaterials;
            Object.DestroyImmediate(model);
            var root = new GameObject(node);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            var b = mesh.bounds;
            // A single-LOD group: the plant fades out once it is a few pixels tall, and the terrain accepts a
            // tree prototype with a LOD group on any shader (without one it demands Nature/Soft Occlusion).
            // Low plants cast no shadow.
            if (b.size.y < 1.2f) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.SetLODs(new[] { new LOD(0.012f, new Renderer[] { renderer }) });
            group.RecalculateBounds();
            if (collider)
            {
                // Drone crashes into a bush; the jet and fuze ignore vegetation beyond that.
                var col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, b.size.y * 0.5f, 0f);
                col.radius = Mathf.Min(b.size.x, b.size.z) * 0.35f;
                col.height = b.size.y;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Environment;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ sky and light

        /// <summary>
        /// HDRI sky, a sun matching it, sky ambient and reflections, horizon-coloured haze and a post-processing volume.
        /// Overcast: the cloud deck scatters the direct beam, so the light is mostly sky ambient - the sun is kept
        /// weak and neutral (faint, soft shadows only), and the haze is thicker. Intensities are art direction.
        /// </summary>
        public static void CreateLighting(bool overcast = false)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = overcast ? 0.3f : 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = overcast ? 0.35f : 0.85f;
            sun.color = overcast ? new Color(0.94f, 0.96f, 1f) : new Color(1f, 0.95f, 0.86f);
            sun.transform.rotation = SunRotation;
            RenderSettings.sun = sun;

            RenderSettings.skybox = overcast ? SkyMaterial(OvercastHdrPath, "SkyOvercast") : SkyMaterial(HdrPath, "Sky");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = overcast ? OvercastHorizon : Horizon;
            RenderSettings.fogDensity = 3.912f / (overcast ? OvercastVisibilityMetres : VisibilityMetres);
            DynamicGI.UpdateEnvironment();

            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = PostProfile();
        }

        static Material SkyMaterial(string hdrPath, string name)
        {
            string path = Dir + "Sky/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(hdrPath);
            if (importer != null && importer.wrapModeU != TextureWrapMode.Repeat)
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false; // mips make a seam where the panorama wraps
                importer.SaveAndReimport();
            }
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(hdrPath));
            mat.SetFloat("_Exposure", 1f);
            mat.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Grade after war footage of the steppe: Neutral tonemapping (range only, no ACES hue shift or extra contrast)
        /// as the base, then desaturated olive and ochre, firmer contrast and a slightly cool balance; warm, saturated
        /// colour is left to fire. Values are art direction. Updated in place on every rebuild.
        /// </summary>
        static VolumeProfile PostProfile()
        {
            const string path = Dir + "PostProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (profile.TryGet<T>(out var c)) return c;
                c = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Get<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.25f);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.3f); // cameras with AutoExposure override this
            color.contrast.Override(20f);      // Neutral is flatter than ACES
            color.saturation.Override(-16f);
            var balance = Get<WhiteBalance>();
            balance.temperature.Override(-3f); // cooler turns chernozem mauve
            balance.tint.Override(0f);
            var lgg = Get<LiftGammaGain>();
            lgg.lift.Override(new Vector4(1f, 1f, 1.02f, 0f)); // cool shadows
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, -0.04f)); // slightly deeper midtones
            foreach (var c in profile.components) EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Shadows far enough for an FPV view from 30-100 m up (URP default is 50 m), with a 4096 map so 350 m over four
        /// cascades stays crisp; STP (Unity 6 temporal upscaler, anti-aliasing at render scale 1); GPU occlusion culling
        /// for the GPU Resident Drawer (behind tree lines and terrain).
        /// </summary>
        public static void ConfigurePipeline()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            if (urp.shadowDistance < 300f)
            {
                urp.shadowDistance = 350f;
                urp.shadowCascadeCount = 4;
            }
            urp.mainLightShadowmapResolution = 4096;
            urp.upscalingFilter = UpscalingFilterSelection.STP;
            urp.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
            EditorUtility.SetDirty(urp);
        }
    }
}
