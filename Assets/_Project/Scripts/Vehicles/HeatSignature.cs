using System.Collections.Generic;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Thermal look for night scenes: vehicle surfaces use an unlit material whose brightness stands in for
    /// temperature, hottest around the engine / generator. Decoys get no signature and stay cold.
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

        public void Configure(Material material) => heatMaterial = material;

        void Start()
        {
            if (heatMaterial == null) return;
            var hotSpots = new List<Vector3>();
            foreach (var m in GetComponent<VehicleDamageModel>().Modules)
                if (m.Type == ModuleType.Engine || m.Type == ModuleType.Generator || m.Type == ModuleType.Transmission)
                    hotSpots.Add(m.transform.position);

            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                float nearest = float.MaxValue;
                foreach (var p in hotSpots) nearest = Mathf.Min(nearest, Vector3.Distance(r.bounds.center, p));
                float heat = Mathf.Lerp(hotHeat, baseHeat, Mathf.InverseLerp(0f, hotRadius, nearest));

                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = heatMaterial;
                r.sharedMaterials = mats;
                block.SetColor(BaseColor, new Color(heat, heat, heat, 1f));
                r.SetPropertyBlock(block);
            }
        }
    }
}
