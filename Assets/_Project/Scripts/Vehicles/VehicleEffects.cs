using System.Collections;
using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Visual consequences of vehicle damage, chosen from data: what burns or blows up (the module's
    /// <see cref="SubstanceData"/> and how much of it is left) picks the effect recipes and sizes them by published
    /// relations (<see cref="FireScaling"/>); where it shows comes from the vehicle's <see cref="VehicleFxProfile"/>.
    /// Fires inside the hull show where they would on a real vehicle: flames leave through the open hatches, turret
    /// ring, engine grilles and jet holes of the burning compartment and those connected to it (<see cref="VentPicker"/>),
    /// never through the armour; with every opening shut, smoke seeps from the seams. Flame height through an opening
    /// follows the fire's heat release, capped by what the opening lets in (Kawagoe ventilation limit, Heskestad flame
    /// height). Violent deflagration (propellant) blows the hatches near it open and roars out of them as flame jets and
    /// breaches the bulkheads around it. A detonation throws the turret (a confined, violent mass only), tears hatches
    /// off, and leaves the wreck cooking off by what it carried: shells going one after another, small-arms rounds
    /// crackling, rockets breaking loose and flying, missiles blowing out of their canisters' ends, propellant roaring
    /// out of the turret ring, fuel burning for minutes (the visual burn here is a shortened, abstract 3-6 min).
    /// Paint chars while fires burn: first black soot and paint ash, then, as the hot steel oxidises, rust red
    /// (burnt-out vehicles "rust immediately": steel that got hot enough oxidises fast, armorama.com / quora.com notes).
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class VehicleEffects : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Imported glTF models (glTFast shader graphs).
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Seconds of burning for the paint to char fully.")]
        [SerializeField] float charTime = 90f;
        [Tooltip("Seconds a wreck keeps burning and smoking after it is destroyed (visual only, abstract).")]
        [SerializeField] float wreckBurn = 200f;
        [Tooltip("Rockets flying off a burning launcher at most (particle and physics budget).")]
        [SerializeField] int maxFlyingRockets = 6;

        static readonly Color Soot = new Color(0.06f, 0.055f, 0.05f);
        static readonly Color Rust = new Color(0.33f, 0.17f, 0.1f);

        sealed class Burning
        {
            public VehicleFire fire;
            public int compartment = -1;
            public readonly List<(FireVent vent, FxEffect fx)> flames = new List<(FireVent, FxEffect)>();
            public FxEffect seep;
            public bool ended;
        }

        VehicleDamageModel _model;
        HeatSignature _heatSig;
        VehicleFxProfile _profile;
        System.Func<int, int, bool> _connected;
        readonly List<FireVent> _vents = new List<FireVent>();
        readonly List<VentPicker.Vent> _ventInfo = new List<VentPicker.Vent>();
        readonly List<(int index, float share)> _shares = new List<(int, float)>();
        readonly List<int> _blown = new List<int>();
        readonly Dictionary<VehicleFire, Burning> _burning = new Dictionary<VehicleFire, Burning>();
        readonly List<FxEffect> _jets = new List<FxEffect>();
        // Wreck fire and smoke after destruction (not tied to a VehicleFire); the flames among them heat the hull.
        readonly List<FxEffect> _wreck = new List<FxEffect>();
        readonly HashSet<FxEffect> _wreckFlames = new HashSet<FxEffect>();
        static int _nextSmokeId = 1000;
        readonly int _smokeId = _nextSmokeId += 2;
        FireVent _ring;
        bool _ventsBuilt, _wrecked;
        float _heat, _nextUpdate, _glow;
        Transform _spill;
        static Material _spillMat;
        MaterialPropertyBlock _glowBlock;

        public IReadOnlyList<FireVent> Vents { get { EnsureVents(); return _vents; } }
        Transform Turret => _profile != null ? _profile.turret : null;

        void Awake()
        {
            _model = GetComponent<VehicleDamageModel>();
            _heatSig = GetComponent<HeatSignature>();
            _profile = GetComponent<VehicleFxProfile>();
            if (GetComponent<HitMarks>() == null) gameObject.AddComponent<HitMarks>();
        }

        void OnEnable()
        {
            _model.FireStarted += OnFireStarted;
            _model.FireEnded += OnFireEnded;
            _model.LeakStarted += OnLeak;
            _model.Flash += OnFlash;
            _model.Destroyed += OnDestroyed;
            _model.FirstStruck += OnFirstStruck;
            DamageSolver.Detonated += OnDetonated;
        }

        void OnDisable()
        {
            _model.FireStarted -= OnFireStarted;
            _model.FireEnded -= OnFireEnded;
            _model.LeakStarted -= OnLeak;
            _model.Flash -= OnFlash;
            _model.Destroyed -= OnDestroyed;
            _model.FirstStruck -= OnFirstStruck;
            DamageSolver.Detonated -= OnDetonated;
        }

        // ---------------------------------------------------------------- openings

        /// <summary>The openings authored on the prefab (a vehicle without a profile gets suggested ones, with a warning).</summary>
        void EnsureVents()
        {
            if (_ventsBuilt) return;
            _ventsBuilt = true;
            if (_profile == null)
            {
                Debug.LogWarning($"[ModernDrone] {name}: no VehicleFxProfile, suggesting compartments and openings at runtime.", this);
                _profile = gameObject.AddComponent<VehicleFxProfile>();
                VehicleFxSetup.Suggest(_profile);
            }
            _connected = _profile.Connected;
            GetComponentsInChildren(true, _vents);
            _ring = _vents.Find(v => v.Kind == VentKind.TurretRing);
        }

        int CompartmentOf(Vector3 p) => _profile != null ? VehicleFxSetup.Nearest(_profile, p) : -1;

        /// <summary>Jet holes become openings the fire can burn out of (into the compartment behind the plate).</summary>
        void OnDetonated(StrikeReport report)
        {
            if (report == null) return;
            foreach (var mark in report.marks)
            {
                if (mark.owner != _model || mark.kind != MarkKind.JetHole || mark.surface == null) continue;
                EnsureVents();
                var go = new GameObject("Vent_Hole");
                go.transform.SetParent(mark.surface, true);
                go.transform.SetPositionAndRotation(mark.point + mark.normal * 0.03f, Quaternion.FromToRotation(Vector3.up, mark.normal));
                var v = go.AddComponent<FireVent>();
                // A ~10 cm hole through the plate (shaped-charge entry holes are a fraction of the charge diameter).
                v.Configure(VentKind.Hole, 0.01f, null, null, CompartmentOf(mark.point - mark.normal * 0.3f));
                _vents.Add(v);
            }
        }

        /// <summary>Every opening as seen from a fire in <paramref name="compartment"/> (unreachable ones blocked).</summary>
        List<VentPicker.Vent> VentInfo(int compartment)
        {
            _ventInfo.Clear();
            foreach (var v in _vents)
            {
                var info = v != null ? v.Info : default;
                // Openings on a part thrown off the hull (the tossed turret) no longer lead into it.
                if (v == null || !v.transform.IsChildOf(transform)) info.open = false;
                _ventInfo.Add(info);
            }
            VentPicker.Block(compartment, _ventInfo, _connected);
            return _ventInfo;
        }

        // ---------------------------------------------------------------- fires

        static FxLibrary Lib => FxLibrary.Instance;

        /// <summary>Black oily smoke for fuel, oil, rubber and paint; grey-white for propellant and rocket motors.</summary>
        static bool BlackSmoke(SubstanceData s) => s == null || s.kind == SubstanceClass.LiquidFuel || s.kind == SubstanceClass.Inert;

        static FxRecipe FireRecipe(SubstanceData s) =>
            s != null && s.fire != null ? s.fire : Lib == null ? null : BlackSmoke(s) ? Lib.fire : Lib.fireGrey;

        static FxRecipe SeepRecipe(SubstanceData s) =>
            s != null && s.seep != null ? s.seep : Lib == null ? null : BlackSmoke(s) ? Lib.seep : Lib.seepGrey;

        void OnFireStarted(VehicleDamageModel model, VehicleFire fire)
        {
            EnsureVents();
            var b = new Burning { fire = fire, compartment = fire.IsPool ? -1 : CompartmentOf(fire.Position) };
            _burning[fire] = b;
            if (fire.IsPool)
            {
                // Spilled fuel burns on the ground under the hull: already outside.
                float size = Mathf.Clamp(fire.Radius * 0.5f, 0.8f, 2f);
                b.flames.Add((null, Own(SimpleVfx.Fire(transform, fire.Position, size, Vector3.up, recipe: FireRecipe(fire.Substance)))));
                return;
            }
            if (fire.Substance != null && fire.Substance.violent)
            {
                BlowHatches(b);
                _profile.BreachAround(b.compartment);
            }
            Assign(b);
        }

        /// <summary>A violent deflagration in a closed hull: the hatches near it are thrown open and flame jets out of them.</summary>
        void BlowHatches(Burning b)
        {
            VentPicker.Blown(b.fire.Position, VentInfo(b.compartment), _blown);
            var jet = b.fire.Substance != null ? b.fire.Substance.ventJet : null;
            foreach (int i in _blown)
            {
                var vent = _vents[i];
                vent.Hatch.Blow(Random.Range(380f, 700f));
                // The torch lasts as long as the charges burn (~20 s out of a T-72's hatch in youtu.be/gbI1EAPONWs).
                _jets.Add(Own(SimpleVfx.CookOff(vent.transform, vent.transform.position, b.fire.Remaining * Random.Range(1.2f, 2.5f), vent.transform.up, 1f, jet)));
            }
            MuzzleFlame();
        }

        /// <summary>The pressure also finds the gun's bore: a tongue of flame out of the muzzle for a few seconds.</summary>
        void MuzzleFlame()
        {
            var gun = Turret != null ? Turret.Find("Gun") : null;
            if (gun == null || Lib == null || Lib.muzzleJet == null) return;
            // Parented to the turret, not the gun (its transform is the barrel's non-uniform scale).
            _jets.Add(Own(SimpleVfx.CookOff(Turret, gun.TransformPoint(0f, 0f, 0.5f), Random.Range(3f, 6f), gun.forward, 1.5f, Lib.muzzleJet)));
        }

        /// <summary>Puts the fire's flames on its openings (and moves them when hatches open or the turret goes).</summary>
        void Assign(Burning b)
        {
            if (b.ended || b.fire.IsPool) return;
            VentPicker.Share(b.fire.Position, VentInfo(b.compartment), _shares);
            // Drop flames on vents that are no longer chosen.
            for (int i = b.flames.Count - 1; i >= 0; i--)
            {
                var (vent, fx) = b.flames[i];
                if (Chosen(vent)) continue;
                if (fx != null) fx.StopEmitting();
                b.flames.RemoveAt(i);
            }
            var s = b.fire.Substance;
            var recipe = FireRecipe(s);
            foreach (var (index, share) in _shares)
            {
                var vent = _vents[index];
                if (HasFlames(b, vent)) continue;
                b.flames.Add((vent, Own(SimpleVfx.Fire(vent.transform, vent.transform.position, FlameScale(s, share, vent.Area, recipe), vent.transform.up, recipe: recipe))));
            }
            if (_shares.Count == 0)
            {
                if (b.seep == null || !b.seep.isActiveAndEnabled)
                {
                    int i = VentPicker.Seep(b.fire.Position, VentInfo(b.compartment));
                    if (i >= 0) b.seep = Own(SimpleVfx.Seep(_vents[i].transform, _vents[i].transform.position, BlackSmoke(s), recipe: SeepRecipe(s)));
                }
            }
            else if (b.seep != null)
            {
                b.seep.StopEmitting();
                b.seep = null;
            }
        }

        /// <summary>
        /// Size of the flames through an opening: the fire's heat release (its share of it) through the opening, capped by
        /// the opening's ventilation limit, gives the flame height (Heskestad); the fire recipe is authored for a flame
        /// <see cref="FxRecipe.referenceSize"/> m tall.
        /// </summary>
        public static float FlameScale(SubstanceData s, float share, float area, FxRecipe recipe)
        {
            float q = (s != null ? s.heatReleaseMW : 4f) * share;
            float h = FireScaling.OpeningFlameHeight(q, area);
            return Mathf.Clamp(h / (recipe != null ? Mathf.Max(0.1f, recipe.referenceSize) : 2.6f), 0.3f, 2.2f);
        }

        void OnFireEnded(VehicleDamageModel model, VehicleFire fire)
        {
            if (!_burning.TryGetValue(fire, out var b) || b.ended) return;
            // Flames die down, the smoke keeps rising for a while.
            b.ended = true;
            foreach (var (_, fx) in b.flames) if (fx != null) fx.StopEmitting(20f);
            if (b.seep != null) b.seep.StopEmitting(20f);
        }

        /// <summary>
        /// A jet atomised fuel: the mist fireball bursts out of the nearest opening (the jet's own hole, usually), sized by
        /// the flashing mass (CCPS fireball diameter of the tank's contents times its mist share).
        /// </summary>
        void OnFlash(VehicleDamageModel model, VehicleModule tank)
        {
            EnsureVents();
            var s = tank.Substance;
            var recipe = s != null && s.flash != null ? s.flash : Lib != null ? Lib.fuel : null;
            if (recipe == null) return;
            float mass = s != null ? s.Mass(tank.Remaining) * s.mistFraction : 0.2f;
            float scale = Mathf.Clamp(FireScaling.FireballDiameter(mass) / Mathf.Max(0.1f, recipe.referenceSize), 0.3f, 3f);
            Vector3 at = tank.transform.position;
            var info = VentInfo(CompartmentOf(at));
            int i = VentPicker.Nearest(at, info);
            if (i < 0) i = VentPicker.Seep(at, info);
            Vector3 p = i >= 0 ? info[i].position + info[i].normal * 0.6f : at;
            SimpleVfx.Play(recipe, p, Quaternion.identity, null, scale);
        }

        // ---------------------------------------------------------------- destruction

        void OnDestroyed(VehicleDamageModel model, DestructionCause cause)
        {
            EnsureVents();
            _wrecked = true;
            // A catastrophic kill scorches the whole vehicle at once; a burn-out has charred it already.
            if (cause == DestructionCause.AmmoDetonation) _char = Mathf.Max(_char, 0.55f);
            Paint();
            if (cause == DestructionCause.AmmoDetonation) Detonate(model.LastDetonation);
            // Whatever still burns goes on for a few minutes, then dies down; the smoke outlives the flames.
            foreach (var b in _burning.Values)
            {
                if (b.ended) continue;
                foreach (var (_, fx) in b.flames) if (fx != null) SimpleVfx.StopAfter(fx, wreckBurn * Random.Range(0.5f, 1f));
                if (b.seep != null) SimpleVfx.StopAfter(b.seep, wreckBurn);
            }
            if (cause == DestructionCause.BurnedOut)
            {
                var smoke = Own(SimpleVfx.Seep(transform, transform.position + Vector3.up * 2f, true, 1.6f));
                if (smoke != null)
                {
                    _wreck.Add(smoke);
                    SimpleVfx.StopAfter(smoke, wreckBurn);
                }
            }
        }

        /// <summary>Where the blast and the burning contents leave the hull: the turret ring, else the top over the module.</summary>
        Vector3 Outlet(Vector3 inside)
        {
            if (_ring != null) return _ring.transform.position;
            if (_profile == null) return inside + Vector3.up;
            return VehicleFxSetup.TopSurface(transform, inside).point;
        }

        /// <summary>
        /// The vehicle blows apart: a fireball sized by what went (explosive equivalent through the detonation fireball fit,
        /// or burning propellant through Baker's fireball), the turret thrown if the mass was confined and violent, every
        /// bulkhead breached, then the contents cook off by kind.
        /// </summary>
        void Detonate(Detonation d)
        {
            var s = d.substance;
            Vector3 at = Outlet(d.module != null ? d.module.transform.position : transform.position + Vector3.up) + Vector3.up * 0.4f;
            FxRecipe recipe;
            float scale;
            if (s == null)
            {
                recipe = Lib != null ? Lib.ammo : null;
                scale = 2.5f;
            }
            else if (d.explosive)
            {
                recipe = s.detonation != null ? s.detonation : Lib != null ? Lib.ammo : null;
                scale = FireScaling.DetonationDiameter(d.amount * s.equivalentPerUnit) / RefSize(recipe);
            }
            else
            {
                recipe = s.flash != null ? s.flash : Lib != null ? Lib.ammo : null;
                scale = FireScaling.PropellantFireballDiameter(s.Mass(d.amount)) / RefSize(recipe);
            }
            scale = Mathf.Clamp(scale, 0.5f, 4f);
            SimpleVfx.Play(recipe, at, Quaternion.identity, null, scale);
            for (int i = 0; i < _profile.compartments.Length; i++) _profile.BreachAround(i, 10f);
            if (s == null || d.explosive || s.violent) TossTurret(scale);
            if (_ring != null) _ring.ForceOpen();
            // Jets out of the turret's hatches go with it: the fire now vents through the ring.
            foreach (var j in _jets) if (j != null && j.isActiveAndEnabled && !j.transform.IsChildOf(transform)) j.StopEmitting(3f);
            _heat = 1f;
            _jetOutlets.Clear();
            foreach (var m in _model.Modules) CookOff(m, d);
            SimpleVfx.ScatterFires(transform.position, Random.Range(3, 6), 9f);
            foreach (var b in _burning.Values) Assign(b);
            // The hull burns out: its fuel (or paint, rubber and wiring) through the open ring.
            SubstanceData fuel = null;
            foreach (var m in _model.Modules) if (m.Substance != null && m.Substance.kind == SubstanceClass.LiquidFuel && m.Load > 0f) { fuel = m.Substance; break; }
            var wreck = Own(SimpleVfx.Fire(transform, Outlet(transform.position + Vector3.up), 1.8f, Vector3.up, recipe: FireRecipe(fuel)));
            if (wreck != null)
            {
                _wreck.Add(wreck);
                _wreckFlames.Add(wreck);
                SimpleVfx.StopAfter(wreck, wreckBurn);
            }
        }

        readonly List<Vector3> _jetOutlets = new List<Vector3>();

        static float RefSize(FxRecipe r) => r != null ? Mathf.Max(0.1f, r.referenceSize) : 6.5f;

        /// <summary>What a module's leftover contents do in the wreck, by substance class.</summary>
        void CookOff(VehicleModule m, Detonation d)
        {
            if (m.HighExplosive && !(d.explosive && d.module == m)) StartCoroutine(Bangs(m));
            var s = m.Substance;
            if (s == null || m.Load <= 0f) return;
            Vector3 p = m.transform.position;
            switch (s.kind)
            {
                case SubstanceClass.GunPropellant:
                {
                    // Charges keep burning after the blast: a flame column out of the open turret ring (one per opening:
                    // racks venting through the same ring feed the same column).
                    Vector3 at = Outlet(p);
                    if (_jetOutlets.Exists(o => (o - at).sqrMagnitude < 1f)) break;
                    _jetOutlets.Add(at);
                    _jets.Add(Own(SimpleVfx.CookOff(transform, at, s.burnSeconds * Random.Range(1.2f, 2f), Vector3.up, 1.8f, s.ventJet)));
                    break;
                }
                case SubstanceClass.RocketMotor:
                    StartCoroutine(Rockets(m, s));
                    break;
                case SubstanceClass.CanisterMissile:
                    StartCoroutine(Canisters(m, s));
                    break;
                case SubstanceClass.SmallArms:
                    if (s.crackleSeconds > 0f)
                    {
                        var fx = Own(SimpleVfx.Play(s.fire, Outlet(p), Quaternion.identity, transform));
                        SimpleVfx.StopAfter(fx, s.crackleSeconds);
                        if (fx != null) SimpleAudio.Sustain(s.cookOffSound, fx.transform, s.crackleSeconds, 1f);
                    }
                    break;
            }
        }

        /// <summary>Shells left in a rack go off one by one, with delays (each a single shell's fireball).</summary>
        IEnumerator Bangs(VehicleModule m)
        {
            var s = m.Explosive;
            var recipe = s.chainBang != null ? s.chainBang : s.detonation != null ? s.detonation : Lib != null ? Lib.ammo : null;
            int n = Mathf.Min(s.chainCount, Mathf.CeilToInt(m.ExplosiveCount * m.Load));
            float scale = Mathf.Clamp(FireScaling.DetonationDiameter(s.equivalentPerUnit) / RefSize(recipe), 0.3f, 2f);
            yield return new WaitForSeconds(Random.Range(1f, 3f));
            for (int i = 0; i < n; i++)
            {
                if (m == null) yield break;
                SimpleVfx.Play(recipe, m.transform.position + Random.insideUnitSphere * 0.8f + Vector3.up * 0.5f, Quaternion.identity, null,
                    scale * Random.Range(0.7f, 1f));
                yield return new WaitForSeconds(Random.Range(0.3f, Mathf.Max(0.4f, s.chainSpacing)));
            }
        }

        /// <summary>
        /// Rocket motors lit by the fire: some rockets break loose and fly off erratically, the rest burn where they lie with
        /// dense white smoke.
        /// </summary>
        IEnumerator Rockets(VehicleModule m, SubstanceData s)
        {
            int count = Mathf.CeilToInt(m.Remaining);
            int fly = Mathf.Min(maxFlyingRockets, Mathf.RoundToInt(count * s.flyOffShare));
            var burn = Own(SimpleVfx.Fire(transform, m.transform.position, 1.2f, Vector3.up, recipe: FireRecipe(s)));
            if (burn != null)
            {
                _wreck.Add(burn);
                _wreckFlames.Add(burn);
                SimpleVfx.StopAfter(burn, s.burnSeconds);
            }
            var box = m.GetComponent<BoxCollider>();
            for (int i = 0; i < fly; i++)
            {
                yield return new WaitForSeconds(Random.Range(0.2f, 1.6f));
                if (m == null) yield break;
                Vector3 local = box != null ? box.center + Vector3.Scale(box.size, new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f)) : Vector3.zero;
                Vector3 dir = Quaternion.AngleAxis(Random.Range(-25f, 25f), Random.onUnitSphere) * m.transform.forward;
                FxRocket.Launch(m.transform.TransformPoint(local), dir, s.ventJet);
            }
        }

        /// <summary>A missile burning in its sealed canister blows out through both ends (front cap first, motor out the back).</summary>
        IEnumerator Canisters(VehicleModule m, SubstanceData s)
        {
            int count = Mathf.Min(4, Mathf.CeilToInt(m.Remaining));
            var box = m.GetComponent<BoxCollider>();
            float half = box != null ? box.size.z * 0.5f * m.transform.lossyScale.z : 2f;
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForSeconds(Random.Range(0.5f, 3f));
                if (m == null) yield break;
                Vector3 c = m.transform.position, axis = m.transform.forward;
                SimpleVfx.Play(s.flash, c + axis * half, Quaternion.FromToRotation(Vector3.up, axis), null, 0.6f);
                _jets.Add(Own(SimpleVfx.CookOff(transform, c + axis * half, s.burnSeconds * 0.5f, axis, 1f, s.ventJet)));
                _jets.Add(Own(SimpleVfx.CookOff(transform, c - axis * half, s.burnSeconds, -axis, 1.4f, s.ventJet)));
            }
        }

        /// <summary>
        /// Keeps a reference to a pooled effect only while it is ours: the pool may recycle it for someone else (a
        /// barrage past the cap), and then it is dropped from every list here.
        /// </summary>
        FxEffect Own(FxEffect fx)
        {
            if (fx != null) fx.Released += Forget;
            return fx;
        }

        void Forget(FxEffect fx)
        {
            foreach (var b in _burning.Values)
            {
                for (int i = b.flames.Count - 1; i >= 0; i--)
                    if (b.flames[i].fx == fx) b.flames.RemoveAt(i);
                if (b.seep == fx) b.seep = null;
            }
            _jets.Remove(fx);
            _wreck.Remove(fx);
            _wreckFlames.Remove(fx);
        }

        bool Chosen(FireVent vent)
        {
            foreach (var s in _shares) if (_vents[s.index] == vent) return true;
            return false;
        }

        static bool HasFlames(Burning b, FireVent vent)
        {
            foreach (var f in b.flames) if (f.vent == vent) return true;
            return false;
        }

        void TossTurret(float scale)
        {
            var turret = Turret;
            if (turret == null || turret.GetComponent<Rigidbody>() != null) return;
            turret.SetParent(null, true);
            var rb = turret.gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f;
            // A bigger blast throws it harder (abstract: square root of the fireball scale against a T-72's).
            float speed = _profile.turretTossSpeed * Mathf.Clamp(Mathf.Sqrt(scale / 2.5f), 0.6f, 1.4f);
            rb.AddForce((Vector3.up + Random.insideUnitSphere * 0.3f) * speed, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            bool burning = false;
            foreach (var b in _burning.Values) if (!b.ended) { burning = true; break; }
            if (burning && _char < 1f)
            {
                _char = Mathf.Min(1f, _char + Time.deltaTime / charTime);
                if (Time.time >= _nextPaint) Paint();
            }
            if (Time.time >= _nextUpdate && Time.deltaTime > 0f)
            {
                _nextUpdate = Time.time + 0.5f;
                Tick(burning);
            }
            if (_spill == null) return;
            float d = 5f * Mathf.Sqrt(_model.TotalSpilled);
            _spill.localScale = new Vector3(d, 0.005f, d);
        }

        void Tick(bool burning)
        {
            // Vents open as hatches blow and the turret leaves: flames follow.
            if (!_wrecked) foreach (var b in _burning.Values) Assign(b);
            Vector3 hot = transform.position;
            float flames = 0f;
            foreach (var b in _burning.Values)
                foreach (var (_, fx) in b.flames)
                {
                    if (fx == null || !fx.isActiveAndEnabled) continue;
                    SimpleVfx.UpdateWind(fx);
                    flames += fx.Scale;
                    hot = fx.transform.position;
                }
            foreach (var fx in _wreck)
            {
                if (fx == null || !fx.isActiveAndEnabled) continue;
                SimpleVfx.UpdateWind(fx);
                if (_wreckFlames.Contains(fx)) { flames += fx.Scale; hot = fx.transform.position; }
            }
            // The smoke column downwind of the fire hides what is behind it (two stacked spheres, density abstract).
            if (flames > 0.1f)
            {
                Vector3 drift = Wind.Current;
                SmokeVolumes.Set(_smokeId, hot + drift * 1.5f + Vector3.up * 5f, 5f + flames * 2f, 0.45f, Time.time + 1.5f);
                SmokeVolumes.Set(_smokeId + 1, hot + drift * 4f + Vector3.up * 13f, 8f + flames * 2f, 0.3f, Time.time + 1.5f);
            }
            // The hull heats through within ~20 s of fire and cools over minutes (abstract time constants).
            float target = burning || flames > 0.1f ? 1f : 0f;
            _heat += (target - _heat) * (1f - Mathf.Exp(-0.5f / (target > _heat ? 20f : 150f)));
            if (_heatSig != null && _heatSig.Active) _heatSig.SetFire(_heat, hot);
            // Openings glow from the fire inside.
            float glow = Mathf.Clamp01(flames);
            if (Mathf.Abs(glow - _glow) > 0.02f)
            {
                _glow = glow;
                _glowBlock ??= new MaterialPropertyBlock();
                foreach (var v in _vents)
                {
                    if (v == null || v.Glow == null) continue;
                    v.Glow.GetPropertyBlock(_glowBlock);
                    _glowBlock.SetColor(EmissionColor, new Color(6f, 2.2f, 0.6f) * (v.Open ? _glow : 0f));
                    v.Glow.SetPropertyBlock(_glowBlock);
                }
            }
        }

        // ---------------------------------------------------------------- decoys, leaks, paint

        void OnFirstStruck(VehicleDamageModel model)
        {
            if (model.IsDecoy) StartCoroutine(Deflate());
        }

        /// <summary>Inflatable decoy sags and collapses sideways.</summary>
        IEnumerator Deflate()
        {
            Vector3 scale = transform.localScale;
            Quaternion rot = transform.rotation;
            Quaternion slump = rot * Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 3f);
                transform.localScale = new Vector3(scale.x * (1f + 0.15f * k), scale.y * Mathf.Lerp(1f, 0.12f, k), scale.z);
                transform.rotation = Quaternion.Slerp(rot, slump, k);
                yield return null;
            }
        }

        /// <summary>Dark stain of leaked fuel spreading under the vehicle.</summary>
        void OnLeak(VehicleDamageModel model, VehicleModule tank)
        {
            if (_spill != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "FuelSpill";
            Object.Destroy(go.GetComponent<Collider>());
            if (_spillMat == null)
            {
                _spillMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _spillMat.SetColor(BaseColor, new Color(0.05f, 0.045f, 0.04f));
            }
            go.GetComponent<Renderer>().sharedMaterial = _spillMat;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            go.transform.localScale = Vector3.zero;
            _spill = go.transform;
        }

        float _char, _nextPaint;
        readonly List<(Renderer r, int id, Color paint)> _paint = new List<(Renderer, int, Color)>();
        MaterialPropertyBlock _block;

        /// <summary>Tints every painted renderer towards soot, then rust, by how long the vehicle has burned.</summary>
        void Paint()
        {
            _nextPaint = Time.time + 0.5f;
            // At night the thermal look owns the hull's colour.
            if (_heatSig != null && _heatSig.Active) return;
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r.sharedMaterial == null || r.GetComponentInParent<FxEffect>() != null) continue;
                    int id = r.sharedMaterial.HasProperty(BaseColor) ? BaseColor : r.sharedMaterial.HasProperty(GltfBaseColor) ? GltfBaseColor : -1;
                    if (id >= 0) _paint.Add((r, id, r.sharedMaterial.GetColor(id)));
                }
            }
            // Soot builds first; rust takes over in the second half as the steel stays hot.
            var burnt = Color.Lerp(Soot, Rust, Mathf.InverseLerp(0.4f, 1f, _char));
            float amount = Mathf.SmoothStep(0f, 0.85f, _char);
            foreach (var (r, id, paint) in _paint)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                var c = Color.Lerp(paint, burnt * paint.maxColorComponent * 2f, amount);
                c.a = paint.a;
                _block.SetColor(id, c);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
