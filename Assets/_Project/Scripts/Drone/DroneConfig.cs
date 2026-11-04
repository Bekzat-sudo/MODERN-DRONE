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
        [Header("Model")]
        public string displayName = "FPV 7\"";
        [TextArea] public string summary = "";
        [Tooltip("Visual prefab of this drone model (props named Prop_FR/FL/RR/RL), placed under the drone's Visual child.")]
        public GameObject visualPrefab;
        [Tooltip("FPV camera mount: the model's camera lens, in drone space.")]
        public Vector3 cameraPosition = new Vector3(0f, -0.003f, 0.095f);
        [Tooltip("Hangar: where the battery, the VTX antenna and a fiber spool sit on the model, in drone space.")]
        public Vector3 batteryMount = new Vector3(0f, 0.07f, -0.03f);
        public Vector3 antennaMount = new Vector3(0f, 0.06f, -0.15f);
        public Vector3 spoolMount = new Vector3(0f, -0.07f, -0.08f);

        // Defaults describe a 7" long-range class quad. Airframe figures follow public hobby data
        // (AUW, thrust-to-weight classes: unmannedtechshop.co.uk TWR guide, oscarliang.com 7" reviews);
        // The payload comes from the fitted warhead (DroneController.SetPayload).
        [Header("Mass")]
        [Tooltip("Airframe + battery (all-up weight without payload), kg.")]
        [Min(0.1f)] public float frameMass = 1.1f;
        [Tooltip("Frame, motors, ESC and flight controller without the swappable parts (battery, camera, VTX), kg.")]
        [Min(0.05f)] public float airframeMass = 0.47f;
        [Tooltip("Parts this model ships with; frameMass = airframeMass + their masses.")]
        public DronePart[] stockParts = new DronePart[0];
        [Tooltip("Payload axis below the centre of mass, m (adds pitch/roll inertia).")]
        [Min(0f)] public float payloadDepth = 0.05f;

        [Header("Geometry & inertia")]
        [Tooltip("Motor-to-motor diagonal distance, m.")]
        [Min(0.05f)] public float motorDiagonal = 0.3f;
        [Tooltip("Principal moments of inertia (pitch, yaw, roll), kg*m^2.")]
        public Vector3 inertia = new Vector3(0.009f, 0.016f, 0.009f);
        public Vector3 colliderSize = new Vector3(0.3f, 0.08f, 0.3f);

        [Header("Propulsion")]
        [Tooltip("Thrust-to-weight ratio of the bare airframe (no payload) at full throttle and full battery.")]
        [Min(1.1f)] public float thrustToWeight = 6.4f;
        [Tooltip("Motor spool-up / spool-down time constants, s.")]
        public float motorTauUp = 0.03f;
        public float motorTauDown = 0.045f;
        [Tooltip("Reaction torque per newton of thrust (yaw authority), m.")]
        public float yawTorqueCoefficient = 0.016f;
        [Tooltip("Motor velocity constant, rpm per volt (sound: blade-pass frequency).")]
        public float motorKv = 1300f;
        [Tooltip("Blades per propeller.")]
        [Range(2, 6)] public int propBlades = 3;
        [Tooltip("Axial airspeed at which propeller thrust drops to zero, m/s.")]
        public float propPitchSpeed = 40f;

        [Header("Aerodynamics")]
        [Tooltip("Drag area Cd*A along body x, y, z, m^2.")]
        public Vector3 dragArea = new Vector3(0.012f, 0.045f, 0.014f);
        public float angularDrag = 0.002f;

        [Header("Battery")]
        [Range(1, 8)] public int cells = 6;
        public float capacityMah = 5600f;
        public float internalResistance = 0.05f;
        [Tooltip("Current drawn by one motor at full output, A.")]
        public float maxCurrentPerMotor = 35f;

        [Header("Rates (Betaflight Actual Rates; soft cinematic profile, oscarliang.com/rates)")]
        public AxisRates rollRates = new AxisRates(100f, 650f, 0.6f);
        public AxisRates pitchRates = new AxisRates(100f, 650f, 0.6f);
        public AxisRates yawRates = new AxisRates(100f, 500f, 0.5f);
        [Tooltip("Stick setpoint low-pass cutoff, Hz (Betaflight RC smoothing: ~10 Hz cinematic, ~20 Hz typical).")]
        public float setpointCutoffHz = 12f;

        [Header("Rate controller (output: angular accel, rad/s^2)")]
        public PidGains pitchPid = new PidGains(18f, 6f, 0f, 40f);
        public PidGains rollPid = new PidGains(18f, 6f, 0f, 40f);
        public PidGains yawPid = new PidGains(12f, 4f, 0f, 20f);

        [Header("Angle mode")]
        [Range(10f, 80f)] public float maxAngle = 55f;
        public float angleGain = 8f;
        public float angleMaxRate = 400f;

        [Header("Camera")]
        [Tooltip("Stock camera uptilt of this model (its lens as mounted), deg. The pilot setting adds an offset to it.")]
        [Range(-45f, 60f)] public float cameraTilt = 8f;
        [Range(60f, 160f)] public float cameraHorizontalFov = 118f;

        [Header("Fixed wing (FixedWingAero; ignored by quadcopters)")]
        [Tooltip("Aeroplane: wing lift, one pusher prop, launched in the air; the quadcopter fields above are unused.")]
        public bool fixedWing;
        [Tooltip("Control link through a satellite terminal (Starlink): no range limit, not jammed by the EW stations.")]
        public bool satelliteLink;
        public float wingSpan = 2.2f;
        [Tooltip("Wing planform area, m^2.")]
        public float wingArea = 0.55f;
        public float liftAtZeroAlpha = 0.45f;
        [Tooltip("Lift slope, per rad.")]
        public float liftSlope = 5f;
        public float maxLift = 1.2f;
        public float zeroLiftDrag = 0.05f;
        [Tooltip("Span efficiency (Oswald).")]
        public float oswald = 0.8f;
        [Tooltip("Side force per rad of sideslip, per (q S).")]
        public float sideForceSlope = 0.4f;
        [Tooltip("Weathervane moments per rad of angle of attack / sideslip, per (q S), m.")]
        public float pitchStability = 0.3f, yawStability = 0.15f;
        [Tooltip("Propeller static thrust, N, and thrust power (thrust x airspeed at full throttle), W.")]
        public float staticThrust = 60f, thrustPower = 2900f;
        [Tooltip("Launch / cruise airspeed, m/s.")]
        public float cruiseSpeed = 31f;
        [Tooltip("Airspeed at which the control surfaces reach full authority, m/s (authority ~ dynamic pressure).")]
        public float controlSpeed = 25f;

        [Header("Damage")]
        [Tooltip("Impact speed above which the drone is destroyed, m/s.")]
        public float crashSpeed = 7f;

        /// <summary>Propulsion is fixed by motors and props: extra payload lowers the effective thrust-to-weight.</summary>
        public float MaxThrustPerMotor => frameMass * Core.SimConstants.Gravity * thrustToWeight / QuadMixer.MotorCount;
        public float ArmOffset => motorDiagonal / (2f * Mathf.Sqrt(2f));

        /// <summary>Throttle that hovers the bare airframe on a full pack, voltage sag included.</summary>
        public float HoverThrottle => HoverThrottleFor(frameMass);

        /// <summary>Throttle that hovers <paramref name="mass"/> on a full pack under load (1 if it cannot).</summary>
        public float HoverThrottleFor(float mass)
        {
            float need = mass / (frameMass * thrustToWeight);
            if (need >= LoadedThrustFraction(1f)) return 1f;
            float lo = 0f, hi = 1f; // thrust rises with throttle over 0..1 for any realistic pack (sag k < ~1)
            for (int i = 0; i < 20; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (LoadedThrustFraction(mid) < need) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>Thrust-to-weight with <paramref name="mass"/> at full throttle on a full pack, voltage sag included.</summary>
        public float LoadedThrustToWeight(float mass) => frameMass * thrustToWeight * LoadedThrustFraction(1f) / mass;

        /// <summary>
        /// Fraction of the configured maximum thrust at motor command <paramref name="throttle"/> on a full pack under
        /// load: thrust ~ rpm^2 ~ (V * throttle)^2. Motor current follows prop power ~ rpm^3 (P = Cp rho n^3 D^5), so
        /// I ~ V^2 * throttle^3, and the loaded voltage fraction x solves x = 1 - k * throttle^3 * x^2.
        /// </summary>
        public float LoadedThrustFraction(float throttle)
        {
            float a = internalResistance * maxCurrentPerMotor * QuadMixer.MotorCount / (BatteryModel.FullCellVoltage * cells)
                      * throttle * throttle * throttle;
            float x = a < 1e-6f ? 1f : (Mathf.Sqrt(1f + 4f * a) - 1f) / (2f * a);
            return throttle * throttle * x * x;
        }

        /// <summary>
        /// Payload modelled as a uniform rod along the frame axis hanging <see cref="payloadDepth"/> below the centre
        /// of mass (parallel-axis theorem). Returns (pitch, yaw, roll) moments added to <see cref="inertia"/>.
        /// </summary>
        public Vector3 PayloadInertia(float mass, float length)
        {
            float rod = mass * length * length / 12f, offset = mass * payloadDepth * payloadDepth;
            return new Vector3(rod + offset, rod, offset);
        }
    }
}
