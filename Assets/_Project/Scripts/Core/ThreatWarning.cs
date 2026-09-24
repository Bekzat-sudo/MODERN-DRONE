using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Strongest air-defence lock on the player's drone this frame (0 = not detected, 1 = firing).
    /// Emitters report every frame; readers see the latest completed value.
    /// </summary>
    public static class ThreatWarning
    {
        static int _frame = -1;
        static float _current;
        static float _previous;

        public static void Report(float lockProgress)
        {
            Roll();
            _current = Mathf.Max(_current, Mathf.Clamp01(lockProgress));
        }

        /// <summary>Lock level from the most recent frame that had reports.</summary>
        public static float Lock
        {
            get
            {
                Roll();
                return Mathf.Max(_current, _previous);
            }
        }

        static void Roll()
        {
            if (Time.frameCount == _frame) return;
            _previous = Time.frameCount == _frame + 1 ? _current : 0f;
            _current = 0f;
            _frame = Time.frameCount;
        }
    }
}
