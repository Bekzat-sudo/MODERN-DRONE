using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Internal (or exposed) component of a vehicle with hit points. Its collider sits on the
    /// query-only Module layer: it never collides physically, only damage raycasts hit it.
    /// Fuel tanks, ammunition and engines also carry what they would burn or explode with: a <see cref="SubstanceData"/>
    /// asset and how much of it (litres, charges, rounds, rockets), plus any explosive fill (shells beside the charges).
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
        [Tooltip("What burns (fuel, propellant charges, rocket motors).")]
        [SerializeField] SubstanceData substance;
        [Tooltip("How much of it when full, in the substance's unit (litres, charges, rounds, rockets).")]
        [SerializeField] float amount;
        [Tooltip("Explosive fill a jet can detonate (shells, warheads), or none.")]
        [SerializeField] SubstanceData explosive;
        [Tooltip("How many explosive units (shells, warheads).")]
        [SerializeField] float explosiveCount;
        [Tooltip("Fill level, 0..1.")]
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

        public SubstanceData Substance => substance;
        public SubstanceData Explosive => explosive;
        /// <summary>Contents when full, in the substance's unit.</summary>
        public float Amount => amount;
        public float ExplosiveCount => explosiveCount;
        /// <summary>Holds something burnable.</summary>
        public bool Burnable => substance != null && substance.Burns;
        public bool HighExplosive => explosive != null && explosive.detonates && explosiveCount > 0f && !ExplosiveSpent;
        /// <summary>The fill burned away in a vented fire: it can no longer detonate.</summary>
        public bool ExplosiveSpent { get; internal set; }
        /// <summary>What is left now (load times amount), in the substance's unit.</summary>
        public float Remaining => amount * load;
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
            var lib = SubstanceLibrary.Instance;
            substance = lib != null ? lib.DefaultContents(type) : null;
            amount = lib != null ? lib.DefaultAmount(type) : 0f;
            explosive = lib != null ? lib.DefaultExplosive(type) : null;
            explosiveCount = lib != null ? lib.DefaultExplosiveCount(type) : 0f;
            load = type == ModuleType.Engine || type == ModuleType.Transmission ? 0.3f : 1f;
            _hp = maxHp;
            State = ModuleState.Operational;
            gameObject.layer = GameLayers.Module;
        }

        /// <summary>
        /// Overrides what the module holds (defaults come from its type): <paramref name="units"/> of
        /// <paramref name="contents"/> and <paramref name="explosiveUnits"/> of <paramref name="explosiveFill"/>.
        /// </summary>
        public VehicleModule SetContents(SubstanceData contents, float units, SubstanceData explosiveFill = null, float explosiveUnits = 0f,
            float fill = 1f, bool selfSealing = false)
        {
            substance = contents;
            amount = Mathf.Max(0f, units);
            explosive = explosiveFill;
            explosiveCount = explosiveFill != null ? Mathf.Max(0f, explosiveUnits) : 0f;
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
            ExplosiveSpent = false;
            Heat = 0f;
        }
    }
}
