using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// Gameplay description of a drone payload. All values are abstract balance numbers
    /// (armor points, damage points), not physical quantities.
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Warhead", fileName = "Warhead")]
    public sealed class WarheadData : ScriptableObject
    {
        public string displayName = "Warhead";
        public WarheadKind kind = WarheadKind.ShapedCharge;

        [Header("Physical (abstract gameplay values)")]
        [Tooltip("Payload mass, kg: loads the drone (lower thrust-to-weight, more inertia).")]
        [Min(0f)] public float mass = 0.5f;
        [Tooltip("Payload length along the frame axis, m.")]
        [Min(0f)] public float length = 0.3f;

        [Header("Jet (shaped charge)")]
        [Tooltip("Armor points the jet can defeat at the point of impact.")]
        public float penetration = 320f;
        [Tooltip("Tandem: the lead charge clears reactive blocks, which then cost nothing.")]
        public bool tandem;
        [Tooltip("Penetration lost per metre of travel after impact.")]
        public float decayPerMeter = 60f;
        public float jetMaxLength = 4f;
        [Tooltip("Damage to each module the jet passes through at full penetration; scales down with the residual jet.")]
        public float jetDamage = 120f;

        [Header("Spall behind armor")]
        public int spallRays = 24;
        [Range(1f, 80f)] public float spallConeAngle = 30f;
        public float spallRange = 3.5f;
        public float spallDamage = 45f;

        [Header("Blast / fragments")]
        public float blastRadius = 1.5f;
        public float blastDamage = 40f;
        [Tooltip("Armor points fragments can defeat; thicker plates shield modules from the blast.")]
        public float fragmentPenetration = 10f;
    }
}
