using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Visual consequences of vehicle damage: fire, ammo detonation, turret toss, strike marks (HitMarks) and burnt paint.
    /// Fires inside the hull show where they would on a real vehicle: flames leave through open hatches, the turret
    /// ring, engine grilles and holes punched by the jet (<see cref="VentPicker"/>), never through the armour; with every
    /// opening shut, smoke seeps from the seams. Burning propellant blows the hatches near it open and roars out of them
    /// as flame jets; an ammunition detonation throws the turret, tears hatches off and leaves the hull burning out of the
    /// turret ring for minutes (wrecks burn for tens of minutes; the visual burn here is a shortened, abstract 3-6 min).
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

        [SerializeField] Transform turret;
        [SerializeField] float turretTossImpulse = 9f;
        [Tooltip("Seconds of burning for the paint to char fully.")]
        [SerializeField] float charTime = 90f;
        [Tooltip("Seconds a wreck keeps burning and smoking after it is destroyed (visual only, abstract).")]
        [SerializeField] float wreckBurn = 200f;

        static readonly Color Soot = new Color(0.06f, 0.055f, 0.05f);
        static readonly Color Rust = new Color(0.33f, 0.17f, 0.1f);

        sealed class Burning
        {
            public VehicleFire fire;
            public readonly List<(FireVent vent, FxEffect fx)> flames = new List<(FireVent, FxEffect)>();
            public FxEffect seep;
            public bool ended;
        }

        VehicleDamageModel _model;
        HeatSignature _heatSig;
        readonly List<FireVent> _vents = new List<FireVent>();
        readonly List<VentPicker.Vent> _ventInfo = new List<VentPicker.Vent>();
        readonly List<(int index, float share)> _shares = new List<(int, float)>();
        readonly List<int> _blown = new List<int>();
        readonly Dictionary<VehicleFire, Burning> _burning = new Dictionary<VehicleFire, Burning>();
        readonly List<FxEffect> _jets = new List<FxEffect>();
        // Wreck fire and smoke after destruction (not tied to a VehicleFire).
        readonly List<FxEffect> _wreck = new List<FxEffect>();
        static int _nextSmokeId = 1000;
        readonly int _smokeId = _nextSmokeId += 2;
        FireVent _ring;
        bool _ventsBuilt, _wrecked;
        float _heat, _nextUpdate, _glow;
        Transform _spill;
        static Material _spillMat;
        MaterialPropertyBlock _glowBlock;

        public IReadOnlyList<FireVent> Vents { get { EnsureVents(); return _vents; } }

        void Awake()
        {
            _model = GetComponent<VehicleDamageModel>();
            _heatSig = GetComponent<HeatSignature>();
            if (turret == null) turret = transform.Find("Turret");
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

        /// <summary>
        /// Authored openings (hatches from the model) plus generated ones: the turret ring, grilles over the engine,
        /// a seam on the hull top, and open cabs and beds on soft-skinned vehicles.
        /// </summary>
        void EnsureVents()
        {
            if (_ventsBuilt) return;
            _ventsBuilt = true;
            GetComponentsInChildren(true, _vents);
            bool armored = false;
            foreach (var a in GetComponentsInChildren<ArmorElement>(true))
                if (a.Kind == ArmorKind.Plate && a.Value >= 15f) { armored = true; break; }

            if (turret != null)
            {
                _ring = AddVent("Vent_TurretRing", turret.position, Vector3.up, VentKind.TurretRing, 3f);
            }
            foreach (var m in _model.Modules)
            {
                if (m.Type == ModuleType.Engine)
                    AddTopVent("Vent_EngineGrille", m.transform.position, VentKind.Grille, 0.8f);
                else if (!armored && (m.Type == ModuleType.FuelTank || m.Type == ModuleType.ControlStation || m.Type == ModuleType.AmmoRack || m.Type == ModuleType.Generator))
                    AddTopVent("Vent_Open", m.transform.position, VentKind.Open, 1.2f);
            }
            AddTopVent("Vent_Seam", transform.position + Vector3.up, VentKind.Seam, 0.05f);
        }

        FireVent AddVent(string name, Vector3 at, Vector3 normal, VentKind kind, float area, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, true);
            go.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, normal));
            var v = go.AddComponent<FireVent>();
            v.Configure(kind, area);
            _vents.Add(v);
            return v;
        }

        /// <summary>A vent on the vehicle's outer top surface above <paramref name="inside"/> (its own hull colliders).</summary>
        void AddTopVent(string name, Vector3 inside, VentKind kind, float area)
        {
            Vector3 at = inside, normal = Vector3.up;
            var hits = Physics.RaycastAll(inside + Vector3.up * 8f, Vector3.down, 12f,
                GameLayers.VehicleHullMask | GameLayers.ArmorMask, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<VehicleDamageModel>() != _model || h.point.y <= best) continue;
                best = h.point.y;
                at = h.point;
                normal = h.normal;
            }
            if (best == float.MinValue) at = inside + Vector3.up * 0.8f;
            AddVent(name, at + normal * 0.05f, normal, kind, area);
        }

        /// <summary>Jet holes become openings the fire can burn out of.</summary>
        void OnDetonated(StrikeReport report)
        {
            if (report == null) return;
            foreach (var mark in report.marks)
            {
                if (mark.owner != _model || mark.kind != MarkKind.JetHole || mark.surface == null) continue;
                EnsureVents();
                // A ~10 cm hole through the plate (shaped-charge entry holes are a fraction of the charge diameter).
                AddVent("Vent_Hole", mark.point + mark.normal * 0.03f, mark.normal, VentKind.Hole, 0.01f, mark.surface);
            }
        }

        List<VentPicker.Vent> VentInfo()
        {
            _ventInfo.Clear();
            foreach (var v in _vents)
            {
                var info = v != null ? v.Info : default;
                // Openings on a part thrown off the hull (the tossed turret) no longer lead into it.
                if (v == null || !v.transform.IsChildOf(transform)) info.open = false;
                _ventInfo.Add(info);
            }
            return _ventInfo;
        }

        // ---------------------------------------------------------------- fires

        void OnFireStarted(VehicleDamageModel model, VehicleFire fire)
        {
            EnsureVents();
            var b = new Burning { fire = fire };
            _burning[fire] = b;
            if (fire.IsPool)
            {
                // Spilled fuel burns on the ground under the hull: already outside.
                float size = Mathf.Clamp(fire.Radius * 0.5f, 0.8f, 2f);
                b.flames.Add((null, Own(SimpleVfx.Fire(transform, fire.Position, size))));
                return;
            }
            if (Combustion.Of(fire.Kind).violent) BlowHatches(fire);
            Assign(b);
        }

        /// <summary>Propellant flaring up in a closed hull: the hatches near it are thrown open and flame jets out of them.</summary>
        void BlowHatches(VehicleFire fire)
        {
            VentPicker.Blown(fire.Position, VentInfo(), _blown);
            foreach (int i in _blown)
            {
                var vent = _vents[i];
                vent.Hatch.Blow(Random.Range(380f, 700f));
                _jets.Add(Own(SimpleVfx.CookOff(vent.transform, vent.transform.position, Random.Range(5f, 10f), vent.transform.up, 1f)));
            }
        }

        /// <summary>Puts the fire's flames on its openings (and moves them when hatches open or the turret goes).</summary>
        void Assign(Burning b)
        {
            if (b.ended || b.fire.IsPool) return;
            VentPicker.Share(b.fire.Position, VentInfo(), _shares);
            // Drop flames on vents that are no longer chosen.
            for (int i = b.flames.Count - 1; i >= 0; i--)
            {
                var (vent, fx) = b.flames[i];
                if (Chosen(vent)) continue;
                if (fx != null) fx.StopEmitting();
                b.flames.RemoveAt(i);
            }
            bool black = b.fire.Kind != Combustible.Propellant && b.fire.Kind != Combustible.RocketPropellant;
            foreach (var (index, share) in _shares)
            {
                var vent = _vents[index];
                if (HasFlames(b, vent)) continue;
                // Flame size: the fire's heat release through this opening (abstract scale, 1 = a hatch-sized fire).
                float size = Mathf.Clamp(Mathf.Sqrt(b.fire.Intensity * share) * Mathf.Lerp(0.45f, 1.4f, Mathf.Clamp01(vent.Area / 1.2f)), 0.3f, 2.2f);
                b.flames.Add((vent, Own(SimpleVfx.Fire(vent.transform, vent.transform.position, size, vent.transform.up, black))));
            }
            if (_shares.Count == 0)
            {
                if (b.seep == null || !b.seep.isActiveAndEnabled)
                {
                    int s = VentPicker.Seep(b.fire.Position, VentInfo());
                    if (s >= 0) b.seep = Own(SimpleVfx.Seep(_vents[s].transform, _vents[s].transform.position, black));
                }
            }
            else if (b.seep != null)
            {
                b.seep.StopEmitting();
                b.seep = null;
            }
        }

        void OnFireEnded(VehicleDamageModel model, VehicleFire fire)
        {
            if (!_burning.TryGetValue(fire, out var b) || b.ended) return;
            // Flames die down, the smoke keeps rising for a while.
            b.ended = true;
            foreach (var (_, fx) in b.flames) if (fx != null) fx.StopEmitting(20f);
            if (b.seep != null) b.seep.StopEmitting(20f);
        }

        /// <summary>Atomised fuel flashing: the fireball bursts out of the nearest opening (the jet's own hole, usually).</summary>
        void OnFlash(VehicleDamageModel model, Vector3 at, float size)
        {
            EnsureVents();
            var info = VentInfo();
            int i = VentPicker.Nearest(at, info);
            if (i < 0) i = VentPicker.Seep(at, info);
            Vector3 p = i >= 0 ? info[i].position + info[i].normal * 0.6f : at;
            SimpleVfx.Explosion(p, size * 0.6f, ExplosionKind.Fuel);
        }

        void OnDestroyed(VehicleDamageModel model, DestructionCause cause)
        {
            EnsureVents();
            _wrecked = true;
            // A catastrophic kill scorches the whole vehicle at once; a burn-out has charred it already.
            if (cause == DestructionCause.AmmoDetonation) _char = Mathf.Max(_char, 0.55f);
            Paint();
            if (cause == DestructionCause.AmmoDetonation)
            {
                Vector3 ring = _ring != null ? _ring.transform.position : transform.position + Vector3.up * 1.6f;
                SimpleVfx.Explosion(ring + Vector3.up * 0.4f, 2.5f, ExplosionKind.Ammo);
                TossTurret();
                if (_ring != null) _ring.ForceOpen();
                // Jets out of the turret's hatches go with it: the fire now vents through the ring.
                foreach (var j in _jets) if (j != null && j.isActiveAndEnabled && !j.transform.IsChildOf(transform)) j.StopEmitting(3f);
                _heat = 1f;
                // Propellant keeps burning after the blast: a flame column out of the open turret ring, then the wreck burns.
                _jets.Add(Own(SimpleVfx.CookOff(transform, ring, Random.Range(10f, 16f), Vector3.up, 1.8f)));
                SimpleVfx.ScatterFires(transform.position, Random.Range(3, 6), 9f);
                foreach (var b in _burning.Values) Assign(b);
                var wreck = Own(SimpleVfx.Fire(transform, ring, 1.8f, Vector3.up, true));
                _wreck.Add(wreck);
                Stop(wreck, wreckBurn);
            }
            // Whatever still burns goes on for a few minutes, then dies down; the smoke outlives the flames.
            foreach (var b in _burning.Values)
            {
                if (b.ended) continue;
                foreach (var (_, fx) in b.flames) if (fx != null) Stop(fx, wreckBurn * Random.Range(0.5f, 1f));
                if (b.seep != null) Stop(b.seep, wreckBurn);
            }
            if (cause == DestructionCause.BurnedOut)
            {
                var smoke = Own(SimpleVfx.Seep(transform, transform.position + Vector3.up * 2f, true, 1.6f));
                _wreck.Add(smoke);
                Stop(smoke, wreckBurn);
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

        static void Stop(FxEffect fx, float after)
        {
            if (!fx.TryGetComponent(out FxStopAfter s)) s = fx.gameObject.AddComponent<FxStopAfter>();
            s.Arm(after);
        }

        void TossTurret()
        {
            if (turret == null || turret.GetComponent<Rigidbody>() != null) return;
            turret.SetParent(null, true);
            var rb = turret.gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.AddForce((Vector3.up + Random.insideUnitSphere * 0.3f) * turretTossImpulse, ForceMode.VelocityChange);
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
                if (fx.Key == "fire") { flames += fx.Scale; hot = fx.transform.position; }
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
        System.Collections.IEnumerator Deflate()
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
