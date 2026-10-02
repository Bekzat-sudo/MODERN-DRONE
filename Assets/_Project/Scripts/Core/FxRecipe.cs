using System;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>What a layer of an effect draws (each maps to one particle system, light or action in the player).</summary>
    public enum FxLayerKind
    {
        /// <summary>White-hot burst sprite, a few frames.</summary>
        Flash,
        /// <summary>Six-way lit fireball cooling into soot.</summary>
        Fireball,
        /// <summary>Refraction ring of the blast front.</summary>
        ShockRing,
        /// <summary>Stretched incandescent fragments / molten metal, bouncing off ground and hulls.</summary>
        Sparks,
        /// <summary>Long-lived glowing grains thrown out (burning propellant, hot debris).</summary>
        Embers,
        /// <summary>Earth clods thrown up by a ground burst.</summary>
        Clods,
        /// <summary>Six-way lit smoke or dust puffs (burst or continuous column).</summary>
        Smoke,
        /// <summary>Flame tongues (continuous fire) leaning out of the effect's up axis, then rising.</summary>
        Flames,
        /// <summary>Roaring flame jet along the effect's up axis (propellant venting), flaring as charges burn through.</summary>
        Jet,
        /// <summary>Heat shimmer over a fire.</summary>
        Haze,
        /// <summary>Point light: flash, glow of a fireball or flicker of a fire (by <see cref="FxLayer.lightMode"/>).</summary>
        Light
    }

    public enum FxMaterial { Auto, Fireball, Smoke, SmokeHot, Dust, Flame, Flash, Sparks, Embers, Clods }

    public enum FxLightMode { Flash, Glow, Fire }

    /// <summary>
    /// One layer of an effect. Sizes are visible metres and speeds m/s at the recipe's reference size; the player scales
    /// them to the size an event calls for. Values are look choices (abstract) unless a comment says otherwise.
    /// </summary>
    [Serializable]
    public sealed class FxLayer
    {
        public FxLayerKind kind;
        [Tooltip("Visible size of one puff / tongue / sprite, m (lights: range, m).")]
        public float size = 3f;
        [Tooltip("Particles in the opening burst (0 = none).")]
        public int count;
        [Tooltip("Particles per second while emitting (continuous layers).")]
        public float rate;
        [Tooltip("Seconds of continuous emission (0 = only the burst; looping recipes emit until stopped).")]
        public float duration;
        [Tooltip("Particle life, s (lights: duration of a flash or glow, s).")]
        public float lifetime = 2f;
        public float delay;
        [Tooltip("Start speed, m/s, fastest particles (lights: unused).")]
        public float speed = 1f;
        [Tooltip("Slowest start speed, m/s (0 = a quarter of the fastest).")]
        public float speedMin;
        [Tooltip("Gravity on the particles, 1 = falling like a stone, negative = lifting.")]
        public float gravity;
        [Tooltip("Jets: speed the flame slows to, m/s (0 = 2.5, a short tongue). Higher keeps a torch rising.")]
        public float dragLimit;
        [Tooltip("Buoyant rise of smoke and flames, m/s (smoke: on top of the wind drift).")]
        public float rise;
        [Tooltip("Height of the emitter along the effect's up axis, m.")]
        public float offset;
        [Tooltip("Gas colour (smoke albedo), tint; alpha = opacity.")]
        public Color color = Color.white;
        [Tooltip("Six-way gas: peak glow (heat) at birth. Lights: intensity.")]
        public float intensity = 1f;
        [Tooltip("Six-way gas: how sooty it gets as it cools, 0..1.")]
        public float soot;
        [Tooltip("Emitter cone half-angle, deg (0 = sphere; 90 and more = hemisphere).")]
        public float spread;
        [Tooltip("Emitter radius, m.")]
        public float radius = 0.4f;
        public FxMaterial material;
        public FxLightMode lightMode;
        [Tooltip("Particles bounce off terrain and hulls.")]
        public bool collide;
        [Tooltip("Smoke and dust drift with the wind (Wind).")]
        public bool windDrift = true;
        [Tooltip("Earth ring: emitter is a flat circle on the ground, puffs roll outward.")]
        public bool groundRing;
        [Tooltip("Lowest graphics quality that draws this layer (0 = always, 3 = high only).")]
        [Range(0, 3)] public int minQuality;
    }

    /// <summary>
    /// An effect as data: the layers to draw, how big it is at scale 1, and what it does to the world (blast wave for the
    /// scenery, obscuring cloud for line of sight, dazzling flash, scorch decal, physical debris, sound). Played by
    /// ModernDrone.Vehicles.SimpleVfx (pooled, game time: waits while the killcam freezes the world).
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/FX Recipe", fileName = "FxRecipe")]
    public sealed class FxRecipe : ScriptableObject
    {
        public enum Obscures { None, AirBurst, GroundBurst, VehicleBurst, Fuel, Era, Screen }

        [Tooltip("Visible size the layers are authored for, m (e.g. the fireball's diameter). Events scale from it.")]
        public float referenceSize = 3.4f;
        [Tooltip("Keeps emitting until stopped (fires, smoke columns).")]
        public bool looping;
        [Tooltip("Seconds before a one-shot returns to its pool.")]
        public float lifetime = 16f;
        [Tooltip("Pooled instances of this recipe at most (the oldest is recycled past it).")]
        public int poolSize = 4;
        public FxLayer[] layers = new FxLayer[0];

        [Header("World")]
        public bool shockwave;
        public Obscures obscures;
        public bool dazzles;
        [Tooltip("Diameter of the scorch mark on the ground at scale 1, m (0 = none).")]
        public float scorch;
        [Tooltip("Physical fragments thrown at scale 1 (count, speed m/s, size).")]
        public int debris;
        public float debrisSpeed = 15f;
        public float debrisSize = 1f;
        [Tooltip("Strips leaves off nearby crowns.")]
        public bool shakesTrees;

        [Header("Sound")]
        public SoundBank sound;

        /// <summary>Effects quality, 1..3 (ControlSettings.effectsQuality via GraphicsQuality; layers above it are skipped).</summary>
        public static int Quality = 3;

        public SmokeVolumes.Kind ObscurationKind => obscures switch
        {
            Obscures.AirBurst => SmokeVolumes.Kind.AirBurst,
            Obscures.GroundBurst => SmokeVolumes.Kind.GroundBurst,
            Obscures.Fuel => SmokeVolumes.Kind.Fuel,
            Obscures.Era => SmokeVolumes.Kind.Era,
            Obscures.Screen => SmokeVolumes.Kind.Screen,
            _ => SmokeVolumes.Kind.VehicleBurst,
        };
    }
}
