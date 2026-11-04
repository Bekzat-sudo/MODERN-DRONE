using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// A broken track: the belt is cut out of that side of the model (every LOD) and lies on the ground as a strip of
    /// links, from under the road wheels trailing out ahead or behind, where the unrolled upper run falls when it slips
    /// off the sprocket. The wheels are left bare. Models have no separate track mesh: the track is the part of the
    /// model whose material fills the track's envelope (the vehicle's Track_L / Track_R box) down to the ground line,
    /// which the road wheels inside the loop never reach.
    /// </summary>
    public static class ThrownTrack
    {
        // Link pitch of the T-72 track: 137 mm, 97 links, 580 mm wide (KMDB, army-guide.com/eng/product1291.html);
        // used for every tracked vehicle here (others are close, not checked one by one).
        const float Pitch = 0.137f;

        /// <summary>Breaks the track whose envelope box is <paramref name="envelope"/> (a child of the vehicle root).</summary>
        public static GameObject Break(Transform root, Transform envelope, Material linkMaterial)
        {
            if (root == null || envelope == null || !envelope.TryGetComponent(out MeshFilter boxFilter) || boxFilter.sharedMesh == null) return null;
            var box = RootBounds(root, envelope, boxFilter.sharedMesh.bounds);
            var track = FindTrackMaterials(root, box);
            if (track.Count > 0) Cut(root, box, track);
            return LayStrip(root, box, linkMaterial);
        }

        /// <summary>The envelope's mesh bounds in the root's space.</summary>
        static Bounds RootBounds(Transform root, Transform t, Bounds local)
        {
            var b = new Bounds(root.InverseTransformPoint(t.TransformPoint(local.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(root.InverseTransformPoint(t.TransformPoint(c)));
            }
            return b;
        }

        /// <summary>
        /// The materials of the track belt: one named for it if the model has one ("track"), else those with the largest
        /// share of their triangles inside the track envelope (mirrored, both sides count) that reach its ground line.
        /// Hull and road-wheel materials spread well beyond the envelope or stop above the ground; the belt does not.
        /// </summary>
        public static List<Material> FindTrackMaterials(Transform root, Bounds box)
        {
            var found = new List<Material>();
            var visual = root.Find("Visual");
            if (visual == null || !visual.TryGetComponent(out MeshRenderer r) || !r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
                return found;
            var mats = r.sharedMaterials;
            foreach (var m in mats)
                if (m != null && m.name.ToLowerInvariant().Contains("track")) found.Add(m);
            if (found.Count > 0) return found;

            var mesh = mf.sharedMesh;
            var v = mesh.vertices;
            var toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var grown = box;
            grown.Expand(new Vector3(0.1f, 0.1f, 0.2f));
            float ground = box.min.y + box.size.y * 0.2f;
            var share = new float[mesh.subMeshCount];
            float best = 0f;
            for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                int inside = 0, low = 0;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = toRoot.MultiplyPoint3x4((v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f);
                    if (Mathf.Sign(c.x) != Mathf.Sign(box.center.x)) c.x = -c.x;
                    if (!grown.Contains(c)) continue;
                    inside++;
                    if (c.y < ground) low++;
                }
                // Tiny parts (bolts, a lamp) do not decide it.
                if (tris.Length < 3 * 200 || low == 0) continue;
                share[sub] = inside / (tris.Length / 3f);
                best = Mathf.Max(best, share[sub]);
            }
            // The belt is often split in a pair of materials (one per side, or links and pads): take every close one.
            for (int sub = 0; sub < share.Length; sub++)
                if (best > 0f && share[sub] >= best * 0.8f && mats[sub] != null) found.Add(mats[sub]);
            return found;
        }

        /// <summary>Removes the triangles of the track materials on the envelope's side from every renderer that uses them.</summary>
        static void Cut(Transform root, Bounds box, List<Material> track)
        {
            float side = Mathf.Sign(box.center.x);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var mats = r.sharedMaterials;
                Mesh mesh = null;
                Vector3[] v = null;
                var toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int sub = 0; sub < mats.Length && sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    if (!track.Contains(mats[sub])) continue;
                    if (mesh == null)
                    {
                        mesh = Object.Instantiate(mf.sharedMesh);
                        mesh.name = mf.sharedMesh.name + "_TrackBroken";
                        v = mesh.vertices;
                    }
                    var tris = mesh.GetTriangles(sub);
                    var kept = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        var c = toRoot.MultiplyPoint3x4((v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f);
                        if (Mathf.Sign(c.x) == side) continue;
                        kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                    }
                    mesh.SetTriangles(kept, sub, false);
                }
                if (mesh != null) mf.sharedMesh = mesh;
            }
        }

        /// <summary>
        /// The belt on the ground: links from under the front or rear wheels, trailing out by most of a track length
        /// (the upper run), with a slight wander. One mesh in world space; the vehicle cannot move any more.
        /// </summary>
        static GameObject LayStrip(Transform root, Bounds box, Material material)
        {
            bool ahead = Random.value < 0.5f;
            float length = box.size.z * (1f + Random.Range(0.5f, 0.9f));
            float start = ahead ? box.min.z : box.max.z, dir = ahead ? 1f : -1f;
            float width = box.size.x * 0.9f, outward = Mathf.Sign(box.center.x);
            float wander = Random.Range(0.2f, 0.6f) * outward, phase = Random.Range(0f, 6.3f);

            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var parts = new List<CombineInstance>();
            var terrain = Terrain.activeTerrain;
            int links = Mathf.CeilToInt(length / Pitch);
            for (int i = 0; i < links; i++)
            {
                float along = i * Pitch;
                // Under the wheels it lies straight; past the hull it curls out a little to the side.
                float past = Mathf.Max(0f, along - box.size.z);
                float x = box.center.x + wander * Mathf.Clamp01(past / 3f) * (1f + 0.3f * Mathf.Sin(phase + past));
                var local = new Vector3(x, box.min.y, start + dir * along);
                var world = root.TransformPoint(local);
                if (terrain != null) world.y = terrain.SampleHeight(world) + terrain.GetPosition().y;
                var fwd = root.TransformDirection(Vector3.forward * dir);
                fwd.y = 0f;
                var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-4f, 4f), 0f);
                parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(world + Vector3.up * 0.035f, rot, new Vector3(width, 0.07f, Pitch * 0.82f)) });
                // The guide horn in the middle of each link.
                parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(world + Vector3.up * 0.1f, rot, new Vector3(0.06f, 0.07f, Pitch * 0.5f)) });
            }
            var mesh = new Mesh { name = "ThrownTrack", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject("ThrownTrack");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            // Worn steel, a shade lighter than burnt debris: the belt has to read against dark soil.
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0.2f, 0.19f, 0.17f));
            mr.SetPropertyBlock(block);
            return go;
        }
    }
}
