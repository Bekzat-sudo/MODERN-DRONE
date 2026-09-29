using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Visual consequences of vehicle damage: fire, ammo detonation, turret toss, strike marks (HitMarks) and burnt paint.
    /// Paint chars while fires burn: first black soot and paint ash, then, as the hot steel oxidises, rust red
    /// (burnt-out vehicles "rust immediately": steel that got hot enough oxidises fast, armorama.com / quora.com notes).
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class VehicleEffects : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Imported glTF models (glTFast shader graphs).
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");

        [SerializeField] Transform turret;
        [SerializeField] float turretTossImpulse = 9f;
        [Tooltip("Seconds of burning for the paint to char fully.")]
        [SerializeField] float charTime = 90f;

        static readonly Color Soot = new Color(0.06f, 0.055f, 0.05f);
        static readonly Color Rust = new Color(0.33f, 0.17f, 0.1f);

        VehicleDamageModel _model;
        GameObject _fire;
        readonly System.Collections.Generic.Dictionary<VehicleFire, GameObject> _fires = new System.Collections.Generic.Dictionary<VehicleFire, GameObject>();
        Transform _spill;
        static Material _spillMat;

        void Awake()
        {
            _model = GetComponent<VehicleDamageModel>();
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
        }

        void OnDisable()
        {
            _model.FireStarted -= OnFireStarted;
            _model.FireEnded -= OnFireEnded;
            _model.LeakStarted -= OnLeak;
            _model.Flash -= OnFlash;
            _model.Destroyed -= OnDestroyed;
            _model.FirstStruck -= OnFirstStruck;
        }

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

        void OnFireStarted(VehicleDamageModel model, VehicleFire fire)
        {
            // Propellant burns as a jet of flame out of the hatches; a fuel pool as a wide, low fire.
            float size = fire.IsPool ? Mathf.Clamp(fire.Radius * 0.5f, 0.8f, 2f) : Mathf.Clamp(fire.Intensity, 0.7f, 2.2f);
            Vector3 pos = fire.IsPool ? fire.Position : fire.Position + Vector3.up * 0.8f;
            var go = SimpleVfx.Fire(transform, pos, size);
            _fires[fire] = go;
            if (_fire == null) _fire = go;
        }

        void OnFireEnded(VehicleDamageModel model, VehicleFire fire)
        {
            // Flames die down, the smoke keeps rising.
            if (!_fires.TryGetValue(fire, out var go) || go == null) return;
            var flames = go.transform.Find("Flames");
            if (flames != null) flames.GetComponent<ParticleSystem>().Stop();
            var roar = go.GetComponent<AudioSource>();
            if (roar != null) roar.Stop();
        }

        void OnFlash(VehicleDamageModel model, Vector3 at, float size) => SimpleVfx.Explosion(at, size * 0.6f);

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

        void Update()
        {
            if (_fires.Count > 0 && _char < 1f)
            {
                _char = Mathf.Min(1f, _char + Time.deltaTime / charTime);
                if (Time.time >= _nextPaint) Paint();
            }
            if (_spill == null) return;
            float d = 5f * Mathf.Sqrt(_model.TotalSpilled);
            _spill.localScale = new Vector3(d, 0.005f, d);
        }

        void OnDestroyed(VehicleDamageModel model, DestructionCause cause)
        {
            // A catastrophic kill scorches the whole vehicle at once; a burn-out has charred it already.
            if (cause == DestructionCause.AmmoDetonation) _char = Mathf.Max(_char, 0.55f);
            Paint();
            if (cause == DestructionCause.AmmoDetonation)
            {
                Vector3 center = turret != null ? turret.position : transform.position + Vector3.up * 1.5f;
                SimpleVfx.Explosion(center, 2.5f);
                TossTurret();
                if (_fire == null)
                    _fire = SimpleVfx.Fire(transform, transform.position + Vector3.up * 1.5f, 1.4f);
            }
            else if (cause == DestructionCause.BurnedOut)
            {
                // Flames die down, smoke remains.
                foreach (var go in _fires.Values)
                {
                    var flames = go != null ? go.transform.Find("Flames") : null;
                    if (flames != null) flames.GetComponent<ParticleSystem>().Stop();
                    var roar = go != null ? go.GetComponent<AudioSource>() : null;
                    if (roar != null) roar.Stop();
                }
            }
        }

        void TossTurret()
        {
            if (turret == null) return;
            turret.SetParent(null, true);
            var rb = turret.gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.AddForce((Vector3.up + Random.insideUnitSphere * 0.3f) * turretTossImpulse, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
        }

        float _char, _nextPaint;
        readonly System.Collections.Generic.List<(Renderer r, int id, Color paint)> _paint = new System.Collections.Generic.List<(Renderer, int, Color)>();
        MaterialPropertyBlock _block;

        /// <summary>Tints every painted renderer towards soot, then rust, by how long the vehicle has burned.</summary>
        void Paint()
        {
            _nextPaint = Time.time + 0.5f;
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r.sharedMaterial == null) continue;
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
