using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// A hatch lid on its hinge (this object: its origin is the hinge, it opens about its local x). The lid itself is a
    /// reference to parts of the vehicle's model (one per LOD copy), moved under the hinge when the vehicle wakes, so a
    /// new model only needs its lid parts assigned here. Pressure from burning propellant
    /// throws it open: it swings up, past vertical it falls against its stop, below vertical gravity pulls it shut
    /// again unless the push carried it over. An ammunition detonation tears lids off their hinges and throws them clear
    /// (catastrophic-kill photos show hatches open or missing; en.wikipedia.org/wiki/Cooking_off). Lid ~30 kg; the
    /// angular kick, stop angle and tear chance are gameplay estimates (abstract). A torn-off lid is cleared after a minute.
    /// </summary>
    public sealed class Hatch : MonoBehaviour
    {
        [SerializeField] float openStop = 115f;
        [SerializeField] float mass = 30f;
        [Range(0f, 1f)] [SerializeField] float tearChance = 0.7f;
        [Tooltip("Rotation axis in the hinge's space: the hinge line.")]
        [SerializeField] Vector3 axis = Vector3.right;
        [Tooltip("The lid's parts in the vehicle's model (one per LOD copy); they swing with the hinge.")]
        [SerializeField] Transform[] lid = new Transform[0];

        Quaternion _closed;
        float _angle, _speed, _tornAge;
        bool _torn, _hullRestored;
        BoxCollider _box;
        Collider[] _hull;
        /// <summary>A lid lying on the ground is removed after this long (game time).</summary>
        const float TornLife = 60f;
        VehicleDamageModel _owner;

        public bool IsOpen => _torn || _angle > 25f;
        public bool Torn => _torn;
        public float Angle => _angle;
        public System.Collections.Generic.IReadOnlyList<Transform> Lid => lid;

        public void Configure(float stopDeg, float kg, float tear, Vector3 hingeAxis, Transform[] lidParts = null)
        {
            openStop = stopDeg;
            mass = kg;
            tearChance = tear;
            axis = hingeAxis;
            if (lidParts != null) lid = lidParts;
        }

        void Awake()
        {
            foreach (var part in lid)
                if (part != null && part.parent != transform) part.SetParent(transform, true);
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
            KeepOneLod();
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            transform.SetParent(null, true);
            SetLayer(transform, GameLayers.Debris);
            _box = gameObject.AddComponent<BoxCollider>();
            _box.center = transform.InverseTransformPoint(bounds.center);
            _box.size = Vector3.Max(transform.InverseTransformVector(bounds.size).Abs(), Vector3.one * 0.05f);
            // The lid starts inside the hull's own colliders: it must not be shot out by them.
            if (_owner != null)
            {
                _hull = _owner.GetComponentsInChildren<Collider>();
                foreach (var c in _hull) if (c != null && c != _box) Physics.IgnoreCollision(_box, c, true);
            }
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 up = Vector3.up * 1.6f + Random.insideUnitSphere * 0.6f;
            rb.linearVelocity = up.normalized * speed;
            rb.angularVelocity = Random.insideUnitSphere * 14f;
            _tornAge = 0f;
        }

        /// <summary>
        /// The lid carries a copy per LOD of the vehicle (and a shadow-only one), switched by the vehicle's LOD group.
        /// Flying on its own it keeps one ordinary copy (the middle LOD, shadows on) and leaves the group.
        /// </summary>
        void KeepOneLod()
        {
            var group = _owner != null ? _owner.GetComponent<LODGroup>() : null;
            if (group == null) return;
            var lods = group.GetLODs();
            Renderer keep = null;
            for (int l = 0; l < lods.Length; l++)
            {
                var list = new System.Collections.Generic.List<Renderer>(lods[l].renderers);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var r = list[i];
                    if (r == null || !r.transform.IsChildOf(transform)) continue;
                    list.RemoveAt(i);
                    if (keep == null && l == Mathf.Min(1, lods.Length - 1) && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) keep = r;
                }
                lods[l].renderers = list.ToArray();
            }
            group.SetLODs(lods);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (keep == null) keep = r;
                if (r == keep) continue;
                Destroy(r.gameObject);
            }
            if (keep != null) keep.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_torn)
            {
                _tornAge += dt;
                // Clear of the hull: it may land on it again.
                if (!_hullRestored && _tornAge > 0.4f)
                {
                    _hullRestored = true;
                    if (_hull != null) foreach (var c in _hull) if (c != null && _box != null) Physics.IgnoreCollision(_box, c, false);
                }
                if (_tornAge > TornLife) Destroy(gameObject);
                return;
            }
            if (_speed == 0f && _angle <= 0f) return;
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
