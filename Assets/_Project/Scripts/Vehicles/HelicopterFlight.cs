using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Attack helicopter working a front line: flies low between firing points, hovers at each to fire, moves on.
    /// FPV drones have caught helicopters in exactly these low, slow phases, often through the tail rotor
    /// (twz.com, "Russian Mi-28 Havoc Attack Helicopter Engaged In Mid-Air By Ukrainian Drone"). Speeds and hover
    /// times are gameplay values, well below the Mi-28NM's 270 km/h cruise (en.wikipedia.org/wiki/Mil_Mi-28).
    /// A mobility kill (rotor, tail rotor, gearbox or both engines) turns flight into a crash.
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class HelicopterFlight : MonoBehaviour
    {
        [SerializeField] Vector3[] firingPoints = new Vector3[0];
        [SerializeField] Transform rotor;
        [Tooltip("Height above the terrain, m.")]
        [SerializeField] float altitude = 35f;
        [Tooltip("Transit speed between firing points, m/s (abstract gameplay value).")]
        [SerializeField] float transitSpeed = 28f;
        [SerializeField] float hoverTime = 22f;
        [SerializeField] float turnRate = 35f;
        [Tooltip("Visual main rotor speed, deg/s (kept low to avoid strobing).")]
        [SerializeField] float rotorSpin = 900f;

        VehicleDamageModel _model;
        Rigidbody _rb;
        int _target;
        float _hover;
        Vector3 _velocity;
        bool _crashing;

        public bool Crashing => _crashing;

        public void Configure(Vector3[] points, Transform mainRotor)
        {
            firingPoints = points;
            rotor = mainRotor;
        }

        void Awake()
        {
            _model = GetComponent<VehicleDamageModel>();
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true; // moved by script; kinematic so the drone's contact fuze still sees the hull
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.mass = 11000f;
        }

        void OnEnable() => _model.StateChanged += OnStateChanged;
        void OnDisable() => _model.StateChanged -= OnStateChanged;

        void OnStateChanged(VehicleDamageModel model, KillState state)
        {
            if (_crashing || (state & (KillState.Mobility | KillState.Destroyed)) == 0) return;
            _crashing = true;
            bool noTail = false;
            foreach (var m in model.Modules)
                if (m.Type == ModuleType.TailRotor && m.State == ModuleState.Destroyed) noTail = true;
            // Without the tail rotor the main rotor's torque spins the fuselage.
            float yaw = noTail ? 220f : Random.Range(-40f, 40f);
            _rb.isKinematic = false;
            _rb.linearVelocity = _velocity;
            _rb.angularVelocity = new Vector3(Random.Range(-0.3f, 0.3f), yaw * Mathf.Deg2Rad, Random.Range(-0.3f, 0.3f));
        }

        void FixedUpdate()
        {
            if (_crashing)
            {
                // A windmilling rotor still gives some lift and slows the fall.
                if (!_rb.isKinematic && (_model.State & KillState.Destroyed) == 0) _rb.AddForce(Vector3.up * Physics.gravity.magnitude * 0.35f, ForceMode.Acceleration);
                return;
            }
            if ((_model.State & KillState.Destroyed) != 0 || firingPoints.Length == 0) return;

            float dt = Time.fixedDeltaTime;
            Vector3 goal = firingPoints[_target];
            Vector3 flat = new Vector3(goal.x - _rb.position.x, 0f, goal.z - _rb.position.z);

            if (flat.magnitude < 8f)
            {
                _hover += dt;
                _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, 6f * dt);
                if (_hover >= hoverTime)
                {
                    _hover = 0f;
                    _target = (_target + 1) % firingPoints.Length;
                }
            }
            else
            {
                _velocity = Vector3.MoveTowards(_velocity, flat.normalized * transitSpeed, 4f * dt);
            }
            // Hold height over the terrain under the aircraft.
            _velocity.y = Mathf.Clamp((Ground(_rb.position) + altitude - _rb.position.y) * 0.8f, -6f, 6f);

            Vector3 face = _hover > 0f ? FacingDuringHover() : new Vector3(_velocity.x, 0f, _velocity.z);
            Quaternion yawNow = Quaternion.Euler(0f, _rb.rotation.eulerAngles.y, 0f);
            Quaternion heading = face.sqrMagnitude > 0.1f ? Quaternion.LookRotation(face) : yawNow;
            heading = Quaternion.RotateTowards(yawNow, heading, turnRate * dt);
            // Nose down in proportion to forward speed, as a helicopter tilts its rotor to accelerate.
            float forward = Vector3.Dot(_velocity, heading * Vector3.forward);
            Quaternion attitude = heading * Quaternion.Euler(Mathf.Clamp(forward * 0.35f, -10f, 12f), 0f, 0f);

            _rb.MovePosition(_rb.position + _velocity * dt);
            _rb.MoveRotation(attitude);
        }

        /// <summary>While firing it faces the next firing point.</summary>
        Vector3 FacingDuringHover()
        {
            var next = firingPoints[(_target + 1) % firingPoints.Length];
            return new Vector3(next.x - _rb.position.x, 0f, next.z - _rb.position.z);
        }

        void Update()
        {
            if (rotor != null && (_model.State & KillState.Destroyed) == 0)
                rotor.Rotate(0f, rotorSpin * (_crashing ? 0.6f : 1f) * Time.deltaTime, 0f, Space.Self);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!_crashing || (_model.State & KillState.Destroyed) != 0) return;
            if (collision.rigidbody != null && collision.rigidbody.mass < 50f) return; // the drone, not the ground
            SimpleVfx.Explosion(transform.position, 3f);
            _model.Crash();
            // The wreck settles instead of sliding and tumbling across the field.
            _rb.linearDamping = 3f;
            _rb.angularDamping = 3f;
        }

        static float Ground(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }
    }
}
