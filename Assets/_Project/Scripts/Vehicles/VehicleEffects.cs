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

        void Awake()
        {
            _model = GetComponent<VehicleDamageModel>();
            if (turret == null) turret = transform.Find("Turret");
        }

        void OnEnable()
        {
            _model.FireStarted += OnFireStarted;
            _model.Destroyed += OnDestroyed;
            _model.FirstStruck += OnFirstStruck;
        }

        void OnDisable()
        {
            _model.FireStarted -= OnFireStarted;
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

        void OnFireStarted(VehicleDamageModel model, VehicleModule origin)
        {
            Vector3 pos = origin != null ? origin.transform.position : transform.position + Vector3.up;
            _fire = SimpleVfx.Fire(transform, pos + Vector3.up * 0.8f, 1f);
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
            else if (cause == DestructionCause.Crashed && _fire == null)
            {
                _fire = SimpleVfx.Fire(transform, transform.position + Vector3.up * 1.5f, 1.4f);
            }
            else if (cause == DestructionCause.BurnedOut && _fire != null)
            {
                // Flames die down, smoke remains.
                var flames = _fire.transform.Find("Flames");
                if (flames != null) flames.GetComponent<ParticleSystem>().Stop();
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
