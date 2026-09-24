using UnityEngine;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Materials for the x-ray debrief view. Kept as assets (not Shader.Find) so the shaders are
    /// included in player builds.
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Killcam Assets", fileName = "KillcamAssets")]
    public sealed class KillcamAssets : ScriptableObject
    {
        [Tooltip("Transparent unlit material replacing vehicle armor in x-ray view.")]
        public Material ghost;
        [Tooltip("Unlit material for module boxes; colour set per module.")]
        public Material module;
        [Tooltip("Vertex-colour unlit material for trace lines.")]
        public Material line;

        public Color moduleIntact = new Color(0.25f, 0.8f, 0.35f, 0.25f);
        public Color moduleDamaged = new Color(1f, 0.8f, 0.1f, 0.6f);
        public Color moduleDestroyed = new Color(1f, 0.15f, 0.1f, 0.65f);
    }
}
