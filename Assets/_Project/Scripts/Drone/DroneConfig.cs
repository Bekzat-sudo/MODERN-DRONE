using UnityEngine;

namespace ModernDrone.Drone
{
    /// <summary>
    /// Flight parameters of one drone type. Values are gameplay tuning numbers for a generic
    /// racing-style quad; presets (light / medium / heavy) are separate assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Drone Config", fileName = "DroneConfig")]
    public sealed class DroneConfig : ScriptableObject
    {
        [Header("Mass")]
        [Min(0.1f)] public float frameMass = 0.8f;
        [Min(0f)] public float payloadMass = 0.6f;

        [Header("Geometry & inertia")]
        [Tooltip("Motor-to-motor diagonal distance, m.")]
        [Min(0.05f)] public float motorDiagonal = 0.3f;
        [Tooltip("Principal moments of inertia (pitch, yaw, roll), kg*m^2.")]
        public Vector3 inertia = new Vector3(0.025f, 0.045f, 0.025f);
        public Vector3 colliderSize = new Vector3(0.3f, 0.08f, 0.3f);

        [Header("Propulsion")]
        [Tooltip("Thrust-to-weight ratio at full throttle and full battery.")]
        [Min(1.1f)] public float thrustToWeight = 4f;
        [Tooltip("Motor spool-up / spool-down time constants, s.")]
        public float motorTauUp = 0.03f;
        public float motorTauDown = 0.045f;
        [Tooltip("Reaction torque per newton of thrust (yaw authority), m.")]
        public float yawTorqueCoefficient = 0.016f;
        [Tooltip("Axial airspeed at which propeller thrust drops to zero, m/s.")]
        public float propPitchSpeed = 40f;

        [Header("Aerodynamics")]
        [Tooltip("Drag area Cd*A along body x, y, z, m^2.")]
        public Vector3 dragArea = new Vector3(0.012f, 0.045f, 0.014f);
        public float angularDrag = 0.002f;

        [Header("Battery")]
        [Range(1, 8)] public int cells = 6;
        public float capacityMah = 3000f;
        public float internalResistance = 0.05f;
        [Tooltip("Current drawn by one motor at full output, A.")]
        public float maxCurrentPerMotor = 35f;

        [Header("Rates (Betaflight)")]
        public AxisRates rollRates = new AxisRates(1.0f, 0.7f, 0f);
        public AxisRates pitchRates = new AxisRates(1.0f, 0.7f, 0f);
        public AxisRates yawRates = new AxisRates(1.0f, 0.5f, 0f);

        [Header("Rate controller (output: angular accel, rad/s^2)")]
        public PidGains pitchPid = new PidGains(18f, 6f, 0f, 40f);
        public PidGains rollPid = new PidGains(18f, 6f, 0f, 40f);
        public PidGains yawPid = new PidGains(12f, 4f, 0f, 20f);

        [Header("Angle mode")]
        [Range(10f, 80f)] public float maxAngle = 55f;
        public float angleGain = 8f;
        public float angleMaxRate = 400f;

        [Header("Camera")]
        [Range(0f, 60f)] public float cameraTilt = 25f;
        [Range(60f, 160f)] public float cameraHorizontalFov = 118f;

        [Header("Damage")]
        [Tooltip("Impact speed above which the drone is destroyed, m/s.")]
        public float crashSpeed = 7f;

        public float TotalMass => frameMass + payloadMass;
        public float Weight => TotalMass * Core.SimConstants.Gravity;
        public float MaxThrustPerMotor => Weight * thrustToWeight / QuadMixer.MotorCount;
        public float ArmOffset => motorDiagonal / (2f * Mathf.Sqrt(2f));

        /// <summary>Throttle that hovers at full battery with thrust = throttle^2 * max.</summary>
        public float HoverThrottle => Mathf.Sqrt(1f / thrustToWeight);
    }
}
