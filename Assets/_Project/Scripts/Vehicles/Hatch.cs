using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// A hatch lid on its hinge (the object's origin, opening about its local x). Pressure from burning propellant
    /// throws it open: it swings up, past vertical it falls against its stop, below vertical gravity pulls it shut
    /// again unless the push carried it over. An ammunition detonation tears lids off their hinges and throws them clear
    /// (catastrophic-kill photos show hatches open or missing; en.wikipedia.org/wiki/Cooking_off). Lid ~30 kg; the
    /// angular kick, stop angle and tear chance are gameplay estimates (abstract).
    /// </summary>
    public sealed class Hatch : MonoBehaviour
    {
        [SerializeField] float openStop = 115f;
        [SerializeField] float mass = 30f;
        [Range(0f, 1f)] [SerializeField] float tearChance = 0.7f;
        [Tooltip("Rotation axis in the lid's space: the hinge line.")]
        [SerializeField] Vector3 axis = Vector3.right;

        Quaternion _closed;
        float _angle, _speed;
        bool _torn;
        VehicleDamageModel _owner;

        public bool IsOpen => _torn || _angle > 25f;
        public bool Torn => _torn;
        public float Angle => _angle;

        public void Configure(float stopDeg, float kg, float tear, Vector3 hingeAxis)
        {
            openStop = stopDeg;
            mass = kg;
            tearChance = tear;
            axis = hingeAxis;
        }

        void Awake()
        {
            _closed = transform.localRotation;
            _owner = GetComponentInParent<VehicleDamageModel>();
        }

        void OnEnable() { if (_owner != null) _owner.Destroyed += OnDestroyed; }
        void OnDisable() { if (_owner != null) _owner.Destroyed -= OnDestroyed; }

        void OnDestroyed(VehicleDamageModel model, DestructionCause cause)
        {
            if (cause != DestructionCause.AmmoDetonation) return;
            if (Random.value < tearChance) TearOff(Random.Range(10f, 18f));
            else Blow(Random.Range(500f, 900f));
        }

        /// <summary>Kicks the lid open with an angular speed, deg/s (a few hundred from a pressure pulse).</summary>
        public void Blow(float degPerSecond)
        {
            if (_torn) return;
            _speed = Mathf.Max(_speed, degPerSecond);
        }

        /// <summary>Rips the lid off and throws it away from the hull at <paramref name="speed"/> m/s.</summary>
        public void TearOff(float speed)
        {
            if (_torn) return;
            _torn = true;
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            transform.SetParent(null, true);
            SetLayer(transform, GameLayers.Debris);
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(bounds.center);
            box.size = Vector3.Max(transform.InverseTransformVector(bounds.size).Abs(), Vector3.one * 0.05f);
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 up = Vector3.up * 1.6f + Random.insideUnitSphere * 0.6f;
            rb.linearVelocity = up.normalized * speed;
            rb.angularVelocity = Random.insideUnitSphere * 14f;
            enabled = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _torn || (_speed == 0f && _angle <= 0f)) return;
            // Gravity torque about the hinge: shuts the lid below vertical, holds it on its stop past vertical.
            float gravity = -900f * Mathf.Cos(_angle * Mathf.Deg2Rad);
            _speed += gravity * dt;
            _speed *= Mathf.Exp(-1.5f * dt); // hinge friction
            _angle += _speed * dt;
            if (_angle >= openStop) { _angle = openStop; _speed = -_speed * 0.25f; }
            if (_angle <= 0f) { _angle = 0f; _speed = Mathf.Abs(_speed) > 30f ? -_speed * 0.3f : 0f; }
            transform.localRotation = _closed * Quaternion.AngleAxis(_angle, axis);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }

    static class VectorAbs
    {
        public static Vector3 Abs(this Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
