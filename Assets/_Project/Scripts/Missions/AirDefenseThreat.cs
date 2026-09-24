using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Vehicles;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Short-range air defence (gameplay model): its radar tracks the drone while it is in range, in line of sight
    /// and above the low-altitude floor. A sustained track becomes a lock and the drone is shot down.
    /// Hiding behind terrain / tree lines or flying very low decays the track. Knocked out by a sensors
    /// or firepower kill.
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class AirDefenseThreat : MonoBehaviour
    {
        [SerializeField] Transform radar;
        [SerializeField] Transform gun;
        [SerializeField] float range = 900f;
        [Tooltip("Below this height above ground the drone is lost in ground clutter, m.")]
        [SerializeField] float minAltitude = 8f;
        [Tooltip("Seconds of continuous track to achieve a firing solution.")]
        [SerializeField] float lockTime = 4f;
        [Tooltip("Seconds for a full track to decay once contact is lost.")]
        [SerializeField] float decayTime = 2f;
        [SerializeField] Material tracerMaterial;

        VehicleDamageModel _model;
        DroneController _drone;

        public float Lock { get; private set; }
        public bool Operational => (_model.State & (KillState.Sensors | KillState.Firepower | KillState.Destroyed)) == 0;

        public void Configure(Transform radarPoint, Transform gunPoint, float rangeMeters, Material tracer)
        {
            tracerMaterial = tracer;
            radar = radarPoint;
            gun = gunPoint;
            range = rangeMeters;
        }

        void Awake() => _model = GetComponent<VehicleDamageModel>();

        void Update()
        {
            if (_drone == null) _drone = FindAnyObjectByType<DroneController>();
            if (_drone == null || !Operational || GamePause.IsPaused)
            {
                Lock = 0f;
                return;
            }

            bool tracked = CanTrack(_drone);
            float dt = Time.deltaTime;
            Lock = Mathf.Clamp01(Lock + (tracked ? dt / lockTime : -dt / decayTime));
            if (Lock > 0f) ThreatWarning.Report(Lock);

            if (Lock >= 1f)
            {
                Fire();
                Lock = 0f;
            }
        }

        bool CanTrack(DroneController drone)
        {
            if (!drone.Armed || drone.Crashed) return false;
            Vector3 from = (radar != null ? radar : transform).position;
            Vector3 to = drone.transform.position;
            float dist = Vector3.Distance(from, to);
            if (dist > range) return false;
            float agl = drone.Telemetry.altitudeAgl;
            if (agl >= 0f && agl < minAltitude) return false;
            // Line of sight: terrain and tree lines mask the drone.
            return !Physics.Linecast(from, to, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        void Fire()
        {
            Vector3 muzzle = (gun != null ? gun : radar != null ? radar : transform).position;
            Vector3 target = _drone.transform.position;
            Tracer(muzzle, target);
            SimpleVfx.Explosion(target, 0.4f);
            _drone.ShootDown();
        }

        void Tracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("AD_Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = 0.08f;
            lr.endWidth = 0.03f;
            lr.sharedMaterial = tracerMaterial;
            lr.startColor = new Color(1f, 0.85f, 0.4f);
            lr.endColor = new Color(1f, 0.5f, 0.1f, 0.4f);
            Destroy(go, 0.35f);
        }
    }
}
