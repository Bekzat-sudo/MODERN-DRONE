using System;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>Global pause state. Pausing stops simulation time; input and HUD code check <see cref="IsPaused"/>.</summary>
    public static class GamePause
    {
        public static bool IsPaused { get; private set; }

        public static event Action<bool> Changed;

        public static void Set(bool paused)
        {
            if (paused == IsPaused) return;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Changed?.Invoke(paused);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            Changed = null;
        }
    }
}
