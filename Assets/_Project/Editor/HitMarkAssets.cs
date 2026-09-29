using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Strike-mark decals: a procedural 2x2 atlas (jet hole, crater, blast scorch, fragment pitting), the decal material
    /// in Resources (loaded by HitMarks at runtime) and the URP Decal renderer feature on the PC renderer.
    /// Looks follow photos of real hits: a small dark entry hole ringed by liner metal splashed radially, soot
    /// "star", paint burnt off around it; fragments leave pits with bare, bright metal rims.
    /// </summary>
    public static class HitMarkAssets
    {
        const string TexturePath = "Assets/_Project/Art/Decals/HitMarks.png";
        const string MaterialPath = "Assets/_Project/Resources/Decals/HitMarks.mat";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const int Cell = 512;

        [MenuItem("Modern Drone/Setup/Build Hit Mark Decals")]
        public static void Build()
        {
            WriteAtlas();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Shader Graphs/Decal"));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.SetTexture("Base_Map", tex);
            mat.enableInstancing = true; // one material for every mark: instanced draws
            EditorUtility.SetDirty(mat);
            InstallFeature();
            AssetDatabase.SaveAssets();
        }

        static void WriteAtlas()
        {
            var px = new Color32[Cell * 2 * Cell * 2];
            var rng = new System.Random(7);
            Draw(px, 0, 1, rng, Hole);
            Draw(px, 1, 1, rng, Crater);
            Draw(px, 0, 0, rng, Scorch);
            Draw(px, 1, 0, rng, Pocks(rng));
            var tex = new Texture2D(Cell * 2, Cell * 2, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }

        delegate Color Shade(float x, float y, float r, float a, System.Random rng);

        /// <summary>Fills one cell; x, y in -1..1 from its centre, r radius, a angle.</summary>
        static void Draw(Color32[] px, int cx, int cy, System.Random rng, Shade shade)
        {
            int w = Cell * 2;
            for (int j = 0; j < Cell; j++)
            for (int i = 0; i < Cell; i++)
            {
                float x = (i + 0.5f) / Cell * 2f - 1f, y = (j + 0.5f) / Cell * 2f - 1f;
                var c = shade(x, y, Mathf.Sqrt(x * x + y * y), Mathf.Atan2(y, x), rng);
                px[(cy * Cell + j) * w + cx * Cell + i] = c;
            }
        }

        static float Noise(float x, float y, float f) => Mathf.PerlinNoise(x * f + 31.7f, y * f + 11.3f);

        /// <summary>Radial streaks (soot and splashed metal thrown out from the impact).</summary>
        static float Rays(float a, int count, float seed) =>
            Mathf.Pow(Mathf.Abs(Mathf.Sin(a * count * 0.5f + seed)) * 0.6f + 0.4f * Mathf.PerlinNoise(a * 3f + seed, seed), 3f);

        static readonly Color SootC = new Color(0.04f, 0.035f, 0.03f);
        /// <summary>Paint burnt to ash: pale grey halo around a hit, the part that reads on dark camouflage.</summary>
        static readonly Color Ash = new Color(0.5f, 0.48f, 0.44f);
        static readonly Color Metal = new Color(0.55f, 0.53f, 0.5f);
        static readonly Color HotMetal = new Color(0.42f, 0.33f, 0.26f);

        static Color Hole(float x, float y, float r, float a, System.Random rng)
        {
            // ~40 cm decal: through hole ~3.5 cm, liner-metal ring, soot star out to the edge.
            float soot = Mathf.Clamp01(1f - r) * Mathf.Lerp(0.45f, 1f, Rays(a, 9, 1.3f)) * Mathf.Lerp(0.6f, 1f, Noise(x, y, 6f));
            float ash = Mathf.Clamp01(1f - Mathf.Abs(r - 0.42f) / 0.2f) * Mathf.Lerp(0.3f, 1f, Noise(x, y, 7f));
            var c = Color.Lerp(SootC, Ash, ash * 0.7f);
            float alpha = Mathf.Clamp01(soot * 1.6f + ash * 0.5f);
            float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.2f) / 0.1f) * Mathf.Lerp(0.3f, 1f, Rays(a, 23, 4.1f));
            c = Color.Lerp(c, Color.Lerp(HotMetal, Metal, Noise(x, y, 18f)), ring * 0.85f);
            alpha = Mathf.Max(alpha, ring);
            if (r < 0.09f) { c = Color.black; alpha = 1f; }
            else if (r < 0.12f) { c = Color.Lerp(Color.black, HotMetal, (r - 0.09f) / 0.03f); alpha = 1f; }
            return new Color(c.r, c.g, c.b, alpha);
        }

        static Color Crater(float x, float y, float r, float a, System.Random rng)
        {
            // Jet stopped: shallow crater of fused metal, no daylight through it.
            float soot = Mathf.Clamp01(1f - r) * Mathf.Lerp(0.5f, 1f, Rays(a, 7, 2.2f)) * Mathf.Lerp(0.6f, 1f, Noise(x, y, 5f));
            float ash = Mathf.Clamp01(1f - Mathf.Abs(r - 0.45f) / 0.2f) * Mathf.Lerp(0.3f, 1f, Noise(x, y, 7f));
            var c = Color.Lerp(SootC, Ash, ash * 0.7f);
            float alpha = Mathf.Clamp01(soot * 1.5f + ash * 0.5f);
            if (r < 0.22f)
            {
                float t = r / 0.22f;
                c = Color.Lerp(new Color(0.12f, 0.1f, 0.09f), Color.Lerp(HotMetal, Metal, Noise(x, y, 20f)), t * t);
                alpha = 1f;
            }
            return new Color(c.r, c.g, c.b, alpha);
        }

        static Color Scorch(float x, float y, float r, float a, System.Random rng)
        {
            // Blast soot and burnt paint: irregular, stronger at the centre.
            float edge = 0.55f + 0.35f * Noise(x, y, 2.5f) + 0.1f * Rays(a, 5, 0.7f);
            float t = Mathf.Clamp01(1f - r / edge);
            float alpha = Mathf.Clamp01(t * 1.4f) * Mathf.Lerp(0.55f, 1f, Noise(x, y, 9f)) * 0.95f;
            // Black soot at the core, burnt-to-ash paint towards the rim.
            var c = Color.Lerp(Ash, SootC, Mathf.Clamp01(t * 1.8f) * Mathf.Lerp(0.7f, 1f, Noise(x, y, 14f)));
            return new Color(c.r, c.g, c.b, alpha);
        }

        static Shade Pocks(System.Random rng)
        {
            var pits = new Vector3[45];
            for (int i = 0; i < pits.Length; i++)
            {
                float rr = Mathf.Sqrt((float)rng.NextDouble()) * 0.85f, aa = (float)(rng.NextDouble() * Mathf.PI * 2);
                pits[i] = new Vector3(rr * Mathf.Cos(aa), rr * Mathf.Sin(aa), Mathf.Lerp(0.008f, 0.024f, (float)rng.NextDouble()));
            }
            return (x, y, r, a, _) =>
            {
                var c = Color.clear;
                // Irregular pits: dark gouge, a thin rim of dull bare steel where paint chipped off, a little soot.
                float wobble = 0.75f + 0.5f * Noise(x, y, 60f);
                foreach (var p in pits)
                {
                    float d = Vector2.Distance(new Vector2(x, y), p) / (p.z * wobble);
                    if (d < 1f) return new Color(0.05f, 0.045f, 0.04f, 1f);
                    if (d < 1.6f) c = new Color(0.3f, 0.28f, 0.25f, Mathf.Max(c.a, 0.75f * (1.6f - d) / 0.6f));
                    else if (d < 3f && c.a < 0.25f) c = new Color(SootC.r, SootC.g, SootC.b, 0.25f * (3f - d) / 1.4f);
                }
                return c;
            };
        }

        static void InstallFeature()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            foreach (var f in data.rendererFeatures)
                if (f is DecalRendererFeature) return;
            var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
            feature.name = "Decals";
            AssetDatabase.AddObjectToAsset(feature, data);
            // Keep the feature list and its local-id map in sync, as the renderer inspector does.
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }
    }
}
