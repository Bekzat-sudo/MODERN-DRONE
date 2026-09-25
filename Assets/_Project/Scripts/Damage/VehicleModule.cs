using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Internal (or exposed) component of a vehicle with hit points. Its collider sits on the
    /// query-only Module layer: it never collides physically, only damage raycasts hit it.
    /// Fuel tanks, ammunition and engines also carry what they would burn with (<see cref="Combustible"/>).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class VehicleModule : MonoBehaviour
    {
        const float DamagedThreshold = 0.6f;

        [SerializeField] ModuleType type;
        [SerializeField] float maxHp = 100f;
        [Tooltip("Armor points the module absorbs from a jet passing through it.")]
        [SerializeField] float absorb = 25f;

        [Header("Contents")]
        [SerializeField] Combustible combustible;
        [Tooltip("Holds high explosive (shells, warheads) that a jet can detonate.")]
        [SerializeField] bool highExplosive;
        [Tooltip("Fill level of a fuel tank, 0..1.")]
        [SerializeField, Range(0f, 1f)] float load = 1f;
        [Tooltip("Self-sealing, foam-filled tank (aircraft).")]
        [SerializeField] bool protectedTank;

        float _hp = -1f;

        public ModuleType Type => type;
        public float MaxHp => maxHp;
        public float Hp => _hp < 0f ? maxHp : _hp;
        public float Absorb => absorb;
        public ModuleState State { get; private set; }
        public VehicleDamageModel Owner { get; internal set; }

        public Combustible Combustible => combustible;
        public bool HighExplosive => highExplosive;
        public bool ProtectedTank => protectedTank;
        /// <summary>Remaining contents, 0..1 (fuel drains when leaking and burns away when on fire).</summary>
        public float Load { get => load; internal set => load = Mathf.Clamp01(value); }
        public bool Leaking { get; internal set; }
        public bool OnFire { get; internal set; }
        /// <summary>Accumulated fire exposure, seconds at full intensity.</summary>
        public float Heat { get; internal set; }

        public void Configure(ModuleType type, float maxHp, float absorb)
        {
            this.type = type;
            this.maxHp = maxHp;
            this.absorb = absorb;
            combustible = Combustion.DefaultFor(type);
            highExplosive = Combustion.DefaultHighExplosive(type);
            load = type == ModuleType.Engine || type == ModuleType.Transmission ? 0.3f : 1f;
            _hp = maxHp;
            State = ModuleState.Operational;
            gameObject.layer = GameLayers.Module;
        }

        /// <summary>Overrides what the module holds (defaults come from its type).</summary>
        public VehicleModule SetContents(Combustible contents, bool explosive = false, float fill = 1f, bool selfSealing = false)
        {
            combustible = contents;
            highExplosive = explosive;
            load = Mathf.Clamp01(fill);
            protectedTank = selfSealing;
            return this;
        }

        /// <returns>True if the state changed.</returns>
        public bool ApplyDamage(float amount, HitKind kind = HitKind.Spall)
        {
            if (amount <= 0f || State == ModuleState.Destroyed) return false;
            _hp = Mathf.Max(0f, Hp - amount);

            var prev = State;
            State = _hp <= 0f ? ModuleState.Destroyed
                : _hp < maxHp * DamagedThreshold ? ModuleState.Damaged
                : ModuleState.Operational;

            // Contents react to every hit, not only to a state change: a jet through a full tank can light it.
            if (kind != HitKind.Heat) Owner?.NotifyHit(this, kind);
            if (State == prev) return false;
            Owner?.NotifyModuleChanged(this, prev);
            return true;
        }

        public void Restore()
        {
            _hp = maxHp;
            State = ModuleState.Operational;
            Leaking = false;
            OnFire = false;
            Heat = 0f;
        }
    }
}
