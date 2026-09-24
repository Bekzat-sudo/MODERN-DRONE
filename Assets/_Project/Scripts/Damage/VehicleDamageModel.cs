using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Aggregates a vehicle's modules into kill states and runs secondary effects:
    /// ammunition detonation and fire (which spreads damage and eventually burns the vehicle out).
    /// </summary>
    public sealed class VehicleDamageModel : MonoBehaviour
    {
        [SerializeField] string displayName = "Vehicle";
        [Tooltip("Inflatable / wooden decoy: no internals, collapses when struck.")]
        [SerializeField] bool isDecoy;

        [Header("Secondary effects (chances 0..1)")]
        [SerializeField] float ammoDetonationChance = 0.7f;
        [SerializeField] float fuelFireChanceDamaged = 0.35f;
        [SerializeField] float fuelFireChanceDestroyed = 0.7f;
        [SerializeField] float engineFireChance = 0.2f;
        [SerializeField] float launcherFireChance = 0.4f;

        [Header("Fire")]
        [SerializeField] float fireDamagePerSecond = 6f;
        [Tooltip("Seconds of burning before the vehicle is burned out.")]
        [SerializeField] float fireDuration = 25f;

        readonly List<VehicleModule> _modules = new List<VehicleModule>();
        readonly List<ArmorElement> _armor = new List<ArmorElement>();
        System.Random _random = new System.Random();
        float _fireTime;

        public string DisplayName => displayName;
        public bool IsDecoy => isDecoy;
        public bool Struck { get; private set; }
        public IReadOnlyList<VehicleModule> Modules => _modules;
        public KillState State { get; private set; }
        public DestructionCause Cause { get; private set; }
        public bool Burning { get; private set; }
        public VehicleModule FireOrigin { get; private set; }

        public event Action<VehicleDamageModel, VehicleModule, ModuleState> ModuleChanged;
        public event Action<VehicleDamageModel, KillState> StateChanged;
        public event Action<VehicleDamageModel, VehicleModule> FireStarted;
        public event Action<VehicleDamageModel, DestructionCause> Destroyed;
        /// <summary>First time any strike touches this vehicle (used by decoys to collapse).</summary>
        public event Action<VehicleDamageModel> FirstStruck;

        void Awake() => Rebuild();

        public void SetDisplayName(string name) => displayName = name;

        public void SetDecoy(bool decoy) => isDecoy = decoy;

        /// <summary>Called by the damage solver whenever a strike reaches this vehicle.</summary>
        public void RegisterHit()
        {
            if (Struck) return;
            Struck = true;
            FirstStruck?.Invoke(this);
        }

        public void SetRandom(System.Random random) => _random = random;

        /// <summary>Collects child modules and armor. Call after building a vehicle in code.</summary>
        public void Rebuild()
        {
            GetComponentsInChildren(true, _modules);
            GetComponentsInChildren(true, _armor);
            foreach (var m in _modules) m.Owner = this;
            foreach (var a in _armor) a.Owner = this;
            State = Evaluate();
        }

        internal void NotifyModuleChanged(VehicleModule module, ModuleState previous)
        {
            ModuleChanged?.Invoke(this, module, previous);
            if ((State & KillState.Destroyed) != 0) return;

            switch (module.Type)
            {
                case ModuleType.AmmoRack when module.State == ModuleState.Destroyed:
                    if (_random.NextDouble() < ammoDetonationChance)
                    {
                        Destroy(DestructionCause.AmmoDetonation);
                        return;
                    }
                    StartFire(module);
                    break;

                case ModuleType.FuelTank:
                    double fuelChance = module.State == ModuleState.Destroyed ? fuelFireChanceDestroyed : fuelFireChanceDamaged;
                    if (_random.NextDouble() < fuelChance) StartFire(module);
                    break;

                case ModuleType.Engine when module.State == ModuleState.Destroyed:
                    if (_random.NextDouble() < engineFireChance) StartFire(module);
                    break;

                case ModuleType.Launcher when module.State == ModuleState.Destroyed:
                    // Missile propellant.
                    if (_random.NextDouble() < launcherFireChance) StartFire(module);
                    break;
            }
            UpdateState();
        }

        public void StartFire(VehicleModule origin)
        {
            if (Burning || (State & KillState.Destroyed) != 0) return;
            Burning = true;
            FireOrigin = origin;
            _fireTime = 0f;
            FireStarted?.Invoke(this, origin);
        }

        /// <summary>Advances fire. Called from Update; public so tests can step time.</summary>
        public void Tick(float dt)
        {
            if (!Burning || (State & KillState.Destroyed) != 0) return;
            _fireTime += dt;

            // Iterate over a copy: damaging modules can trigger detonation and change state.
            var snapshot = _modules.ToArray();
            foreach (var m in snapshot)
            {
                if ((State & KillState.Destroyed) != 0) return;
                m.ApplyDamage(fireDamagePerSecond * dt);
            }

            if (_fireTime >= fireDuration)
                Destroy(DestructionCause.BurnedOut);
        }

        void Update() => Tick(Time.deltaTime);

        void Destroy(DestructionCause cause)
        {
            Cause = cause;
            Burning = cause == DestructionCause.BurnedOut ? false : Burning;
            var prev = State;
            State = Evaluate() | KillState.Destroyed | KillState.Mobility | KillState.Firepower;
            if (State != prev) StateChanged?.Invoke(this, State);
            Destroyed?.Invoke(this, cause);
        }

        void UpdateState()
        {
            var next = Evaluate() | (State & KillState.Destroyed);
            if (next == State) return;
            State = next;
            StateChanged?.Invoke(this, State);
        }

        /// <summary>Kill state implied by module states alone (no secondary effects).</summary>
        public KillState Evaluate()
        {
            bool Has(ModuleType t) => _modules.Exists(m => m.Type == t);
            bool AnyDestroyed(ModuleType t) => _modules.Exists(m => m.Type == t && m.State == ModuleState.Destroyed);
            bool AllDestroyed(ModuleType t) => Has(t) && _modules.TrueForAll(m => m.Type != t || m.State == ModuleState.Destroyed);

            var k = KillState.None;
            bool control = AnyDestroyed(ModuleType.ControlStation);

            if (control || AnyDestroyed(ModuleType.Engine) || AnyDestroyed(ModuleType.Transmission) || AnyDestroyed(ModuleType.Track) || AnyDestroyed(ModuleType.Wheels))
                k |= KillState.Mobility;

            if (control || AnyDestroyed(ModuleType.Gun) || AnyDestroyed(ModuleType.Breech) || AnyDestroyed(ModuleType.TurretDrive)
                || AnyDestroyed(ModuleType.AmmoRack) || AllDestroyed(ModuleType.Launcher))
                k |= KillState.Firepower;

            bool IsSensor(ModuleType type) => type == ModuleType.Optics || type == ModuleType.Radar || type == ModuleType.Antenna;
            bool hasSensors = _modules.Exists(m => IsSensor(m.Type));
            bool sensorsLeft = _modules.Exists(m => IsSensor(m.Type) && m.State != ModuleState.Destroyed);
            if (hasSensors && !sensorsLeft)
                k |= KillState.Sensors;

            return k;
        }
    }
}
