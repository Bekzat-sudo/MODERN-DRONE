using System;
using System.Collections.Generic;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Drives a column of vehicles along a path over the terrain. Immobilised vehicles stop; those behind queue up,
    /// then bypass. When any vehicle is struck the column raises the alarm: it speeds up and lays a smoke screen.
    /// Vehicles that reach the end of the path leave the area ("escaped").
    /// </summary>
    public sealed class Convoy : MonoBehaviour
    {
        [SerializeField] Vector3[] path = new Vector3[0];
        [SerializeField] List<VehicleDamageModel> vehicles = new List<VehicleDamageModel>();
        [SerializeField] float speed = 7f;
        [SerializeField] float alarmSpeedMultiplier = 1.6f;
        [SerializeField] float spacing = 22f;
        [SerializeField] float bypassOffset = 6f;
        [Tooltip("Start moving immediately (otherwise wait for Begin()).")]
        [SerializeField] bool autoStart;

        float[] _cumulative;
        float[] _distance;
        float[] _offset;
        bool[] _escaped;
        bool _running;
        bool _alarm;

        public bool Alarm => _alarm;
        public IReadOnlyList<VehicleDamageModel> Vehicles => vehicles;
        public event Action<VehicleDamageModel> VehicleEscaped;

        public void Configure(Vector3[] waypoints, List<VehicleDamageModel> column, float cruiseSpeed)
        {
            path = waypoints;
            vehicles = column;
            speed = cruiseSpeed;
        }

        public bool IsEscaped(VehicleDamageModel v)
        {
            int i = vehicles.IndexOf(v);
            return i >= 0 && _escaped != null && _escaped[i];
        }

        public void Begin() => _running = true;

        void Start()
        {
            _cumulative = new float[path.Length];
            for (int i = 1; i < path.Length; i++)
                _cumulative[i] = _cumulative[i - 1] + Vector3.Distance(Flat(path[i - 1]), Flat(path[i]));

            int n = vehicles.Count;
            _distance = new float[n];
            _offset = new float[n];
            _escaped = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _distance[i] = (n - 1 - i) * spacing; // lead vehicle first
                var v = vehicles[i];
                // Explicit check: '??' does not see Unity's fake-null for missing components.
                var rb = v.GetComponent<Rigidbody>();
                if (rb == null) rb = v.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true; // moving colliders need a kinematic body
                v.FirstStruck += _ => RaiseAlarm();
                Place(i);
            }
            _running |= autoStart;
        }

        void Update()
        {
            if (!_running || _cumulative == null) return;
            float total = _cumulative[_cumulative.Length - 1];
            float cruise = speed * (_alarm ? alarmSpeedMultiplier : 1f);

            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                if (v == null || _escaped[i] || Immobile(v)) continue;

                float next = _distance[i] + cruise * Time.deltaTime;
                // Queue behind the nearest vehicle ahead; bypass it if it is knocked out.
                bool bypassing = false;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (_escaped[j] || vehicles[j] == null) continue;
                    float gap = _distance[j] - next;
                    if (gap > spacing * 1.5f) break;
                    if (Immobile(vehicles[j]))
                    {
                        if (gap > -spacing * 0.5f) bypassing = true;
                        continue;
                    }
                    if (gap < spacing * 0.8f) next = Mathf.Min(next, _distance[j] - spacing * 0.8f);
                    break;
                }
                _offset[i] = Mathf.MoveTowards(_offset[i], bypassing ? bypassOffset : 0f, 3f * Time.deltaTime);
                _distance[i] = Mathf.Max(_distance[i], next);

                if (_distance[i] >= total)
                {
                    _escaped[i] = true;
                    v.gameObject.SetActive(false);
                    VehicleEscaped?.Invoke(v);
                    continue;
                }
                Place(i);
            }
        }

        static bool Immobile(VehicleDamageModel v) => (v.State & (KillState.Mobility | KillState.Destroyed)) != 0;

        void RaiseAlarm()
        {
            if (_alarm) return;
            _alarm = true;
            foreach (var v in vehicles)
                if (v != null && v.gameObject.activeSelf && !Immobile(v))
                    SimpleVfx.SmokeScreen(v.transform.position + v.transform.forward * 8f);
        }

        void Place(int i)
        {
            var t = vehicles[i].transform;
            Sample(_distance[i], out var pos, out var dir);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            pos += right * _offset[i];
            pos.y = Height(pos);
            // Pitch from terrain height under the front and rear of the hull.
            float front = Height(pos + dir * 3f), rear = Height(pos - dir * 3f);
            Vector3 forward = new Vector3(dir.x, (front - rear) / 6f, dir.z).normalized;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, Vector3.up));
        }

        void Sample(float d, out Vector3 pos, out Vector3 dir)
        {
            for (int k = 1; k < path.Length; k++)
            {
                if (d > _cumulative[k] && k < path.Length - 1) continue;
                float seg = Mathf.Max(0.01f, _cumulative[k] - _cumulative[k - 1]);
                float u = Mathf.Clamp01((d - _cumulative[k - 1]) / seg);
                pos = Vector3.Lerp(path[k - 1], path[k], u);
                dir = Flat(path[k] - path[k - 1]).normalized;
                return;
            }
            pos = path.Length > 0 ? path[0] : transform.position;
            dir = Vector3.forward;
        }

        static float Height(Vector3 p)
        {
            var terrain = Terrain.activeTerrain;
            return terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : p.y;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            for (int i = 1; i < path.Length; i++) Gizmos.DrawLine(path[i - 1], path[i]);
        }
    }
}
