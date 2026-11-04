using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// Effect materials, built as assets by Modern Drone/Setup/Build VFX Assets into Resources/VFX (so their shaders and
    /// variants ship with the player: materials made at runtime with Shader.Find had their shader stripped from builds).
    /// </summary>
    public static class FxAssets
    {
        static Material _fireball, _smoke, _smokeHot, _dust, _flame, _flash, _sparks, _embers, _clods, _leaves, _ring, _haze, _debris, _glow, _torch;

        public static Material Fireball => Get(ref _fireball, "VFX_Fireball");
        public static Material Smoke => Get(ref _smoke, "VFX_Smoke");
        /// <summary>Smoke straight off a fire: still warm for the thermal camera.</summary>
        public static Material SmokeHot => Get(ref _smokeHot, "VFX_SmokeHot");
        public static Material Dust => Get(ref _dust, "VFX_Dust");
        public static Material Flame => Get(ref _flame, "VFX_Flame");
        /// <summary>A whole jet of flame per sprite (burning charges venting through a hatch).</summary>
        public static Material Torch => Get(ref _torch, "VFX_Torch");
        public static Material Flash => Get(ref _flash, "VFX_Flash");
        public static Material Sparks => Get(ref _sparks, "VFX_Sparks");
        public static Material Embers => Get(ref _embers, "VFX_Embers");
        public static Material Clods => Get(ref _clods, "VFX_Clods");
        public static Material Leaves => Get(ref _leaves, "VFX_Leaves");
        public static Material ShockRing => Get(ref _ring, "VFX_ShockRing");
        public static Material Haze => Get(ref _haze, "VFX_Haze");
        public static Material Debris => Get(ref _debris, "VFX_Debris");
        /// <summary>Inside of an open hatch: dark, glowing orange while the vehicle burns.</summary>
        public static Material HatchGlow => Get(ref _glow, "VFX_HatchGlow");

        static Material Get(ref Material slot, string name)
        {
            if (slot != null) return slot;
            slot = Resources.Load<Material>("VFX/" + name);
            if (slot == null)
            {
                Debug.LogError($"[ModernDrone] Missing effect material Resources/VFX/{name} (run Modern Drone/Setup/Build VFX Assets).");
                slot = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = name };
            }
            return slot;
        }
    }
}
