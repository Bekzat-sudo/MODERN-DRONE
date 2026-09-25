using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>Visual consequences of vehicle damage: fire, ammo detonation, turret toss, burnt paint.</summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class VehicleEffects : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Imported glTF models (glTFast shader graphs).
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");

        [SerializeField] Transform turret;
        [SerializeField] float turretTossImpulse = 9f;
        [SerializeField] float burntDarkness = 0.22f;

        VehicleDamageModel _model;
        GameObject _fire;
        readonly System.Collections.Generic.Dictionary<VehicleFire, GameObject> _fires = new System.Collections.Generic.Dictionary<VehicleFire, GameObject>();
        Transform _spill;
        static Material _spillMat;

        void Awake()
        {
            _model = GetComponent<VehicleDamageModel>();
            if (turret == null) turret = transform.Find("Turret");
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
            if (_spill == null) return;
            float d = 5f * Mathf.Sqrt(_model.TotalSpilled);
            _spill.localScale = new Vector3(d, 0.005f, d);
        }

        void OnDestroyed(VehicleDamageModel model, DestructionCause cause)
        {
            Darken();
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

        void Darken()
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.sharedMaterial == null) continue;
                int id = r.sharedMaterial.HasProperty(BaseColor) ? BaseColor : r.sharedMaterial.HasProperty(GltfBaseColor) ? GltfBaseColor : -1;
                if (id < 0) continue;
                r.GetPropertyBlock(block);
                block.SetColor(id, r.sharedMaterial.GetColor(id) * burntDarkness);
                r.SetPropertyBlock(block);
            }
        }
    }
}
