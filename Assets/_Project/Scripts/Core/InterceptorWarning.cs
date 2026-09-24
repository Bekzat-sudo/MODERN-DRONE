using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>Closest hostile interceptor chasing the player's drone (for the OSD), refreshed every frame.</summary>
    public static class InterceptorWarning
    {
        static int _frame = -1;
        static float _distance = float.MaxValue;
        static Vector3 _position;
        static int _lastReportFrame = -10;
        static float _lastDistance;
        static Vector3 _lastPosition;

        public static void Report(Vector3 position, float distance)
        {
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                _distance = float.MaxValue;
            }
            if (distance >= _distance) return;
            _distance = distance;
            _position = position;
            _lastReportFrame = Time.frameCount;
            _lastDistance = distance;
            _lastPosition = position;
        }

        /// <summary>True if an interceptor reported this frame or the previous one.</summary>
        public static bool Active => Time.frameCount - _lastReportFrame <= 1;
        public static float Distance => _lastDistance;
        public static Vector3 Position => _lastPosition;
    }
}
