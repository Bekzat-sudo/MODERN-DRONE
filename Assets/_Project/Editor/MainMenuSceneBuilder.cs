using ModernDrone.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernDrone.Editor
{
    /// <summary>Builds the start scene: a tank on a small ground patch as backdrop plus the main menu.</summary>
    public static class MainMenuSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";

        [MenuItem("Modern Drone/Setup/Build Main Menu Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ProvingGroundBuilder.CreateLighting();
            RenderSettings.fogDensity = 0.02f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            const string matPath = "Assets/_Project/Art/Materials/MenuGround.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(0.30f, 0.33f, 0.20f));
                mat.SetFloat("_Smoothness", 0.05f);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            ground.GetComponent<Renderer>().sharedMaterial = mat;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.TankPrefabPath) ?? VehicleBuilder.BuildTank();
            var tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            tank.transform.rotation = Quaternion.Euler(0f, 150f, 0f);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 40f;
            cam.gameObject.AddComponent<AudioListener>();

            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            var so = new SerializedObject(menu);
            so.FindProperty("orbitTarget").objectReferenceValue = tank.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            ProvingGroundBuilder.AddToBuildSettings(ScenePath, 0);
            Debug.Log($"[ModernDrone] Main menu built: {ScenePath}");
        }

        [MenuItem("Modern Drone/Setup/Rebuild Everything")]
        public static void RebuildAll()
        {
            WarheadAssets.CreateAll();
            VehicleBuilder.BuildTank();
            DroneRigBuilder.BuildPrefab();
            ProvingGroundBuilder.Build();
            MissionSceneBuilder.Build();
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
