using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>One burning source: a module's contents, or a pool of leaked fuel under the vehicle.</summary>
    public sealed class VehicleFire
    {
        public VehicleModule Source { get; internal set; }
        /// <summary>What burns (the module's substance, or the fuel that pooled under the vehicle).</summary>
        public SubstanceData Substance { get; internal set; }
        /// <summary>Fuel spilled on the ground rather than burning inside the vehicle.</summary>
        public bool IsPool { get; internal set; }
        public Vector3 Position { get; internal set; }
        public float Radius { get; internal set; }
        public float Intensity { get; internal set; }
        public float Remaining { get; internal set; }
        public float Age { get; internal set; }
        public bool Out => Remaining <= 0f;
    }

    /// <summary>What blew the vehicle apart: the module, what it held and how much, where.</summary>
    public struct Detonation
    {
        public VehicleModule module;
        public SubstanceData substance;
        /// <summary>Units of <see cref="substance"/> that went (shells, charges, rockets).</summary>
        public float amount;
        public Vector3 position;
        /// <summary>True for an explosive fill detonating; false for burning propellant blowing the hull apart.</summary>
        public bool explosive;
    }

    /// <summary>
    /// Aggregates a vehicle's modules into kill states and runs secondary effects: what burns, what leaks, what explodes.
    /// Fires are local: each heats the modules within its reach, which take damage by how fire-sensitive they are and
    /// may catch fire or cook off in turn. See <see cref="Combustion"/> for the rules and sources.
    /// </summary>
    public sealed class VehicleDamageModel : MonoBehaviour
    {
        [SerializeField] string displayName = "Vehicle";
        [Tooltip("Inflatable / wooden decoy: no internals, collapses when struck.")]
        [SerializeField] bool isDecoy;

        [Header("Ammunition")]
        [Tooltip("Chance that burning propellant inside a closed hull sets off the rest and blows the vehicle apart " +
                 "(T-72 carousel and loose charges: high; open cargo beds vent the fire: low). Abstract value.")]
        [SerializeField] float ammoDetonationChance = 0.7f;

        [Header("Fire")]
        [Tooltip("Automatic fire suppression discharges (crew and engine compartments). T-72B3 3ETs13: 2 + 2 Halon 1301 bottles.")]
        [SerializeField] int crewSuppressionShots;
        [SerializeField] int engineSuppressionShots;
        [Tooltip("Chance one discharge puts out a fuel or oil fire; propellant fires cannot be suppressed.")]
        [SerializeField] float suppressionChance = 0.85f;
        [Tooltip("Seconds of fire inside the hull before the vehicle is burned out.")]
        [SerializeField] float fireDuration = 25f;

        const float MistFireballRadius = 2.5f, MistFireballDamage = 25f;
        const float PoolRadiusPerLoad = 2.5f, PoolBurnSecondsPerLoad = 30f;
        const float CookOffDelayMin = 0.4f, CookOffDelayMax = 2.5f;

        readonly List<VehicleModule> _modules = new List<VehicleModule>();
        readonly List<ArmorElement> _armor = new List<ArmorElement>();
        readonly List<VehicleFire> _fires = new List<VehicleFire>();
        readonly List<string> _log = new List<string>();
        System.Random _random = new System.Random();
        float _hullFireTime;
        float _pendingBlast = -1f;
        float _pooledFuel;
        SubstanceData _poolSubstance;
        VehicleModule _pendingModule;
        VehicleFire _pool;

        public string DisplayName => displayName;
        public bool IsDecoy => isDecoy;
        public bool Struck { get; private set; }
        public IReadOnlyList<VehicleModule> Modules => _modules;
        public IReadOnlyList<VehicleFire> Fires => _fires;
        /// <summary>What happened to the vehicle's contents (fires, leaks, explosions), in order.</summary>
        public IReadOnlyList<string> Log => _log;
        public KillState State { get; private set; }
        public DestructionCause Cause { get; private set; }
        public bool Burning => _fires.Exists(f => !f.Out);
        public VehicleModule FireOrigin => _fires.Count > 0 ? _fires[0].Source : null;
        /// <summary>The detonation that destroyed the vehicle (module null if none).</summary>
        public Detonation LastDetonation { get; private set; }

        public event Action<VehicleDamageModel, VehicleModule, ModuleState> ModuleChanged;
        public event Action<VehicleDamageModel, KillState> StateChanged;
        public event Action<VehicleDamageModel, VehicleFire> FireStarted;
        public event Action<VehicleDamageModel, VehicleFire> FireEnded;
        public event Action<VehicleDamageModel, VehicleModule> LeakStarted;
        /// <summary>A jet atomised a module's fuel: a short mist fireball (the module says what and how much).</summary>
        public event Action<VehicleDamageModel, VehicleModule> Flash;
        public event Action<VehicleDamageModel, DestructionCause> Destroyed;
        /// <summary>First time any strike touches this vehicle (used by decoys to collapse).</summary>
        public event Action<VehicleDamageModel> FirstStruck;

        void Awake() => Rebuild();

        public void SetDisplayName(string name) => displayName = name;

        public void SetDecoy(bool decoy) => isDecoy = decoy;

        public void SetAmmoConfinement(float chance) => ammoDetonationChance = Mathf.Clamp01(chance);

        public void SetFireSuppression(int crewShots, int engineShots)
        {
            crewSuppressionShots = crewShots;
            engineSuppressionShots = engineShots;
        }

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

        bool Wrecked => (State & KillState.Destroyed) != 0;

        void Note(string text) => _log.Add(text);

        /// <summary>A hit on a module: its contents may light, leak or detonate depending on how it was struck.</summary>
        internal void NotifyHit(VehicleModule m, HitKind kind)
        {
            if (Wrecked) return;
            string name = DamageNames.Ru(m.Type);

            if (m.HighExplosive && Roll(m.Explosive.DetonateChance(kind)))
            {
                Note($"{name}: детонация ВВ");
                Explode(m, true);
                return;
            }

            var p = m.Substance;
            if (!m.Burnable || m.Load <= 0f || m.OnFire) return;

            if (p.mistFireball && kind == HitKind.Jet)
            {
                Note($"{name}: вспышка топливного тумана");
                MistFireball(m);
                if (Wrecked) return;
            }

            float chance = p.IgniteChance(kind);
            // Charges torn open by the hit that wrecked their rack light far more readily.
            if (m.State == ModuleState.Destroyed && (p.kind == SubstanceClass.GunPropellant || p.kind == SubstanceClass.RocketMotor || p.kind == SubstanceClass.CanisterMissile))
                chance = Mathf.Max(chance, 0.8f);
            if (m.ProtectedTank) chance *= Combustion.ProtectedTankIgnition;
            if (Roll(chance))
            {
                Ignite(m);
                return;
            }

            if (p.liquid && !m.Leaking && Roll(Combustion.LeakChance(kind) * (m.ProtectedTank ? Combustion.ProtectedTankLeak : 1f)))
            {
                m.Leaking = true;
                Note($"{name}: течь");
                LeakStarted?.Invoke(this, m);
            }
        }

        internal void NotifyModuleChanged(VehicleModule module, ModuleState previous)
        {
            ModuleChanged?.Invoke(this, module, previous);
            if (!Wrecked) UpdateState();
        }

        /// <summary>Sets a module's contents on fire.</summary>
        public void Ignite(VehicleModule m)
        {
            if (m == null || m.OnFire || Wrecked || !m.Burnable) return;
            var p = m.Substance;
            m.OnFire = true;
            var fire = new VehicleFire
            {
                Source = m, Substance = p, Position = m.transform.position,
                Radius = p.radius, Intensity = p.intensity, Remaining = p.burnSeconds * Mathf.Max(0.2f, m.Load)
            };
            _fires.Add(fire);
            Note($"{DamageNames.Ru(m.Type)}: {(p.violent ? "воспламенение зарядов" : "пожар")}");
            FireStarted?.Invoke(this, fire);

            if (p.violent && Roll(ammoDetonationChance))
            {
                // One charge sets off the next: the pressure builds for a moment, then the hull gives way.
                _pendingBlast = Mathf.Lerp(CookOffDelayMin, CookOffDelayMax, (float)_random.NextDouble());
                _pendingModule = m;
            }
            else if (p.suppressible)
            {
                TrySuppress(fire);
            }
        }

        /// <summary>Halon discharge into the compartment the fire is in.</summary>
        void TrySuppress(VehicleFire fire)
        {
            if (fire.IsPool) return;
            bool engineBay = fire.Source != null && (fire.Source.Type == ModuleType.Engine || fire.Source.Type == ModuleType.Transmission);
            ref int shots = ref engineBay ? ref engineSuppressionShots : ref crewSuppressionShots;
            while (shots > 0 && !fire.Out)
            {
                shots--;
                if (Roll(suppressionChance))
                {
                    Extinguish(fire);
                    Note("сработала система пожаротушения");
                }
            }
        }

        void Extinguish(VehicleFire fire)
        {
            fire.Remaining = 0f;
            if (fire.Source != null) fire.Source.OnFire = false;
            FireEnded?.Invoke(this, fire);
        }

        /// <summary>Jet through a fuel tank: atomised fuel flashes, scorching everything close by.</summary>
        void MistFireball(VehicleModule tank)
        {
            Vector3 c = tank.transform.position;
            Flash?.Invoke(this, tank);
            foreach (var m in _modules.ToArray())
            {
                if (m == tank || Wrecked) continue;
                float d = Distance(m, c);
                if (d > MistFireballRadius) continue;
                m.Heat += 1.5f * (1f - d / MistFireballRadius);
                m.ApplyDamage(MistFireballDamage * (1f - d / MistFireballRadius) * Combustion.FireSensitivity(m.Type), HitKind.Heat);
            }
        }

        /// <summary>
        /// Blows the vehicle apart with <paramref name="m"/>'s contents now: its explosive fill if it has one, otherwise its
        /// burning contents (test bench and tools; in play the damage rules decide when this happens).
        /// </summary>
        public void Detonate(VehicleModule m)
        {
            if (m == null || Wrecked) return;
            Note($"{DamageNames.Ru(m.Type)}: подрыв (полигон)");
            Explode(m, m.HighExplosive);
        }

        /// <summary>A jet's mist fireball out of <paramref name="tank"/> (test bench and tools).</summary>
        public void FlashMist(VehicleModule tank)
        {
            if (tank != null && tank.Burnable && !Wrecked) MistFireball(tank);
        }

        /// <summary>The module's contents blow the vehicle apart (explosive fill, or propellant deflagrating in the closed hull).</summary>
        void Explode(VehicleModule m, bool explosive)
        {
            LastDetonation = new Detonation
            {
                module = m,
                substance = m == null ? null : explosive ? m.Explosive : m.Substance,
                amount = m == null ? 0f : explosive ? m.ExplosiveCount * m.Load : m.Remaining,
                position = m != null ? m.transform.position : transform.position + Vector3.up * 1.5f,
                explosive = explosive
            };
            Destroy(DestructionCause.AmmoDetonation);
        }

        /// <summary>Advances fires, leaks and cook-offs. Called from Update; public so tests can step time.</summary>
        public void Tick(float dt)
        {
            if (Wrecked) return;

            if (_pendingBlast >= 0f)
            {
                _pendingBlast -= dt;
                if (_pendingBlast < 0f)
                {
                    Note("подрыв боекомплекта");
                    Explode(_pendingModule, false);
                    return;
                }
            }

            TickLeaks(dt);

            bool hullFire = false;
            var modules = _modules.ToArray();
            for (int i = 0; i < _fires.Count; i++)
            {
                var fire = _fires[i];
                if (fire.Out) continue;
                fire.Age += dt;
                fire.Remaining -= dt;
                if (fire.Source != null) fire.Source.Load -= dt / Mathf.Max(1f, fire.Substance != null ? fire.Substance.burnSeconds : 30f);
                if (!fire.IsPool) hullFire = true;

                foreach (var m in modules)
                {
                    if (Wrecked) return;
                    float d = Distance(m, fire.Position);
                    if (d > fire.Radius) continue;
                    float exposure = fire.Intensity * (1f - d / fire.Radius) * dt;
                    m.Heat += exposure;
                    m.ApplyDamage(Combustion.FireDamagePerSecond * Combustion.FireSensitivity(m.Type) * exposure, HitKind.Heat);
                    HeatUp(m);
                }
                if (Wrecked) return;
                if (fire.Out)
                {
                    if (fire.Source != null) fire.Source.OnFire = false;
                    FireEnded?.Invoke(this, fire);
                }
            }

            if (hullFire) _hullFireTime += dt;
            if (_hullFireTime >= fireDuration)
            {
                Note("выгорел");
                Destroy(DestructionCause.BurnedOut);
            }
        }

        /// <summary>A module heated long enough lights (fuel, charges) or cooks off (explosive).</summary>
        void HeatUp(VehicleModule m)
        {
            if (m.HighExplosive && m.Heat >= m.Explosive.heatToDetonate)
            {
                Note($"{DamageNames.Ru(m.Type)}: подрыв от нагрева");
                Explode(m, true);
                return;
            }
            if (!m.OnFire && m.Load > 0f && m.Burnable && m.Heat >= m.Substance.heatToIgnite)
                Ignite(m);
        }

        /// <summary>Holed tanks drain into one pool under the vehicle; any fire that reaches it lights the pool.</summary>
        void TickLeaks(float dt)
        {
            foreach (var m in _modules)
            {
                if (!m.Leaking || m.Load <= 0f) continue;
                float spilled = Mathf.Min(m.Load, Combustion.LeakRate * dt);
                if (_poolSubstance == null) _poolSubstance = m.Substance;
                m.Load -= spilled;
                _pooledFuel += spilled;
                TotalSpilled += spilled;
                if (m.Load <= 0f) UpdateState(); // a drained tank can leave the vehicle without fuel
            }
            if (_pooledFuel <= 0f) return;

            Vector3 poolAt = new Vector3(transform.position.x, transform.position.y + 0.1f, transform.position.z);
            if (_pool == null || _pool.Out)
            {
                foreach (var f in _fires)
                {
                    if (f.Out || f.IsPool) continue;
                    if (Vector3.Distance(f.Position, poolAt) > f.Radius + PoolRadiusPerLoad * Mathf.Sqrt(_pooledFuel)) continue;
                    _pool = new VehicleFire
                    {
                        Substance = _poolSubstance, IsPool = true, Position = poolAt, Intensity = 0.8f,
                        Radius = PoolRadiusPerLoad * Mathf.Sqrt(_pooledFuel), Remaining = _pooledFuel * PoolBurnSecondsPerLoad
                    };
                    _pooledFuel = 0f;
                    _fires.Add(_pool);
                    Note("загорелась разлитая лужа топлива");
                    FireStarted?.Invoke(this, _pool);
                    return;
                }
                return;
            }
            // A burning pool is fed by whatever keeps leaking into it.
            _pool.Remaining += _pooledFuel * PoolBurnSecondsPerLoad;
            _pooledFuel = 0f;
        }

        /// <summary>All fuel that has leaked out so far, in tank loads (for the spill visual).</summary>
        public float TotalSpilled { get; private set; }

        static float Distance(VehicleModule m, Vector3 p)
        {
            var col = m.GetComponent<Collider>();
            return col != null && col.enabled ? Vector3.Distance(col.ClosestPoint(p), p) : Vector3.Distance(m.transform.position, p);
        }

        bool Roll(float chance) => chance > 0f && _random.NextDouble() < chance;

        void Update() => Tick(Time.deltaTime);

        /// <summary>An aircraft that lost lift or control struck the ground.</summary>
        public void Crash()
        {
            if (Wrecked) return;
            Destroy(DestructionCause.Crashed);
            var fuel = _modules.Find(m => m.Type == ModuleType.FuelTank && m.Load > 0f && m.Burnable);
            if (fuel != null)
            {
                // The wreck's fuel keeps burning on the ground.
                var fire = new VehicleFire { Source = fuel, Substance = fuel.Substance, IsPool = true, Position = transform.position, Radius = 4f, Intensity = 1f, Remaining = 60f };
                _fires.Add(fire);
                FireStarted?.Invoke(this, fire);
            }
        }

        void Destroy(DestructionCause cause)
        {
            Cause = cause;
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
            // Every tank drained (leaked or burned away): the engine starves.
            bool noFuel = Has(ModuleType.FuelTank) && _modules.TrueForAll(m => m.Type != ModuleType.FuelTank || m.Load <= 0f);

            var k = KillState.None;
            bool control = AnyDestroyed(ModuleType.ControlStation);

            // Twin-engine vehicles (helicopters) keep moving on one engine.
            if (control || noFuel || AllDestroyed(ModuleType.Engine) || AnyDestroyed(ModuleType.Transmission) || AnyDestroyed(ModuleType.Track)
                || AnyDestroyed(ModuleType.Wheels) || AnyDestroyed(ModuleType.Rotor) || AnyDestroyed(ModuleType.TailRotor))
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
