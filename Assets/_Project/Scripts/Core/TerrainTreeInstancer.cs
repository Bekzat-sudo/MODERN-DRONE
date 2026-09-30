using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Draws the terrain's tree instances (windbreak trees, bushes) as ordinary renderers, so the GPU Resident Drawer
    /// batches them - instanced, GPU-culled, LOD cross-fade - instead of the terrain's tree path, which draws LOD-group
    /// prefabs one by one. Measured in the editor, Mission_01, FPV view over the tree belts: SetPass calls 1932 -> 243,
    /// CPU 14.5 -> 8.8 ms, GPU 10.3 -> 6.3 ms. Plants without LODs get a cull distance. Tree colliders stay with the terrain collider.
    /// Hierarchical LOD: each tree's last LOD is merged per 60 m cell into one mesh that takes over where the single
    /// trees drop out, so thousands of far trees become a few dozen draws per camera and shadow cascade (the GPU
    /// Resident Drawer's cost grows with instance count). Saplings and small trees just fade out there.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public sealed class TerrainTreeInstancer : MonoBehaviour
    {
        [Tooltip("Bushes and nettles (no LODs of their own) fade out below this share of the screen height. Before, "
                 + "7,000 bushes of ~2,300 triangles and 900 nettles of ~6,600 were drawn to the horizon and into every shadow cascade.")]
        [SerializeField] float smallPlantCull = 0.012f;
        [Tooltip("Cell size of the merged far-tree meshes, m. Smaller cells hand over closer to each tree's own distance.")]
        [SerializeField] float cellSize = 60f;
        [Tooltip("Trees shorter than this (saplings, garden trees) are not merged; they fade out with their last LOD.")]
        [SerializeField] float minMergedHeight = 8f;

        sealed class Cell
        {
            public readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();
            public float transition, size;
            public int count;
            public Bounds bounds;
        }

        /// <summary>Single-LOD group so a plant without LODs is culled (cross-faded) once it is a few pixels tall; no shadows from low ones.</summary>
        void AddCull(GameObject plant)
        {
            var renderers = plant.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y < 1.2f)
                foreach (var r in renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var group = plant.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.SetLODs(new[] { new LOD(smallPlantCull, renderers) });
            group.RecalculateBounds();
        }

        void Awake()
        {
            var terrain = GetComponent<Terrain>();
            var data = terrain.terrainData;
            var protos = data.treePrototypes;
            if (protos.Length == 0) return;

            // Render-only templates: the prefabs without their colliders, under an inactive holder.
            var holder = new GameObject("TreeTemplates");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var templates = new GameObject[protos.Length];
            for (int i = 0; i < protos.Length; i++)
            {
                if (protos[i].prefab == null) continue;
                templates[i] = Instantiate(protos[i].prefab, holder.transform);
                foreach (var c in templates[i].GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                if (templates[i].GetComponent<LODGroup>() == null) AddCull(templates[i]);
            }

            var root = new GameObject("Trees").transform;
            root.SetParent(transform, true);
            Vector3 origin = transform.position, size = data.size;
            var cells = new Dictionary<Vector2Int, Cell>();
            foreach (var t in data.treeInstances)
            {
                var template = templates[t.prototypeIndex];
                if (template == null) continue;
                var go = Instantiate(template, origin + Vector3.Scale(t.position, size), Quaternion.Euler(0f, t.rotation * Mathf.Rad2Deg, 0f), root);
                go.transform.localScale = new Vector3(t.widthScale, t.heightScale, t.widthScale);
                MergeLastLod(go, cells);
            }
            BuildCells(cells, root);
            terrain.treeDistance = 0f; // the terrain stops drawing them; its tree colliders are unaffected
        }

        /// <summary>
        /// Moves a tall tree's last LOD into its cell's merge list and ends the tree's own LODs where that one began;
        /// short trees simply fade out there.
        /// </summary>
        void MergeLastLod(GameObject tree, Dictionary<Vector2Int, Cell> cells)
        {
            var group = tree.GetComponent<LODGroup>();
            if (group == null || group.lodCount < 3) return;
            var lods = group.GetLODs();
            var last = lods[lods.Length - 1];
            float handover = lods[lods.Length - 2].screenRelativeTransitionHeight;
            var trimmed = new LOD[lods.Length - 1];
            System.Array.Copy(lods, trimmed, trimmed.Length);
            group.SetLODs(trimmed);

            float height = group.size * tree.transform.lossyScale.y;
            bool merge = height >= minMergedHeight;
            foreach (var r in last.renderers)
            {
                if (r == null) continue;
                if (merge && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                {
                    var key = new Vector2Int(Mathf.FloorToInt(tree.transform.position.x / cellSize), Mathf.FloorToInt(tree.transform.position.z / cellSize));
                    if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new Cell { bounds = r.bounds };
                    var mats = r.sharedMaterials;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount && sub < mats.Length; sub++)
                    {
                        if (!cell.parts.TryGetValue(mats[sub], out var list)) cell.parts[mats[sub]] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sub, transform = r.transform.localToWorldMatrix });
                    }
                    cell.bounds.Encapsulate(r.bounds);
                    cell.transition += handover;
                    cell.size += group.size * Mathf.Max(tree.transform.lossyScale.x, tree.transform.lossyScale.y);
                    cell.count++;
                }
                Destroy(r.gameObject == tree ? (Object)r : r.gameObject);
            }
        }

        /// <summary>
        /// One mesh per cell (a submesh per material) under a LOD group whose only LOD shows it below the trees'
        /// average handover height; the group's size is the trees' average, so it switches at about the distance the
        /// single trees drop out.
        /// </summary>
        void BuildCells(Dictionary<Vector2Int, Cell> cells, Transform root)
        {
            var parent = new GameObject("FarTrees").transform;
            parent.SetParent(root, false);
            foreach (var pair in cells)
            {
                var cell = pair.Value;
                if (cell.count == 0) continue;
                var centre = cell.bounds.center;
                var subMeshes = new List<CombineInstance>();
                var materials = new List<Material>();
                foreach (var part in cell.parts)
                {
                    var merged = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    var list = part.Value;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var c = list[i];
                        c.transform = Matrix4x4.Translate(-centre) * c.transform;
                        list[i] = c;
                    }
                    merged.CombineMeshes(list.ToArray(), true, true);
                    subMeshes.Add(new CombineInstance { mesh = merged, transform = Matrix4x4.identity });
                    materials.Add(part.Key);
                }
                var mesh = new Mesh { name = $"FarTrees_{pair.Key.x}_{pair.Key.y}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(subMeshes.ToArray(), false, false);
                foreach (var sm in subMeshes) Destroy(sm.mesh);
                mesh.RecalculateBounds();

                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.transform.position = centre;
                go.layer = gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials.ToArray();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // as the trees' own last LOD
                var group = go.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.localReferencePoint = Vector3.zero;
                group.size = cell.size / cell.count;
                // LOD 0 is empty (the single trees are showing); LOD 1 is the merged mesh until it is a dot.
                group.SetLODs(new[] { new LOD(cell.transition / cell.count, new Renderer[0]), new LOD(0.002f, new Renderer[] { mr }) });
            }
        }
    }
}
