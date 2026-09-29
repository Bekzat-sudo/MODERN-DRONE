using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>Scene-wide night / thermal-camera state, set by the scene (e.g. a night mission).</summary>
    public static class NightMode
    {
        /// <summary>The scene is a night scene (thermal cameras are offered in the loadout).</summary>
        public static bool Night { get; set; }

        /// <summary>FPV camera is a thermal imager.</summary>
        public static bool Thermal { get; set; }

        /// <summary>Horizontal FOV of the thermal camera: 384x288 core, 12 um pitch, 4 mm lens ~ 60 deg by default.</summary>
        public static float ThermalHorizontalFov { get; set; } = 60f;

        /// <summary>Sensor resolution of the thermal core.</summary>
        public static Vector2 ThermalResolution { get; set; } = new Vector2(384f, 288f);

        /// <summary>Current thermal gain (driven by the scene AGC).</summary>
        public static float ThermalGain { get; set; } = 10f;

        /// <summary>0..1 non-uniformity drift since the last shutter calibration.</summary>
        public static float ThermalDrift { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Night = false;
            Thermal = false;
            ThermalHorizontalFov = 60f;
            ThermalResolution = new Vector2(384f, 288f);
            ThermalGain = 10f;
            ThermalDrift = 0f;
        }
    }
}
