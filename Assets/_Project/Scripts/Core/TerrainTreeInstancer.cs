using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Draws the terrain's tree instances (windbreak trees, bushes) as ordinary renderers, so the GPU Resident Drawer
    /// batches them - instanced, GPU-culled, LOD cross-fade - instead of the terrain's tree path, which draws LOD-group
    /// prefabs one by one. Measured in the editor, Mission_01, FPV view over the tree belts: SetPass calls 1932 -> 243,
    /// CPU 14.5 -> 8.8 ms, GPU 10.3 -> 6.3 ms. Tree colliders stay with the terrain collider.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public sealed class TerrainTreeInstancer : MonoBehaviour
    {
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
            }

            var root = new GameObject("Trees").transform;
            root.SetParent(transform, true);
            Vector3 origin = transform.position, size = data.size;
            foreach (var t in data.treeInstances)
            {
                var template = templates[t.prototypeIndex];
                if (template == null) continue;
                var go = Instantiate(template, origin + Vector3.Scale(t.position, size), Quaternion.Euler(0f, t.rotation * Mathf.Rad2Deg, 0f), root);
                go.transform.localScale = new Vector3(t.widthScale, t.heightScale, t.widthScale);
            }
            terrain.treeDistance = 0f; // the terrain stops drawing them; its tree colliders are unaffected
        }
    }
}
