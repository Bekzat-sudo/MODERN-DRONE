using System.IO;
using UnityEditor;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Import settings for the textures taken out of the .glb models (Tools/glb_extract_textures.py writes them to a
    /// "&lt;model&gt;_tex" folder beside the model, named by role). glTFast imports images embedded in a .glb as raw
    /// RGBA32 without compression or anisotropy (~2 GB of VRAM for the vehicles); as separate files they go through the
    /// texture importer: block-compressed (BC7, normals BC5), mipmaps, anisotropic filtering, capped at 2048.
    /// Roles: _col / _emi colour (sRGB); _nrm normal map; _mr, _ao, _orm data (linear).
    /// </summary>
    sealed class ModelTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Art/")) return;
            string dir = Path.GetDirectoryName(assetPath);
            if (dir == null || !dir.EndsWith("_tex")) return;
            var ti = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = name.EndsWith("_nrm");
            bool data = normal || name.EndsWith("_mr") || name.EndsWith("_ao") || name.EndsWith("_orm");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !data;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; // BC7, BC5 for normal maps (PC)
        }
    }
}
