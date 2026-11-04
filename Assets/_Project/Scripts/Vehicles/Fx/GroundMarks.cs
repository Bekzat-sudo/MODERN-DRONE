using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Scorched, blackened ground where blasts and wreck fires were: URP decals (the hit-mark atlas' scorch cell)
    /// projected along the ground normal, with a random roll. A ring of 16; the oldest is reused, as URP draws decals
    /// one by one. Diameters are look values from strike photos (a drone warhead blackens a 2-4 m patch; abstract).
    /// </summary>
    public static class GroundMarks
    {
        const int Max = 16;
        const float Depth = 2f;
        static readonly DecalProjector[] _ring = new DecalProjector[Max];
        static int _next;
        static Material _material;

        public static void Place(Vector3 point, Vector3 normal, float diameter)
        {
            if (_material == null) _material = Resources.Load<Material>("Decals/HitMarks");
            if (_material == null) return;
            var d = _ring[_next];
            if (d == null)
            {
                var go = new GameObject("VFX_GroundMark");
                d = go.AddComponent<DecalProjector>();
                d.material = _material;
                d.uvScale = new Vector2(0.5f, 0.5f);
                d.uvBias = new Vector2(0f, 0f); // scorch cell
                d.drawDistance = 900f;
                d.startAngleFade = 70f;
                d.endAngleFade = 88f;
                _ring[_next] = d;
            }
            d.transform.SetPositionAndRotation(point + normal * (Depth * 0.4f),
                Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            d.size = new Vector3(diameter, diameter, Depth);
            d.pivot = Vector3.zero;
            d.fadeFactor = 0.9f;
            _next = (_next + 1) % Max;
        }
    }
}
