using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Recorded drone poses for the post-strike replay: sampled at a fixed minimum interval, interpolated on playback.
    /// Only the drone is recorded; the world is replayed as it stands frozen at the moment of impact.
    /// </summary>
    public sealed class FlightTrack
    {
        public struct Pose
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;
        }

        readonly List<Pose> _poses = new List<Pose>();
        readonly float _interval;

        /// <param name="interval">Minimum seconds between samples (30 Hz keeps a 10 min sortie under 1 MB).</param>
        public FlightTrack(float interval = 1f / 30f) => _interval = interval;

        public int Count => _poses.Count;
        public float StartTime => _poses.Count > 0 ? _poses[0].time : 0f;
        public float EndTime => _poses.Count > 0 ? _poses[_poses.Count - 1].time : 0f;
        public Pose this[int i] => _poses[i];

        public void Clear() => _poses.Clear();

        /// <summary>Adds a sample unless it is closer than the interval to the last one; <paramref name="force"/> always adds.</summary>
        public void Add(float time, Vector3 position, Quaternion rotation, bool force = false)
        {
            if (_poses.Count > 0)
            {
                float dt = time - _poses[_poses.Count - 1].time;
                if (dt <= 0f || (!force && dt < _interval)) return;
            }
            _poses.Add(new Pose { time = time, position = position, rotation = rotation });
        }

        /// <summary>Pose at <paramref name="time"/>, clamped to the recorded span.</summary>
        public Pose Sample(float time)
        {
            if (_poses.Count == 0) return new Pose { rotation = Quaternion.identity };
            if (time <= StartTime) return _poses[0];
            if (time >= EndTime) return _poses[_poses.Count - 1];
            int lo = 0, hi = _poses.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_poses[mid].time <= time) lo = mid; else hi = mid;
            }
            var a = _poses[lo];
            var b = _poses[hi];
            float k = (time - a.time) / (b.time - a.time);
            return new Pose
            {
                time = time,
                position = Vector3.Lerp(a.position, b.position, k),
                rotation = Quaternion.Slerp(a.rotation, b.rotation, k)
            };
        }
    }
}
