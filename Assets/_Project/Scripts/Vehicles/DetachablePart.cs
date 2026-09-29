using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    public enum DetachCause
    {
        /// <summary>An explosive reactive block that fired: its casing and plates are thrown off the hull.</summary>
        ReactiveFired,
        /// <summary>Ammunition detonation inside: the overpressure blows hatches open and off.</summary>
        Catastrophic
    }

    /// <summary>
    /// A piece of the vehicle model that comes off as physical debris: reactive armour blocks when they fire (the
    /// explosive layer throws the plates and casing outward, en.wikipedia.org/wiki/Reactive_armour), hatches when the
    /// ammunition goes up (the overpressure vents through hatches and the turret ring, en.wikipedia.org/wiki/Cooking_off).
    /// Masses and throw speeds are gameplay estimates.
    /// </summary>
    public sealed class DetachablePart : MonoBehaviour
    {
        [SerializeField] DetachCause cause;
        [SerializeField] float mass = 6f;
        [SerializeField] float throwSpeed = 18f;
        [Range(0f, 1f)] [SerializeField] float chance = 1f;

        VehicleDamageModel _owner;
        ArmorElement _armor;
        bool _detached;

        public bool Detached => _detached;

        public void Configure(DetachCause detachCause, float kg, float speed, float probability)
        {
            cause = detachCause;
            mass = kg;
            throwSpeed = speed;
            chance = probability;
        }

        void Awake()
        {
            _owner = GetComponentInParent<VehicleDamageModel>();
            _armor = GetComponent<ArmorElement>();
        }

        void OnEnable()
        {
            if (cause == DetachCause.ReactiveFired && _armor != null) _armor.Triggered += OnTriggered;
            else if (_owner != null) _owner.Destroyed += OnDestroyed;
        }

        void OnDisable()
        {
            if (_armor != null) _armor.Triggered -= OnTriggered;
            if (_owner != null) _owner.Destroyed -= OnDestroyed;
        }

        void OnTriggered(ArmorElement _)
        {
            SimpleVfx.Explosion(transform.position, 0.6f, ExplosionKind.Era);
            Detach();
        }

        void OnDestroyed(VehicleDamageModel model, DestructionCause destruction)
        {
            if (destruction == DestructionCause.AmmoDetonation && Random.value < chance) Detach();
        }

        /// <summary>Unparents the piece and throws it away from the hull.</summary>
        public void Detach()
        {
            if (_detached) return;
            _detached = true;
            Vector3 centre = _owner != null ? _owner.transform.position + Vector3.up * 1.2f : transform.position - Vector3.up;
            Vector3 away = transform.position - centre;
            away.y = Mathf.Max(away.y, 0.4f * away.magnitude); // blasts lift debris
            away.Normalize();

            transform.SetParent(null, true);
            SetLayer(transform, GameLayers.Debris);
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>();
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearVelocity = away * throwSpeed * Random.Range(0.7f, 1.2f);
            rb.angularVelocity = Random.insideUnitSphere * 12f;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }
    }
}
