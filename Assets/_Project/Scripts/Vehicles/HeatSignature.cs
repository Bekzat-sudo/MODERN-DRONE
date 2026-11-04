using System.Collections.Generic;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Thermal look for night scenes: vehicle surfaces use an unlit material whose brightness stands in for
    /// temperature, hottest around the engine / generator. Decoys get no signature and stay cold. A fire heats the
    /// hull until it saturates the camera (burning vehicles and flames read white-hot, lightpath.com LWIR notes); after
    /// the fire the steel cools over minutes (the cooling time is abstract).
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class HeatSignature : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Material heatMaterial;
        [Tooltip("Brightness of the hull (before the thermal camera gain).")]
        [SerializeField] float baseHeat = 0.09f;
        [SerializeField] float hotHeat = 0.14f;
        [SerializeField] float hotRadius = 2.5f;
        [Tooltip("Brightness of a hull heated through by a fire.")]
        [SerializeField] float fireHeat = 0.7f;

        readonly List<(Renderer r, float heat, float near)> _renderers = new List<(Renderer, float, float)>();
        MaterialPropertyBlock _block;
        float _fire = -1f;
        Vector3 _fireAt;

        public bool Active => heatMaterial != null && _renderers.Count > 0;

        public void Configure(Material material) => heatMaterial = material;

        void Start()
        {
            if (heatMaterial == null) return;
            var hotSpots = new List<Vector3>();
            foreach (var m in GetComponent<VehicleDamageModel>().Modules)
                if (m.Type == ModuleType.Engine || m.Type == ModuleType.Generator || m.Type == ModuleType.Transmission)
                    hotSpots.Add(m.transform.position);

            _block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                float nearest = float.MaxValue;
                foreach (var p in hotSpots) nearest = Mathf.Min(nearest, Vector3.Distance(r.bounds.center, p));
                float heat = Mathf.Lerp(hotHeat, baseHeat, Mathf.InverseLerp(0f, hotRadius, nearest));

                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = heatMaterial;
                r.sharedMaterials = mats;
                _renderers.Add((r, heat, 0f));
            }
            Apply();
        }

        /// <summary>
        /// How hot the fire has made the hull, 0..1 (1 = burning through), and where it burns (parts nearest the fire
        /// are hottest).
        /// </summary>
        public void SetFire(float level, Vector3 at)
        {
            level = Mathf.Clamp01(level);
            if (Mathf.Abs(level - _fire) < 0.01f && (at - _fireAt).sqrMagnitude < 0.25f) return;
            _fire = level;
            _fireAt = at;
            Apply();
        }

        void Apply()
        {
            if (_block == null) return;
            foreach (var (r, heat, _) in _renderers)
            {
                if (r == null) continue;
                float f = Mathf.Max(0f, _fire);
                // Parts by the fire glow first; the whole hull follows as the fire goes on.
                float near = Mathf.Clamp01(1f - Vector3.Distance(r.bounds.center, _fireAt) / 4f);
                float h = Mathf.Lerp(heat, fireHeat, Mathf.Clamp01(f * (0.4f + 0.6f * near) * 1.3f));
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, new Color(h, h, h, 1f));
                r.SetPropertyBlock(_block);
            }
        }
    }
}
