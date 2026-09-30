using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>Builds a Windows player of Mission 01 for <see cref="ModernDrone.Missions.Benchmark"/> (run it with -benchmark).</summary>
    public static class BenchmarkBuild
    {
        public const string ExePath = "Builds/Benchmark/ModernDrone.exe";

        [MenuItem("Modern Drone/Build/Benchmark Player (Mission 01)")]
        public static void Build()
        {
            bool timing = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true;
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/_Project/Scenes/Mission_01.unity" },
                    locationPathName = ExePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
                Debug.Log($"[ModernDrone] Benchmark build {report.summary.result}: {ExePath}, {report.summary.totalTime.TotalSeconds:F0} s");
            }
            finally
            {
                // The build serialises project settings with the flag on; write them back as they were.
                PlayerSettings.enableFrameTimingStats = timing;
                AssetDatabase.SaveAssets();
            }
        }
    }
}
