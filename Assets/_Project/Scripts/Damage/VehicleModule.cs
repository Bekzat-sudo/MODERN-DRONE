using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Internal (or exposed) component of a vehicle with hit points. Its collider sits on the
    /// query-only Module layer: it never collides physically, only damage raycasts hit it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class VehicleModule : MonoBehaviour
    {
        const float DamagedThreshold = 0.6f;

        [SerializeField] ModuleType type;
        [SerializeField] float maxHp = 100f;
        [Tooltip("Armor points the module absorbs from a jet passing through it.")]
        [SerializeField] float absorb = 25f;

        float _hp = -1f;

        public ModuleType Type => type;
        public float MaxHp => maxHp;
        public float Hp => _hp < 0f ? maxHp : _hp;
        public float Absorb => absorb;
        public ModuleState State { get; private set; }
        public VehicleDamageModel Owner { get; internal set; }

        public void Configure(ModuleType type, float maxHp, float absorb)
        {
            this.type = type;
            this.maxHp = maxHp;
            this.absorb = absorb;
            _hp = maxHp;
            State = ModuleState.Operational;
            gameObject.layer = GameLayers.Module;
        }

        /// <returns>True if the state changed.</returns>
        public bool ApplyDamage(float amount)
        {
            if (amount <= 0f || State == ModuleState.Destroyed) return false;
            _hp = Mathf.Max(0f, Hp - amount);

            var prev = State;
            State = _hp <= 0f ? ModuleState.Destroyed
                : _hp < maxHp * DamagedThreshold ? ModuleState.Damaged
                : ModuleState.Operational;

            if (State == prev) return false;
            Owner?.NotifyModuleChanged(this, prev);
            return true;
        }

        public void Restore()
        {
            _hp = maxHp;
            State = ModuleState.Operational;
        }
    }
}
