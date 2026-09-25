using System.IO;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Procedural, tileable ground textures (grass, dry grass, dirt) and the terrain layers using them,
    /// plus a splat painter that mixes them by noise patches and slope.
    /// </summary>
    public static class TerrainTextures
    {
        const string Dir = "Assets/_Project/Art/Textures";
        const int Size = 512;

        struct Palette
        {
            public Color a, b, c;
            public float grain, blotch;
        }

        public static TerrainLayer[] LoadOrCreateLayers()
        {
            var grass = Layer("Ground_Grass", new Palette
            {
                a = new Color(0.30f, 0.36f, 0.17f), b = new Color(0.40f, 0.45f, 0.22f), c = new Color(0.22f, 0.28f, 0.12f),
                grain = 0.55f, blotch = 0.45f
            }, 12f, 11);
            var dry = Layer("Ground_DryGrass", new Palette
            {
                a = new Color(0.47f, 0.44f, 0.26f), b = new Color(0.56f, 0.52f, 0.32f), c = new Color(0.36f, 0.35f, 0.20f),
                grain = 0.6f, blotch = 0.4f
            }, 10f, 23);
            var dirt = Layer("Ground_Dirt", new Palette
            {
                a = new Color(0.36f, 0.30f, 0.21f), b = new Color(0.45f, 0.38f, 0.27f), c = new Color(0.26f, 0.22f, 0.16f),
                grain = 0.7f, blotch = 0.3f
            }, 6f, 37);
            return new[] { grass, dry, dirt };
        }

        /// <summary>Grass base, dry-grass patches from low-frequency noise, dirt on slopes and in small bare spots.</summary>
        public static void Paint(TerrainData data, int seed)
        {
            int res = data.alphamapResolution;
            var maps = new float[res, res, 3];
            var rng = new System.Random(seed);
            float ox = rng.Next(0, 10000), oz = rng.Next(0, 10000);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float u = x / (float)(res - 1), v = z / (float)(res - 1);
                float slope = data.GetSteepness(u, v) / 90f;
                float patches = Mathf.PerlinNoise(ox + u * 9f, oz + v * 9f);
                float fine = Mathf.PerlinNoise(ox + 500f + u * 60f, oz + 500f + v * 60f);

                float dirt = Mathf.Clamp01((slope - 0.12f) * 6f) + Mathf.Clamp01((fine - 0.78f) * 5f);
                float dry = Mathf.Clamp01((patches - 0.5f) * 3f) * (1f - dirt);
                float grass = Mathf.Max(0f, 1f - dirt - dry);
                float sum = grass + dry + dirt;
                maps[z, x, 0] = grass / sum;
                maps[z, x, 1] = dry / sum;
                maps[z, x, 2] = dirt / sum;
            }
            data.SetAlphamaps(0, 0, maps);
        }

        static TerrainLayer Layer(string name, Palette pal, float tile, int seed)
        {
            string layerPath = $"{Dir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer != null) return layer;

            var tex = Texture(name, pal, seed);
            layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = 0f, metallic = 0f };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        static Texture2D Texture(string name, Palette pal, int seed)
        {
            string path = $"{Dir}/{name}.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var px = new Color[Size * Size];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float blotch = Fbm(u, v, 4, 4, seed);          // large patches
                    float grain = Fbm(u, v, 64, 3, seed + 101);     // blades / crumbs
                    float speck = Fbm(u, v, 128, 1, seed + 202);
                    Color col = Color.Lerp(pal.a, pal.b, Mathf.Clamp01(blotch * pal.blotch * 2f));
                    col = Color.Lerp(col, pal.c, Mathf.Clamp01((grain - 0.45f) * pal.grain * 2.5f));
                    col *= 0.9f + speck * 0.2f;
                    col.a = 0.03f; // URP terrain reads smoothness from diffuse alpha: keep it matte
                    px[y * Size + x] = col;
                }
                tex.SetPixels(px);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Tileable fractal value noise: lattice indices wrap with the period, so the texture repeats seamlessly.</summary>
        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int p = period << o;
                sum += amp * ValueNoise(u * p, v * p, p, seed + o * 17);
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 144269504;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)int.MaxValue;
            }
        }
    }
}
