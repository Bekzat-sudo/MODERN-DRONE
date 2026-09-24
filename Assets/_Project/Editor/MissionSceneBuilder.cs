using ModernDrone.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>Builds the first playable mission scene on the shared procedural terrain.</summary>
    public static class MissionSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Mission_01.unity";
        const string TerrainDataPath = "Assets/_Project/Scenes/Mission_01_Terrain.asset";

        const string Briefing =
            "Разведка обнаружила бронетехнику противника у лесополосы примерно в 330 м к северу " +
            "от точки взлёта (курс 000).\n\n" +
            "Задача: вывести из строя все три танка.\n\n" +
            "Лобовая броня закрыта динамической защитой — ищите борт, корму, крышу моторного отсека и башни. " +
            "Взрыватель взводится через 2 с после арма.";

        [MenuItem("Modern Drone/Setup/Build Mission 01 Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var terrain = ProvingGroundBuilder.CreateTerrain(TerrainDataPath);
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            // Targets parked along the tree line north of the launch site (tree line at z = 1300).
            var c = ProvingGroundBuilder.Center;
            Target(terrain, new Vector3(c.x - 40f, 0f, 1287f), 270f, "Танк №1 (бортом к нам)");
            Target(terrain, new Vector3(c.x + 60f, 0f, 1283f), 180f, "Танк №2 (лбом к нам)");
            Target(terrain, new Vector3(c.x + 150f, 0f, 1278f), 215f, "Танк №3 (на выезде)");

            ProvingGroundBuilder.CreateMissionRig("Задача 1: Стоянка у посадки", Briefing, 4);

            EditorSceneManager.SaveScene(scene, ScenePath);
            ProvingGroundBuilder.AddToBuildSettings(ScenePath, 0);
            Debug.Log($"[ModernDrone] Mission scene built: {ScenePath}");
        }

        static void Target(Terrain terrain, Vector3 pos, float yaw, string label)
        {
            var tank = ProvingGroundBuilder.PlaceTank(terrain, pos, yaw, label);
            tank.AddComponent<MissionTarget>().Configure(label, TargetRequirement.OutOfAction);
        }
    }
}
