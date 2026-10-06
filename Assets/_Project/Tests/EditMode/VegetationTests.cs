using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class VegetationTests
    {
        /// <summary>
        /// Tree crowns are hit as a convex hull inside the leaves that tapers with the crown (TreeBuilder.CrownHull),
        /// not a cylinder standing clear of a tapering top: a drone must not crash into air beside a tree.
        /// </summary>
        [Test]
        public void TreeCrownHulls_StayInsideTheTreeAndTaper()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Art/Environment/Plants/Trees" });
            int crowns = 0;
            foreach (var guid in guids)
            {
                var tree = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var crown = tree.transform.Find("Crown");
                if (crown == null) continue;
                crowns++;
                var col = crown.GetComponent<MeshCollider>();
                Assert.IsNotNull(col, tree.name);
                Assert.IsTrue(col.convex, tree.name);
                var hull = col.sharedMesh;
                Assert.LessOrEqual(hull.vertexCount, 255, tree.name); // convex hull limit

                var visible = tree.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
                Assert.Less(hull.bounds.extents.x, visible.extents.x, tree.name);
                Assert.Less(hull.bounds.extents.z, visible.extents.z, tree.name);
                Assert.LessOrEqual(hull.bounds.max.y, visible.max.y + 0.01f, tree.name);

                float widest = 0f, top = 0f, topY = hull.bounds.max.y - 0.15f * hull.bounds.size.y;
                foreach (var v in hull.vertices)
                {
                    float r = new Vector2(v.x, v.z).magnitude;
                    widest = Mathf.Max(widest, r);
                    if (v.y > topY) top = Mathf.Max(top, r);
                }
                Assert.Less(top, 0.85f * widest, $"{tree.name}: the hull should narrow towards the crown's top");
            }
            Assert.Greater(crowns, 0, "no tree prefabs with a crown found (run Modern Drone/Setup/Rebuild Everything)");
        }
    }
}
