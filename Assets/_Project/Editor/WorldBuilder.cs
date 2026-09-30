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
    /// ground textures, a partly cloudy HDRI sky, bushes and a grass sprite rendered from a grass model in Blender.
    /// Steppe with windbreak tree lines: grass fields with dry patches, bare mud, forest floor under the tree lines,
    /// rock on steep slopes.
    /// </summary>
    public static class WorldBuilder
    {
        const string Dir = "Assets/_Project/Art/Environment/";
        const string HdrPath = Dir + "Sky/kloofendal_48d_partly_cloudy_puresky_2k.hdr";

        // Sun position measured on the HDRI (brightest texel, u 0.595 / v 0.766 in the equirectangular map):
        // 47.9 deg elevation; the Unity panoramic skybox maps it to this light direction.
        static readonly Quaternion SunRotation = Quaternion.Euler(47.9f, -55.8f, 0f);
        // Sky colour at the horizon, averaged over the HDRI's horizon rows: linear (0.43, 0.46, 0.56), i.e. this in sRGB,
        // so far terrain fades into the sky behind it.
        static readonly Color Horizon = new Color(0.69f, 0.71f, 0.78f);

        // Haze as Beer-Lambert extinction from a meteorological visibility V (Koschmieder: sigma = 3.912 / V, contrast
        // threshold 2 %). 8 km is a hazy summer day over dusty steppe - a scene choice, not a measurement:
        // 84 % contrast left at 350 m (recon drone range), 61 % at 1 km, 38 % at 2 km.
        const float VisibilityMetres = 8000f;

        public const int Grass = 0, DryGrass = 1, Mud = 2, ForestFloor = 3, Rock = 4, GrassWide = 5;
        const int LayerCount = 6;

        /// <summary>Tree line rows (z, m) shared by the painter, the vegetation and the tree lines themselves.</summary>
        public static readonly float[] TreeRows = { 350f, 700f, 1300f, 1650f };

        // ------------------------------------------------------------------ ground

        public static TerrainLayer[] Layers() => new[]
        {
            Layer("Grass", "aerial_grass_rock", 27f, new Color(0.9f, 1f, 0.84f)),
            // Same scan, sun-bleached: dry-grass patches of a steppe field.
            Layer("DryGrass", "aerial_grass_rock", 23f, new Color(1.08f, 1.02f, 0.84f)),
            Layer("Mud", "brown_mud_02", 5f, Color.white),
            Layer("ForestFloor", "forrest_ground_01", 5f, Color.white),
            Layer("Rock", "rocky_trail", 6f, Color.white),
            // The grass scan again at a different scale and a greener tone, blended in by noise to break the tiling.
            Layer("GrassWide", "aerial_grass_rock", 71f, new Color(0.8f, 0.97f, 0.76f))
        };

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
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
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

        /// <summary>Splat map: grass base, dry patches, mud spots, forest floor under the tree lines, rock on slopes.</summary>
        public static void Paint(TerrainData data, int seed)
        {
            int res = data.alphamapResolution;
            var maps = new float[res, res, LayerCount];
            var rng = new System.Random(seed);
            float ox = rng.Next(0, 10000), oz = rng.Next(0, 10000);
            float size = data.size.x;
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
                float[] w = { green * (1f - wide), dry, mud, forest * 2f, rock * 2f, green * wide };
                float sum = 0f;
                foreach (float f in w) sum += f;
                for (int l = 0; l < LayerCount; l++) maps[z, x, l] = w[l] / sum;
            }
            data.SetAlphamaps(0, 0, maps);
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
                int n = 3 + rng.Next(8);
                for (int i = 0; i < n; i++)
                    Add(cx + (float)(rng.NextDouble() - 0.5) * 12f, cz + (float)(rng.NextDouble() - 0.5) * 12f, 0.9f + (float)rng.NextDouble());
            }
            data.SetTreeInstances(trees.ToArray(), true);
            terrain.treeDistance = 450f;
            terrain.treeBillboardDistance = 450f;
        }

        /// <summary>Removes bushes and grass around a point (a vehicle or position placed on the terrain).</summary>
        public static void ClearAround(Terrain terrain, Vector3 world, float radius) => ClearVegetation(terrain, new[] { world, world }, radius);

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
        static GameObject ShrubPrefab(string node, bool collider)
        {
            string path = Dir + $"Plants/{node}{(collider ? "" : "_canopy")}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Plants/shrubs.glb"));
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
            string meshPath = Dir + $"Plants/{node}.mesh";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh != null) { EditorUtility.CopySerialized(mesh, existingMesh); mesh = existingMesh; }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            var materials = part.GetComponent<MeshRenderer>().sharedMaterials;
            Object.DestroyImmediate(model);
            var root = new GameObject(node);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = materials;
            var b = mesh.bounds;
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

        /// <summary>HDRI sky, a sun matching it, sky ambient and reflections, horizon-coloured haze and a post-processing volume.</summary>
        public static void CreateLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.transform.rotation = SunRotation;
            RenderSettings.sun = sun;

            RenderSettings.skybox = SkyMaterial();
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Horizon;
            RenderSettings.fogDensity = 3.912f / VisibilityMetres;
            DynamicGI.UpdateEnvironment();

            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = PostProfile();
        }

        static Material SkyMaterial()
        {
            const string path = Dir + "Sky/Sky.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(HdrPath);
            if (importer != null && importer.wrapModeU != TextureWrapMode.Repeat)
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false; // mips make a seam where the panorama wraps
                importer.SaveAndReimport();
            }
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HdrPath));
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
            balance.temperature.Override(-6f);
            balance.tint.Override(2f);
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
