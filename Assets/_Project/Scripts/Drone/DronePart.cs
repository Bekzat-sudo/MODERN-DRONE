using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>Where a part goes on the drone.</summary>
    public enum PartSlot
    {
        Battery,
        Camera,
        Propellers,
        /// <summary>Video transmitter (radio) or fiber-optic spool.</summary>
        VideoLink
    }

    /// <summary>
    /// One selectable drone component. Figures follow public hobby data (sources in the editor catalogue DroneParts);
    /// numbers marked there as estimates are gameplay values.
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Drone Part", fileName = "DronePart")]
    public sealed class DronePart : ScriptableObject
    {
        public PartSlot slot;
        public string displayName = "Part";
        [TextArea] public string summary = "";
        [Tooltip("Mass of the part, kg.")]
        [Min(0f)] public float mass;
        [Tooltip("Visual of the part (hangar).")]
        public GameObject model;

        [Header("Battery")]
        [Range(1, 8)] public int cells = 6;
        public float capacityMah;
        [Tooltip("Pack internal resistance, ohm (voltage sag under load).")]
        public float internalResistance;

        [Header("Camera")]
        public bool thermal;
        public Vector2Int thermalResolution;
        [Tooltip("Horizontal field of view of a thermal lens, deg (analog cameras use the pilot setting).")]
        public float horizontalFov;

        [Header("Propellers (relative to the model's stock props)")]
        public float thrustScale = 1f;
        public float currentScale = 1f;
        [Tooltip("Axial speed where thrust fades (prop pitch): higher = faster top speed.")]
        public float pitchSpeedScale = 1f;
        [Tooltip("Motor spool-up / spool-down time (heavier props respond slower).")]
        public float spinUpScale = 1f;

        [Header("Video link")]
        public bool fiber;
        [Tooltip("Radio: video range multiplier against the 5.8 GHz 800 mW baseline.")]
        public float videoRangeScale = 1f;
        [Tooltip("Radio: signal kept per tree / structure in the way (lower frequencies pass foliage better).")]
        [Range(0f, 1f)] public float foliageBlock = 0.8f;
        [Tooltip("Fiber: cable on the spool, m.")]
        public float spoolLength;
    }

    /// <summary>Turns a drone model plus a set of parts into the runtime flight parameters.</summary>
    public static class DroneBuild
    {
        /// <summary>
        /// Runtime config: airframe plus part masses. Motor thrust is the model's stock thrust scaled by the props, so
        /// heavier parts lower thrust-to-weight instead of adding thrust. Fiber spool mass is carried by the link.
        /// </summary>
        public static DroneConfig Compose(DroneConfig model, IEnumerable<DronePart> parts)
        {
            var cfg = Object.Instantiate(model);
            cfg.name = model.name + " (Runtime)";
            float thrust = model.MaxThrustPerMotor;
            float mass = model.airframeMass;
            foreach (var p in parts)
            {
                if (p == null) continue;
                if (!(p.slot == PartSlot.VideoLink && p.fiber)) mass += p.mass;
                if (p.slot == PartSlot.Battery)
                {
                    cfg.cells = p.cells;
                    cfg.capacityMah = p.capacityMah;
                    cfg.internalResistance = p.internalResistance;
                }
                else if (p.slot == PartSlot.Propellers)
                {
                    thrust *= p.thrustScale;
                    cfg.maxCurrentPerMotor *= p.currentScale;
                    cfg.propPitchSpeed *= p.pitchSpeedScale;
                    cfg.motorTauUp *= p.spinUpScale;
                    cfg.motorTauDown *= p.spinUpScale;
                }
            }
            cfg.frameMass = mass;
            cfg.thrustToWeight = thrust * QuadMixer.MotorCount / (mass * Core.SimConstants.Gravity);
            return cfg;
        }

        /// <summary>Hover endurance on 80% of the pack, minutes (motor current follows the flight model: V^2 * spin^3).</summary>
        public static float HoverMinutes(DroneConfig cfg, float payloadKg)
        {
            float spin = cfg.HoverThrottleFor(cfg.frameMass + payloadKg);
            // spin^3 * (V/Vfull)^2 = spin * thrust fraction
            float amps = cfg.maxCurrentPerMotor * QuadMixer.MotorCount * spin * cfg.LoadedThrustFraction(spin);
            return amps <= 0f ? 0f : cfg.capacityMah / 1000f * 0.8f / amps * 60f;
        }
    }
}
