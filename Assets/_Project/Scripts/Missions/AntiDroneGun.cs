using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Gun of a mobile fire group (MOG): a truck- or pickup-mounted ZU-23-2 or heavy machine gun that hunts drones.
    /// The crew spots the drone in line of sight, lays the mount at the traverse / elevation rates of a manually
    /// laid gun with lead for the drone's velocity, and fires bursts with tracers only when on target.
    /// Weapon figures: ZU-23-2 2 x 1000 rpm cyclic, 970 m/s, air targets to 2.5 km; DShK 600 rpm, 850 m/s
    /// (en.wikipedia.org/wiki/ZU-23-2, /DShK). A small FPV drone is a far harder target than a Shahed or a Lancet,
    /// so the hit chance per round and the effective range against it are abstract gameplay values.
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class AntiDroneGun : MonoBehaviour
    {
        [SerializeField] Transform traverse;
        [SerializeField] Transform elevation;
        [SerializeField] Transform muzzle;
        [Header("Weapon")]
        [SerializeField] float caliberMm = 23f;
        [SerializeField] float roundsPerMinute = 2000f;
        [SerializeField] float muzzleVelocity = 970f;
        [SerializeField] float burstSeconds = 0.6f;
        [SerializeField] float burstPause = 1.2f;
        [Tooltip("Chance a single round kills the drone at optimal range, slow target (abstract).")]
        [SerializeField] float hitChancePerRound = 0.02f;
        [SerializeField] float range = 1200f;
        [SerializeField] float optimalRange = 250f;
        [Header("Crew")]
        [SerializeField] float reactionTime = 3f;
        [SerializeField] float traverseSpeed = 60f;
        [SerializeField] float elevationSpeed = 40f;
        [SerializeField] float minElevation = -5f;
        [SerializeField] float maxElevation = 85f;
        [Tooltip("Aim error within which the gunner opens fire, deg.")]
        [SerializeField] float fireCone = 3f;

        VehicleDamageModel _model;
        DroneController _drone;
        float _spotted, _burstEnd, _nextBurst, _nextRound;
        static Material _tracerMat;

        public void Configure(Transform traverseJoint, Transform elevationJoint, Transform muzzlePoint, float caliber, float rpm, float velocity,
            float burst, float pause, float perRound, float rangeMeters, float optimal)
        {
            caliberMm = caliber;
            traverse = traverseJoint;
            elevation = elevationJoint;
            muzzle = muzzlePoint;
            roundsPerMinute = rpm;
            muzzleVelocity = velocity;
            burstSeconds = burst;
            burstPause = pause;
            hitChancePerRound = perRound;
            range = rangeMeters;
            optimalRange = optimal;
        }

        void Awake() => _model = GetComponent<VehicleDamageModel>();

        /// <summary>The gun and its crew are out once the mount or the vehicle is destroyed.</summary>
        bool Operational
        {
            get
            {
                if ((_model.State & KillState.Destroyed) != 0) return false;
                foreach (var m in _model.Modules)
                    if (m.Type == ModuleType.Launcher && m.State == ModuleState.Destroyed) return false;
                return true;
            }
        }

        /// <summary>
        /// Per-round kill probability: full at the optimal range, falling to zero at the maximum; a fast, crossing
        /// or diving drone is much harder to hit than one hanging in the air.
        /// </summary>
        public static float RoundKillChance(float perRound, float distance, float optimal, float range, float speed)
        {
            if (distance > range) return 0f;
            float falloff = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(optimal, range, distance));
            float speedFactor = Mathf.Lerp(1.5f, 0.3f, Mathf.InverseLerp(3f, 30f, speed));
            return Mathf.Clamp01(perRound * falloff * speedFactor);
        }

        void Update()
        {
            if (_drone == null) _drone = FindAnyObjectByType<DroneController>();
            if (_drone == null || GamePause.IsPaused || !Operational || !_drone.Armed || _drone.Crashed)
            {
                _spotted = 0f;
                return;
            }

            Vector3 from = Muzzle;
            Vector3 target = _drone.transform.position;
            float dist = Vector3.Distance(from, target);
            bool visible = dist <= range && !Physics.Linecast(from, target, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore);
            if (!visible)
            {
                _spotted = Mathf.Max(0f, _spotted - Time.deltaTime);
                return;
            }
            _spotted += Time.deltaTime;

            // Lead: aim where the drone will be when the round arrives (one iteration is enough at these ranges).
            Vector3 vel = _drone.Body.linearVelocity;
            Vector3 aimPoint = target + vel * (dist / muzzleVelocity);
            float error = Lay(aimPoint);
            if (_spotted < reactionTime) return;

            if (Time.time >= _nextBurst && error < fireCone)
            {
                _burstEnd = Time.time + burstSeconds;
                _nextBurst = _burstEnd + burstPause;
            }
            if (Time.time >= _burstEnd || Time.time < _nextRound) return;

            // Rounds are resolved in small groups (one tracer per group) to keep the frame cost flat.
            const int group = 4;
            _nextRound = Time.time + group * 60f / roundsPerMinute;
            ModernDrone.Vehicles.SimpleAudio.GunShot(from, caliberMm);
            Tracer(from, aimPoint);
            float p = RoundKillChance(hitChancePerRound, dist, optimalRange, range, vel.magnitude) * Mathf.InverseLerp(fireCone, 0f, error);
            if (Random.value < 1f - Mathf.Pow(1f - p, group))
                _drone.ShootDown("СБИТ МОГ");
        }

        Vector3 Muzzle => (muzzle != null ? muzzle : elevation != null ? elevation : transform).position;

        /// <summary>Turns the mount towards the point at crew laying rates; returns the remaining aim error, deg.</summary>
        float Lay(Vector3 point)
        {
            if (traverse == null || elevation == null) return 0f;
            Vector3 local = traverse.parent.InverseTransformPoint(point) - traverse.localPosition;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, minElevation, maxElevation);

            float curYaw = traverse.localEulerAngles.y;
            float newYaw = Mathf.MoveTowardsAngle(curYaw, yaw, traverseSpeed * Time.deltaTime);
            traverse.localRotation = Quaternion.Euler(0f, newYaw, 0f);
            float curPitch = -Mathf.DeltaAngle(0f, elevation.localEulerAngles.x);
            float newPitch = Mathf.MoveTowards(curPitch, pitch, elevationSpeed * Time.deltaTime);
            elevation.localRotation = Quaternion.Euler(-newPitch, 0f, 0f);

            Vector3 dir = elevation.forward, want = (point - Muzzle).normalized;
            return Vector3.Angle(dir, want);
        }

        void Tracer(Vector3 from, Vector3 aim)
        {
            if (_tracerMat == null)
            {
                _tracerMat = new Material(Shader.Find("Sprites/Default"));
                _tracerMat.color = new Color(1f, 0.55f, 0.2f);
            }
            // Dispersion of a hand-laid gun on a moving truck: a few mils.
            Vector3 dir = Quaternion.Euler(Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f), 0f) * (aim - from).normalized;
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _tracerMat;
            lr.startWidth = 0.06f;
            lr.endWidth = 0.02f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.positionCount = 2;
            go.AddComponent<TracerMotion>().Launch(lr, from, dir * muzzleVelocity, Mathf.Min(range, 1500f) / muzzleVelocity, _drone.transform);
        }

        /// <summary>A short glowing streak flying along the round's path.</summary>
        sealed class TracerMotion : MonoBehaviour
        {
            const float CrackRadius = 25f;
            LineRenderer _lr;
            Vector3 _pos, _vel;
            float _dieAt;
            Transform _listener;
            bool _cracked;

            public void Launch(LineRenderer lr, Vector3 pos, Vector3 vel, float life, Transform listener)
            {
                _lr = lr;
                _pos = pos;
                _vel = vel;
                _dieAt = Time.time + life;
                _listener = listener;
                Step();
            }

            void Update()
            {
                if (Time.time >= _dieAt) { Destroy(gameObject); return; }
                Vector3 before = _pos;
                _vel += Physics.gravity * Time.deltaTime;
                _pos += _vel * Time.deltaTime;
                // Supersonic round passing the drone: its shock wave cracks there, ahead of the muzzle report.
                if (!_cracked && _listener != null)
                {
                    Vector3 seg = _pos - before, rel = _listener.position - before;
                    float t = Mathf.Clamp01(Vector3.Dot(rel, seg) / Mathf.Max(1e-4f, seg.sqrMagnitude));
                    Vector3 closest = before + seg * t;
                    float miss = Vector3.Distance(closest, _listener.position);
                    if (t < 1f && miss < CrackRadius)
                    {
                        _cracked = true;
                        ModernDrone.Vehicles.SimpleAudio.Crack(closest, miss);
                    }
                }
                Step();
            }

            void Step()
            {
                _lr.SetPosition(0, _pos - _vel.normalized * 8f);
                _lr.SetPosition(1, _pos);
            }
        }
    }
}
