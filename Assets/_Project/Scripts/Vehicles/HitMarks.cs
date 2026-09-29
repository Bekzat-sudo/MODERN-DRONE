using System.Collections.Generic;
using ModernDrone.Damage;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Persistent strike marks on a vehicle: every detonation that reaches it leaves decals projected onto the visual
    /// model and parented to the struck part, so they ride along with a moving vehicle or a tossed turret.
    /// Looks follow real hits: a shaped-charge jet leaves a small entry hole ringed by liner metal and radial soot, or a
    /// shallow crater when the plate stops it; fragmentation pits and chips the paint; blasts scorch it
    /// (globalsecurity.org shaped charge; burnt-vehicle notes, armorama.com). Sizes are gameplay estimates.
    /// </summary>
    [RequireComponent(typeof(VehicleDamageModel))]
    public sealed class HitMarks : MonoBehaviour
    {
        /// <summary>Oldest marks are removed past this (URP decals are drawn one by one).</summary>
        const int MaxMarks = 24;
        const float Depth = 0.7f;

        static Material _material;

        VehicleDamageModel _model;
        readonly Queue<GameObject> _marks = new Queue<GameObject>();

        public int Count => _marks.Count;

        void Awake() => _model = GetComponent<VehicleDamageModel>();
        void OnEnable() => DamageSolver.Detonated += OnDetonated;
        void OnDisable() => DamageSolver.Detonated -= OnDetonated;

        void OnDetonated(StrikeReport report)
        {
            foreach (var mark in report.marks)
                if (mark.owner == _model && mark.surface != null) Place(mark);
        }

        /// <summary>Atlas cell (2x2: hole, crater, scorch, pocks) and projected size, m.</summary>
        static (Vector2 cell, float size) Look(MarkKind kind) => kind switch
        {
            MarkKind.JetHole => (new Vector2(0f, 0.5f), 0.4f),
            MarkKind.JetCrater => (new Vector2(0.5f, 0.5f), 0.35f),
            MarkKind.Scorch => (new Vector2(0f, 0f), 1.5f),
            MarkKind.Pocks => (new Vector2(0.5f, 0f), 1.4f),
            _ => (new Vector2(0f, 0f), 0.9f), // ERA block: its own soot patch
        };

        void Place(SurfaceMark mark)
        {
            if (_material == null) _material = Resources.Load<Material>("Decals/HitMarks");
            if (_material == null) return;
            var (cell, size) = Look(mark.kind);
            var go = new GameObject("HitMark_" + mark.kind);
            go.transform.SetParent(mark.surface, true);
            // The projector looks into the surface along -normal, centred on it; a random roll keeps repeats distinct.
            go.transform.SetPositionAndRotation(mark.point + mark.normal * (Depth * 0.5f),
                Quaternion.LookRotation(-mark.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            var decal = go.AddComponent<DecalProjector>();
            decal.material = _material;
            decal.size = new Vector3(size, size, Depth);
            decal.pivot = new Vector3(0f, 0f, Depth * 0.5f);
            decal.uvScale = new Vector2(0.5f, 0.5f);
            decal.uvBias = cell;
            decal.drawDistance = 600f;
            decal.startAngleFade = 60f; // no smearing onto faces nearly parallel to the projection
            decal.endAngleFade = 85f;
            _marks.Enqueue(go);
            while (_marks.Count > MaxMarks)
            {
                var old = _marks.Dequeue();
                if (old != null) Destroy(old);
            }
        }
    }
}
