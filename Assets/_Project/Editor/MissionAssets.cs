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
            if (assets != null)
            {
                XRayLineShader(assets.line);
                return assets;
            }

            assets = ScriptableObject.CreateInstance<KillcamAssets>();
            assets.ghost = Transparent("XRay_Ghost", "Universal Render Pipeline/Unlit", new Color(0.55f, 0.75f, 1f, 0.10f));
            assets.module = Transparent("XRay_Module", "Universal Render Pipeline/Unlit", Color.white);
            assets.line = Transparent("XRay_Line", "Universal Render Pipeline/Particles/Unlit", Color.white);
            // Traces draw after the ghost shell and module boxes so they stay visible inside them.
            assets.line.renderQueue = (int)RenderQueue.Transparent + 100;
            XRayLineShader(assets.line);
            AssetDatabase.CreateAsset(assets, KillcamAssetsPath);
            AssetDatabase.SaveAssets();
            return assets;
        }

        /// <summary>Traces draw over everything (depth test off), so a short jet inside a module box is not hidden.</summary>
        static void XRayLineShader(Material line)
        {
            var shader = Shader.Find("ModernDrone/XRayLine");
            if (line == null || shader == null || line.shader == shader) return;
            line.shader = shader;
            line.renderQueue = (int)RenderQueue.Transparent + 100;
            EditorUtility.SetDirty(line);
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
