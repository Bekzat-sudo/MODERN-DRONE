using System.IO;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Builds the effect assets: imports the six-way flipbooks baked by Tools/Blender/vfx_bake.py (linear, they hold
    /// lighting, not colour), draws the small particle textures (soft dot, spark, clods, leaves), creates the materials
    /// in Resources/VFX that <see cref="ModernDrone.Vehicles.FxAssets"/> loads, and sets the import settings of the field
    /// recordings in Resources/Audio (mono, Vorbis; long loops stay compressed in memory).
    /// </summary>
    public static class VfxAssets
    {
        const string FlipbookDir = "Assets/_Project/Art/VFX/Flipbooks";
        const string TextureDir = "Assets/_Project/Art/VFX/Textures";
        const string MaterialDir = "Assets/_Project/Resources/VFX";
        const string AudioDir = "Assets/_Project/Resources/Audio";

        [MenuItem("Modern Drone/Setup/Build VFX Assets")]
        public static void Build()
        {
            Directory.CreateDirectory(TextureDir);
            Directory.CreateDirectory(MaterialDir);
            var soft = Texture("soft", 64, 1, 1, (x, y, cell, rng) => Dot(x, y, 2f));
            var spark = Texture("spark", 32, 1, 1, (x, y, cell, rng) => Dot(x, y, 5f));
            var clods = Texture("clods", 64, 2, 2, Clod);
            var leaves = Texture("leaves", 64, 2, 2, Leaf);
            AssetDatabase.Refresh();

            foreach (var name in new[] { "explosion", "smoke", "flame", "torch" })
                foreach (var map in new[] { "A", "B" })
                    Flipbook($"{FlipbookDir}/{name}_{map}.png");

            SixWay("VFX_Fireball", "explosion", emission: 3.2f, warmth: 0.4f, softFade: 5f);
            SixWay("VFX_Smoke", "smoke", emission: 0f, warmth: 0f);
            SixWay("VFX_SmokeHot", "smoke", emission: 0f, warmth: 0.25f);
            SixWay("VFX_Dust", "smoke", emission: 0f, warmth: 0f, softFade: 2.5f);
            SixWay("VFX_Flame", "flame", emission: 4.5f, warmth: 1f, softFade: 0.6f);
            SixWay("VFX_Torch", "torch", emission: 4.5f, warmth: 1f, softFade: 0.6f);
            Particle("VFX_Flash", soft, true, new Color(14f, 11f, 8f));
            Particle("VFX_Sparks", spark, true, new Color(7f, 4.2f, 2f));
            Particle("VFX_Embers", soft, true, new Color(5f, 2.8f, 1.2f));
            Particle("VFX_Clods", clods, false, Color.white);
            Particle("VFX_Leaves", leaves, false, Color.white);
            // Heat shimmer bends the view by a few pixels, not tens: a 1.2 % screen shift dragged sky and trees in a band
            // round the haze sprite that showed through a thin fireball as a rainbow arc.
            Distortion("VFX_ShockRing", ring: true, strength: 0.016f);
            Distortion("VFX_Haze", ring: false, strength: 0.003f);
            Lit("VFX_Debris", new Color(0.09f, 0.085f, 0.08f), 0.3f);
            Lit("VFX_HatchGlow", new Color(0.02f, 0.018f, 0.016f), 0f);
            Audio();
            AssetDatabase.SaveAssets();
            Debug.Log("[ModernDrone] VFX assets built.");
        }

        // ---------------------------------------------------------------- textures

        delegate float Pixel(float x, float y, int cell, System.Random rng);

        /// <summary>A white texture whose alpha is drawn by <paramref name="pixel"/>, per cell of a tiles x tiles atlas.</summary>
        static Texture2D Texture(string name, int cellSize, int tilesX, int tilesY, Pixel pixel)
        {
            string path = $"{TextureDir}/{name}.png";
            int w = cellSize * tilesX, h = cellSize * tilesY;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var rng = new System.Random(name.GetHashCode());
            var px = new Color[w * h];
            for (int cy = 0; cy < tilesY; cy++)
            for (int cx = 0; cx < tilesX; cx++)
            {
                int cell = cy * tilesX + cx;
                var cellRng = new System.Random(cell * 7919 + name.Length);
                for (int y = 0; y < cellSize; y++)
                for (int x = 0; x < cellSize; x++)
                {
                    float u = (x + 0.5f) / cellSize * 2f - 1f, v = (y + 0.5f) / cellSize * 2f - 1f;
                    float a = Mathf.Clamp01(pixel(u, v, cell, cellRng));
                    px[(cy * cellSize + y) * w + cx * cellSize + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Dot(float x, float y, float sharpness) => Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), sharpness);

        /// <summary>Lumpy earth clod: a disc with a noisy edge.</summary>
        static float Clod(float x, float y, int cell, System.Random rng)
        {
            float ang = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float edge = 0.62f + 0.18f * Mathf.Sin(ang * (3 + cell) + cell) + 0.1f * Mathf.Sin(ang * (7 + cell * 2) - cell * 1.7f);
            return Mathf.Clamp01((edge - r) * 12f);
        }

        /// <summary>Leaf: a pointed ellipse along the diagonal with a midrib.</summary>
        static float Leaf(float x, float y, int cell, System.Random rng)
        {
            float a = 0.6f + 0.25f * cell;
            float u = (x * Mathf.Cos(a) + y * Mathf.Sin(a)), v = (-x * Mathf.Sin(a) + y * Mathf.Cos(a));
            float width = 0.42f * (1f - u * u) * (cell % 2 == 0 ? 1f : 0.75f);
            float inside = Mathf.Clamp01((width - Mathf.Abs(v)) * 20f) * Mathf.Clamp01((0.95f - Mathf.Abs(u)) * 20f);
            float rib = Mathf.Abs(v) < 0.025f ? 0.75f : 1f;
            return inside * rib;
        }

        static void Flipbook(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Debug.LogWarning($"[ModernDrone] Missing flipbook {path} (bake it with Tools/Blender/vfx_bake.py)."); return; }
            imp.sRGBTexture = false; // light per direction and opacity, not colour
            imp.alphaIsTransparency = false;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ; // BC7
            imp.SaveAndReimport();
        }

        // ---------------------------------------------------------------- materials

        static Material Save(string name, Shader shader)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void SixWay(string name, string book, float emission, float warmth, float softFade = 1.5f)
        {
            var mat = Save(name, Shader.Find("ModernDrone/Particles/SixWay"));
            mat.SetTexture("_MapA", AssetDatabase.LoadAssetAtPath<Texture2D>($"{FlipbookDir}/{book}_A.png"));
            mat.SetTexture("_MapB", AssetDatabase.LoadAssetAtPath<Texture2D>($"{FlipbookDir}/{book}_B.png"));
            mat.SetFloat("_EmissionGain", emission);
            mat.SetFloat("_ThermalWarmth", warmth);
            mat.SetFloat("_SoftFade", softFade);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static void Particle(string name, Texture2D tex, bool additive, Color tint)
        {
            var mat = Save(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // Soft particles: fade where the sprite meets the ground or a hull instead of a hard cut.
            mat.SetFloat("_SoftParticlesEnabled", 1f);
            mat.EnableKeyword("_SOFTPARTICLES_ON");
            mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 0.5f, 0f, 0f));
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static void Distortion(string name, bool ring, float strength)
        {
            var mat = Save(name, Shader.Find("ModernDrone/Particles/Distortion"));
            mat.SetFloat("_Mode", ring ? 0f : 1f);
            mat.DisableKeyword(ring ? "_MODE_HAZE" : "_MODE_RING");
            mat.EnableKeyword(ring ? "_MODE_RING" : "_MODE_HAZE");
            mat.SetFloat("_Strength", strength);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 50;
        }

        static void Lit(string name, Color color, float smoothness)
        {
            var mat = Save(name, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.enableInstancing = true;
        }

        // ---------------------------------------------------------------- audio

        static void Audio()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (AudioImporter)AssetImporter.GetAtPath(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                imp.forceToMono = true;
                imp.loadInBackground = false;
                var s = imp.defaultSampleSettings;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                s.preloadAudioData = true;
                // Short one-shots decoded up front (no decode spikes when a barrage lands); long loops stay compressed.
                s.loadType = clip != null && clip.length > 6f ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
        }
    }
}
