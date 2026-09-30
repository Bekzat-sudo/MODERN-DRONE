using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Editor
{
    /// <summary>Installs the analog-video full-screen pass and the auto-exposure meter on the PC URP renderer (idempotent).</summary>
    public static class VideoFeedSetup
    {
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string ShaderPath = "Assets/_Project/Art/Shaders/AnalogVideo.shader";
        const string MaterialPath = "Assets/_Project/Art/Materials/AnalogVideo.mat";
        const string FeatureName = "AnalogVideo";

        [MenuItem("Modern Drone/Setup/Install Video Feed Effect")]
        public static void Install()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath)) { name = FeatureName };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            bool hasMeter = false;
            foreach (var f in data.rendererFeatures)
                hasMeter |= f is ModernDrone.Video.AutoExposureFeature;
            if (!hasMeter)
            {
                var meter = ScriptableObject.CreateInstance<ModernDrone.Video.AutoExposureFeature>();
                meter.name = "AutoExposure";
                AddFeature(data, meter);
            }

            foreach (var f in data.rendererFeatures)
            {
                if (f is FullScreenPassRendererFeature existing && f.name == FeatureName)
                {
                    existing.passMaterial = material;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                    return;
                }
            }

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = FeatureName;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.passMaterial = material;
            AddFeature(data, feature);
            Debug.Log("[ModernDrone] Analog video full-screen pass installed on " + RendererPath);
        }

        static void AddFeature(UniversalRendererData data, ScriptableRendererFeature feature)
        {
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
            AssetDatabase.SaveAssets();
        }
    }
}
