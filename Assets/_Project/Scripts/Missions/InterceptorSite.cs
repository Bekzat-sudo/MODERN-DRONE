using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Ground team operating interceptor drones. Its observation mast detects the player's drone when it is in range,
    /// in line of sight and above the clutter floor; after a short confirmation it launches an interceptor.
    /// Knocked out by destroying the antenna (sensors kill) or the operator post (firepower kill).
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class InterceptorSite : MonoBehaviour
    {
        [SerializeField] Transform sensor;
        [SerializeField] Transform launchPoint;
        [SerializeField] Material interceptorMaterial;

        // Detection: small FPV drones are hard to spot and track; interceptors are mostly effective against larger,
        // predictable targets (Wikipedia "Interceptor drone"; Defense News 2026-03). Values below are abstract gameplay.
        [Header("Detection")]
        [SerializeField] float detectionRange = 350f;
        [SerializeField] float minAltitude = 10f;
        [Tooltip("Seconds of continuous observation before a launch.")]
        [SerializeField] float detectTime = 5f;

        [Header("Interceptors")]
        [SerializeField] int stock = 2;
        [SerializeField] int maxAirborne = 1;
        [SerializeField] float launchCooldown = 15f;
        // Top speed: publicly reported ~310 km/h (~86 m/s) for P1-SUN-class interceptors
        // (pravda.com.ua, militarnyi.com, united24media.com, 2026). Turn rate, acceleration and fuze radius are
        // abstract gameplay values.
        [SerializeField] float maxSpeed = 80f;
        [SerializeField] float acceleration = 20f;
        [SerializeField] float turnRate = 100f;
        [SerializeField] float fuzeRadius = 2.5f;
        [Tooltip("Chance that a pass inside the fuze radius kills the drone (reported interceptor success ~60-90% vs larger targets).")]
        [Range(0f, 1f)] [SerializeField] float killChance = 0.55f;
        [SerializeField] float endurance = 70f;

        readonly List<InterceptorDrone> _airborne = new List<InterceptorDrone>();
        VehicleDamageModel _model;
        DroneController _drone;
        float _cooldown;

        public float Detection { get; private set; }
        public int Stock => stock;
        public int Airborne => _airborne.Count;
        public bool Operational => _model != null && (_model.State & (KillState.Sensors | KillState.Firepower | KillState.Destroyed)) == 0;

        public void Configure(Transform sensorPoint, Transform launch, Material body)
        {
            sensor = sensorPoint;
            launchPoint = launch;
            interceptorMaterial = body;
        }

        void Awake() => _model = GetComponent<VehicleDamageModel>();

        void Update()
        {
            _airborne.RemoveAll(i => i == null || !i.Alive);
            if (_drone == null) _drone = FindAnyObjectByType<DroneController>();
            if (_drone == null || !Operational || GamePause.IsPaused)
            {
                Detection = 0f;
                return;
            }

            _cooldown -= Time.deltaTime;
            bool seen = CanSee(_drone);
            Detection = Mathf.Clamp01(Detection + (seen ? Time.deltaTime / detectTime : -Time.deltaTime / detectTime));
            if (Detection > 0f && _airborne.Count == 0) ThreatWarning.Report(Detection);

            if (Detection >= 1f && _cooldown <= 0f && stock > 0 && _airborne.Count < maxAirborne)
                Launch();
        }

        bool CanSee(DroneController drone)
        {
            if (!drone.Armed || drone.Crashed) return false;
            Vector3 from = (sensor != null ? sensor : transform).position;
            Vector3 to = drone.transform.position;
            if (Vector3.Distance(from, to) > detectionRange) return false;
            float agl = drone.Telemetry.altitudeAgl;
            if (agl >= 0f && agl < minAltitude) return false;
            return !Physics.Linecast(from, to, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        void Launch()
        {
            stock--;
            _cooldown = launchCooldown;
            Vector3 origin = (launchPoint != null ? launchPoint : transform).position + Vector3.up * 0.5f;
            var interceptor = InterceptorDrone.Create(origin, interceptorMaterial);
            interceptor.Launch(this, _drone, Vector3.up * 10f, maxSpeed, acceleration, turnRate, fuzeRadius, endurance, killChance);
            _airborne.Add(interceptor);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere((sensor != null ? sensor : transform).position, detectionRange);
        }
    }
}
