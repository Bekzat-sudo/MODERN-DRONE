using ModernDrone.Missions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernDrone.Editor
{
    /// <summary>Creates killcam materials as assets so their shaders ship with builds.</summary>
    public static class MissionAssets
    {
        const string KillcamAssetsPath = "Assets/_Project/Data/KillcamAssets.asset";
        const string MatDir = "Assets/_Project/Art/Materials";

        public static KillcamAssets LoadKillcamAssets()
        {
            var assets = AssetDatabase.LoadAssetAtPath<KillcamAssets>(KillcamAssetsPath);
            if (assets != null) return assets;

            assets = ScriptableObject.CreateInstance<KillcamAssets>();
            assets.ghost = Transparent("XRay_Ghost", "Universal Render Pipeline/Unlit", new Color(0.55f, 0.75f, 1f, 0.10f));
            assets.module = Transparent("XRay_Module", "Universal Render Pipeline/Unlit", Color.white);
            assets.line = Transparent("XRay_Line", "Universal Render Pipeline/Particles/Unlit", Color.white);
            // Traces draw after the ghost shell and module boxes so they stay visible inside them.
            assets.line.renderQueue = (int)RenderQueue.Transparent + 100;
            AssetDatabase.CreateAsset(assets, KillcamAssetsPath);
            AssetDatabase.SaveAssets();
            return assets;
        }

        static Material Transparent(string name, string shader, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(Shader.Find(shader)) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
