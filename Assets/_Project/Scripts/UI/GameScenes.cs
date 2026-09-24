using ModernDrone.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModernDrone.UI
{
    /// <summary>Scene names and loading (always unpauses first).</summary>
    public static class GameScenes
    {
        public const string MainMenu = "MainMenu";
        public const string Mission01 = "Mission_01";
        public const string Mission02 = "Mission_02";
        public const string Mission03 = "Mission_03";
        public const string Mission04 = "Mission_04";
        public const string ProvingGround = "ProvingGround";

        public static void Load(string scene)
        {
            GamePause.Set(false);
            SceneManager.LoadScene(scene);
        }

        public static void Reload() => Load(SceneManager.GetActiveScene().name);

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
