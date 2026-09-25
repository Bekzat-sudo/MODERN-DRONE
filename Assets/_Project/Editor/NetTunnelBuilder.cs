using System.IO;
using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Anti-drone net tunnel over a road stretch: roof and side nets on posts. Nets often set off FPV contact fuzes early,
    /// but a skilled pilot can slip through a gap (ukrinform.net net tests; euromaidanpress.com on road netting, 2026).
    /// Each panel is a Screen-layer armor element; some panels are left out as holes.
    /// </summary>
    public static class NetTunnelBuilder
    {
        const string TexturePath = "Assets/_Project/Art/Textures/NetGrid.png";
        const string MaterialPath = "Assets/_Project/Art/Materials/AntiDroneNet.mat";
        const float SegmentLength = 6f;
        const float Width = 7.5f;
        const float Height = 4.5f;

        /// <summary>Builds a tunnel over the part of the polyline between the two distances (m along the path).</summary>
        public static GameObject Build(Terrain terrain, Vector3[] path, float fromDistance, float toDistance, int seed)
        {
            var root = new GameObject("NetTunnel");
            var net = NetMaterial();
            var post = VehicleKit.Material("VehicleTrack", new Color(0.12f, 0.12f, 0.11f));
            var rng = new System.Random(seed);

            for (float d = fromDistance; d < toDistance; d += SegmentLength)
            {
                Sample(path, d + SegmentLength * 0.5f, out var mid, out var dir);
                mid.y = terrain.SampleHeight(mid);
                var seg = new GameObject("Segment").transform;
                seg.SetParent(root.transform, false);
                seg.SetPositionAndRotation(mid, Quaternion.LookRotation(dir));

                if (rng.NextDouble() > 0.18) Panel(seg, "Roof", new Vector3(0f, Height, 0f), new Vector3(Width, 0.04f, SegmentLength), net);
                if (rng.NextDouble() > 0.1) Panel(seg, "Wall_L", new Vector3(-Width * 0.5f, Height * 0.5f, 0f), new Vector3(0.04f, Height, SegmentLength), net);
                if (rng.NextDouble() > 0.1) Panel(seg, "Wall_R", new Vector3(Width * 0.5f, Height * 0.5f, 0f), new Vector3(0.04f, Height, SegmentLength), net);
                foreach (float x in new[] { -Width * 0.5f, Width * 0.5f })
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    p.name = "Post";
                    p.layer = GameLayers.Environment;
                    p.transform.SetParent(seg, false);
                    p.transform.localPosition = new Vector3(x, Height * 0.5f, -SegmentLength * 0.5f);
                    p.transform.localScale = new Vector3(0.1f, Height, 0.1f);
                    p.GetComponent<Renderer>().sharedMaterial = post;
                }
            }
            return root;
        }

        static void Panel(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<ArmorElement>().Configure(ArmorKind.Screen, 0.85f);
        }

        static void Sample(Vector3[] path, float distance, out Vector3 pos, out Vector3 dir)
        {
            float acc = 0f;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                float len = flat.magnitude;
                if (acc + len >= distance || i == path.Length - 1)
                {
                    float u = Mathf.Clamp01((distance - acc) / Mathf.Max(len, 0.01f));
                    pos = Vector3.Lerp(a, b, u);
                    dir = flat.normalized;
                    return;
                }
                acc += len;
            }
            pos = path[0];
            dir = Vector3.forward;
        }

        static Material NetMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            if (!File.Exists(TexturePath))
            {
                const int size = 128, cell = 16;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool line = x % cell < 3 || y % cell < 3;
                    px[y * size + x] = line ? new Color(0.16f, 0.2f, 0.12f, 1f) : new Color(0f, 0f, 0f, 0f);
                }
                tex.SetPixels(px);
                tex.Apply();
                File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(TexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                // Keep thin mesh lines visible at distance: alpha-tested mips would otherwise fade them out.
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.5f;
                importer.SaveAndReimport();
            }

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "AntiDroneNet" };
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            mat.SetTextureScale("_BaseMap", new Vector2(6f, 4f));
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f); // visible from inside and outside
            mat.SetFloat("_Smoothness", 0f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }
    }
}
