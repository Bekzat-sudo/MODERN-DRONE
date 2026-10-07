using System.Collections.Generic;
using ModernDrone.Core;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Pieces of a field air-defence battery laid out by function, following the level-design guide for a believable
    /// battery (Generic Air Defense Battery Layout, game design doc): a sensor up front with open ground round it, the
    /// command element behind it with a mast and a road, launchers dispersed at uneven spacing and angles in earth
    /// revetments open to their access track, reloads and the technical zone off the firing line, wheel tracks
    /// between them. Fictional placement for the game; no real site is copied.
    /// </summary>
    public static class AirDefenseSiteBuilder
    {
        const string Dir = "Assets/_Project/Art/Environment/AirDefense/";
        static Material _earth;

        /// <summary>
        /// Earth revetment round a vehicle parked at <paramref name="at"/> facing <paramref name="yaw"/>: walls on both
        /// sides and in front, open behind (the vehicle backs in from its track). Walls 2.2 m high (illustrative).
        /// </summary>
        public static void Revetment(Terrain t, Transform parent, Vector3 at, float yaw, float length, float width)
        {
            var root = new GameObject("Revetment").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Ground(t, at), Quaternion.Euler(0f, yaw, 0f));
            const float baseW = 4.5f, h = 2.2f;
            Berm(t, root, new Vector3(-(width * 0.5f + baseW * 0.5f), 0f, 0f), 0f, length);
            Berm(t, root, new Vector3(width * 0.5f + baseW * 0.5f, 0f, 0f), 0f, length * 0.92f);
            Berm(t, root, new Vector3(0f, 0f, length * 0.5f + baseW * 0.5f), 90f, width + 2f * baseW);
            WorldBuilder.ClearAround(t, at, Mathf.Max(length, width) * 0.5f + baseW);
        }

        /// <summary>A straight earth berm (trapezoid section, sloped ends), sunk a little so it meets uneven ground.</summary>
        static void Berm(Terrain t, Transform parent, Vector3 local, float yaw, float length)
        {
            var go = new GameObject("Berm");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var p = go.transform.position;
            p.y = t.SampleHeight(p) + t.transform.position.y - 0.35f;
            go.transform.position = p;
            var mesh = BermMesh(length, 4.5f, 1.2f, 2.5f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = EarthMaterial();
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = true;
            go.layer = GameLayers.Environment;
            go.isStatic = true;
        }

        static readonly Dictionary<float, Mesh> Berms = new Dictionary<float, Mesh>();

        /// <summary>Frustum of a box: base <paramref name="baseW"/> x length, top <paramref name="topW"/> wide, flat-shaded.</summary>
        static Mesh BermMesh(float length, float baseW, float topW, float height)
        {
            float key = Mathf.Round(length * 10f) / 10f;
            if (Berms.TryGetValue(key, out var cached) && cached != null) return cached;
            float bl = length * 0.5f, tl = Mathf.Max(0.5f, bl - (baseW - topW)), bw = baseW * 0.5f, tw = topW * 0.5f;
            Vector3[] c =
            {
                new Vector3(-bw, 0f, -bl), new Vector3(bw, 0f, -bl), new Vector3(bw, 0f, bl), new Vector3(-bw, 0f, bl),
                new Vector3(-tw, height, -tl), new Vector3(tw, height, -tl), new Vector3(tw, height, tl), new Vector3(-tw, height, tl),
            };
            int[][] faces = { new[] { 4, 7, 6, 5 }, new[] { 0, 4, 5, 1 }, new[] { 1, 5, 6, 2 }, new[] { 2, 6, 7, 3 }, new[] { 3, 7, 4, 0 } };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var f in faces)
            {
                int s = v.Count;
                var n = Vector3.Cross(c[f[1]] - c[f[0]], c[f[2]] - c[f[0]]).normalized;
                foreach (int i in f)
                {
                    v.Add(c[i]);
                    // Planar projection along the face's dominant axis, ~4 m per texture tile.
                    var q = c[i];
                    uv.Add((Mathf.Abs(n.y) > 0.7f ? new Vector2(q.x, q.z) : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(q.z, q.y) : new Vector2(q.x, q.y)) / 4f);
                }
                tri.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            var mesh = new Mesh { name = $"Berm_{Mathf.RoundToInt(key * 10f)}" }; // decimetres: no locale decimal separator
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            System.IO.Directory.CreateDirectory(Dir);
            string path = Dir + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);
            return Berms[key] = mesh;
        }

        /// <summary>Dug earth: the terrain's mud scan, a little darker and redder than the field.</summary>
        static Material EarthMaterial()
        {
            if (_earth != null) return _earth;
            const string ground = "Assets/_Project/Art/Environment/Ground/brown_mud_02/brown_mud_02_";
            System.IO.Directory.CreateDirectory(Dir);
            string path = Dir + "Earth.mat";
            _earth = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (_earth == null)
            {
                _earth = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(_earth, path);
            }
            _earth.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ground + "diff_2k.jpg"));
            _earth.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ground + "nor_gl_2k.jpg"));
            _earth.EnableKeyword("_NORMALMAP");
            _earth.SetColor("_BaseColor", new Color(0.82f, 0.74f, 0.66f));
            _earth.SetFloat("_Smoothness", 0.05f);
            _earth.enableInstancing = true;
            EditorUtility.SetDirty(_earth);
            return _earth;
        }

        /// <summary>A static model (glb) on the ground with a box collider, e.g. the command post's van.</summary>
        public static GameObject StaticModel(Terrain t, Transform parent, string glb, Vector3 at, float yaw, string name)
        {
            var go = PropsBuilder.Place(t, parent, AssetDatabase.LoadAssetAtPath<GameObject>(glb), at, yaw, 1f, true);
            go.name = name;
            WorldBuilder.ClearAround(t, at, 5f);
            return go;
        }

        /// <summary>Wheel tracks / a dirt road along the points (painted mud, vegetation cleared).</summary>
        public static void Track(Terrain t, float width, params Vector3[] points) => WorldBuilder.PaintRoad(t, points, width);

        static Vector3 Ground(Terrain t, Vector3 p)
        {
            p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }
    }
}
