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
            var preview = menu.gameObject.AddComponent<ModernDrone.Missions.HangarPreview>();
            preview.SetOutline(ProvingGroundBuilder.OutlineMaterial());
            menu.SetHangar(DroneModels.LoadAll(), DroneParts.LoadAll(), ProvingGroundBuilder.Warheads(), preview);
            menu.SetMissions(
                new[] { "Стоянка у посадки", "Прикрытие РЭБ", "Огневые позиции", "Колонна", "Позиция ЗРК", "Ложные цели", "Ночная охота", "Охота на вертолёт" },
                new[] { GameScenes.Mission01, GameScenes.Mission02, GameScenes.Mission03, GameScenes.Mission04, GameScenes.Mission05, GameScenes.Mission06, GameScenes.Mission07, GameScenes.Mission08 },
                new[] { MissionSceneBuilder.Briefing, MissionSceneBuilder.Briefing02, MissionSceneBuilder.Briefing03, MissionSceneBuilder.Briefing04,
                        MissionSceneBuilder.Briefing05, MissionSceneBuilder.Briefing06, MissionSceneBuilder.Briefing07, MissionSceneBuilder.Briefing08 });

            EditorSceneManager.SaveScene(scene, ScenePath);
            ProvingGroundBuilder.AddToBuildSettings(ScenePath, 0);
            Debug.Log($"[ModernDrone] Main menu built: {ScenePath}");
        }

        [MenuItem("Modern Drone/Setup/Rebuild Everything")]
        public static void RebuildAll()
        {
            VideoFeedSetup.Install();
            WarheadAssets.CreateAll();
            VehicleBuilder.BuildTank();
            VehicleBuilder.BuildEwStation();
            VehicleCatalog.BuildAll();
            DroneRigBuilder.BuildPrefab();
            ProvingGroundBuilder.Build();
            MissionSceneBuilder.Build();
            MissionSceneBuilder.BuildMission02();
            MissionSceneBuilder.BuildMission03();
            MissionSceneBuilder.BuildMission04();
            MissionSceneBuilder.BuildMission05();
            MissionSceneBuilder.BuildMission06();
            MissionSceneBuilder.BuildMission07();
            MissionSceneBuilder.BuildMission08();
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
