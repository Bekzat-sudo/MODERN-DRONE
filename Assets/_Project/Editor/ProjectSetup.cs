using ModernDrone.Core;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>Applies project-wide physics/time settings that gameplay code relies on.</summary>
    public static class ProjectSetup
    {
        [MenuItem("Modern Drone/Setup/Apply Project Settings")]
        public static void Apply()
        {
            for (int i = 0; i < GameLayers.Names.Length; i++)
            {
                int index = GameLayers.Drone + i;
                if (LayerMask.LayerToName(index) != GameLayers.Names[i])
                    Debug.LogError($"Layer {index} should be '{GameLayers.Names[i]}' but is '{LayerMask.LayerToName(index)}'");
            }

            foreach (var (a, b) in GameLayers.IgnoredCollisionPairs)
                Physics.IgnoreLayerCollision(a, b, true);

            Time.fixedDeltaTime = SimConstants.FixedDeltaTime;
            Time.maximumDeltaTime = 0.1f;
            // Time.* setters only change memory; mark the settings asset dirty so it is written to disk.
            EditorUtility.SetDirty(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            EditorUtility.SetDirty(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;

            AssetDatabase.SaveAssets();
            Debug.Log($"[ModernDrone] Project settings applied: fixedDeltaTime={Time.fixedDeltaTime}, layer collision matrix updated.");
        }
    }
}
