using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Last-ditch defence of a vehicle against a drone at close range (crew shotgun / vehicle machine gun; no crew model).
    /// Effective range ~40-60 m with shotguns, much harder beyond (defensetechforukraine.org, "Shotgun Doctrine").
    /// Per-shot kill chance is an abstract gameplay value that falls with distance and with drone speed: a fast
    /// terminal dive is hard to hit, a drone loitering next to the vehicle is not.
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class CloseInDefense : MonoBehaviour
    {
        [SerializeField] Transform muzzle;
        [SerializeField] float range = 50f;
        [Tooltip("Distance with full effectiveness, m.")]
        [SerializeField] float optimalRange = 20f;
        [SerializeField] float shotInterval = 0.7f;
        [SerializeField] float baseKillChance = 0.18f;
        [Tooltip("Seconds between spotting the drone and the first shot.")]
        [SerializeField] float reactionTime = 1.2f;

        VehicleDamageModel _model;
        DroneController _drone;
        float _engaged;
        float _nextShot;

        public void Configure(Transform muzzlePoint, float rangeMeters)
        {
            muzzle = muzzlePoint;
            range = rangeMeters;
        }

        void Awake() => _model = GetComponent<VehicleDamageModel>();

        bool Operational
        {
            get
            {
                if ((_model.State & KillState.Destroyed) != 0) return false;
                foreach (var m in _model.Modules)
                    if (m.Type == ModuleType.ControlStation && m.State == ModuleState.Destroyed) return false;
                return true;
            }
        }

        /// <summary>Per-shot kill probability for a drone at the given distance and speed.</summary>
        public static float KillChance(float baseChance, float distance, float optimal, float range, float speed)
        {
            if (distance > range) return 0f;
            float falloff = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(optimal, range, distance));
            float speedFactor = Mathf.Lerp(1.5f, 0.3f, Mathf.InverseLerp(3f, 30f, speed));
            return Mathf.Clamp01(baseChance * falloff * speedFactor);
        }

        void Update()
        {
            if (_drone == null) _drone = FindAnyObjectByType<DroneController>();
            if (_drone == null || GamePause.IsPaused || !Operational || !_drone.Armed || _drone.Crashed)
            {
                _engaged = 0f;
                return;
            }

            Vector3 from = (muzzle != null ? muzzle : transform).position + Vector3.up * 1.5f;
            Vector3 to = _drone.transform.position;
            float dist = Vector3.Distance(from, to);
            bool visible = dist <= range &&
                           !Physics.Linecast(from, to, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (!visible)
            {
                _engaged = 0f;
                return;
            }

            _engaged += Time.deltaTime;
            if (_engaged < reactionTime || Time.time < _nextShot) return;
            _nextShot = Time.time + shotInterval;

            ThreatWarning.ReportGroundFire();
            MuzzleFlash(from, to);
            ModernDrone.Vehicles.SimpleAudio.Shot(from);
            float p = KillChance(baseKillChance, dist, optimalRange, range, _drone.Body.linearVelocity.magnitude);
            if (Random.value < p)
                _drone.ShootDown("СБИТ С ЗЕМЛИ");
        }

        static void MuzzleFlash(Vector3 from, Vector3 to)
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.position = from + (to - from).normalized * 0.8f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.5f);
            light.range = 6f;
            light.intensity = 5f;
            Destroy(go, 0.06f);
        }
    }
}
