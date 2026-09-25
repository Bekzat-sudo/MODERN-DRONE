using System;
using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Drone
{
    public enum ArmBlockReason
    {
        None,
        Throttle,
        Crash,
        Failsafe
    }

    public struct DroneTelemetry
    {
        public bool armed;
        public bool crashed;
        public FlightMode mode;
        public float throttle;
        public float speed;          // m/s
        public float verticalSpeed;  // m/s
        public float altitudeAgl;    // m, ground below; -1 if out of range
        public float heightAboveHome;
        public float distanceHome;
        public float voltage;
        public float cellVoltage;
        public float current;
        public float usedMah;
        public float flightTime;     // s since arming
        public float pitchDeg;       // nose down +
        public float rollDeg;        // right +
        public float headingDeg;     // 0 = north (+z), clockwise
    }

    /// <summary>
    /// Quadcopter rigid-body simulation: per-motor thrust at motor positions, reaction yaw torque,
    /// motor lag, propeller inflow loss, body drag and battery sag.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DroneController : MonoBehaviour
    {
        const float AglProbeDistance = 500f;

        [SerializeField] DroneConfig config;

        Rigidbody _rb;
        FlightController _fc;
        BatteryModel _battery;
        readonly float[] _desired = new float[QuadMixer.MotorCount];
        readonly float[] _spin = new float[QuadMixer.MotorCount];   // normalised motor speed 0..1
        readonly float[] _thrust = new float[QuadMixer.MotorCount];

        DroneInputState _input;
        bool _prevArmSwitch;
        Vector3 _homePosition;
        Quaternion _homeRotation;
        float _armedTime;
        DroneTelemetry _telemetry;

        public DroneConfig Config => config;
        public Rigidbody Body => _rb;
        public bool Armed { get; private set; }
        public bool Crashed { get; private set; }

        /// <summary>Why the last arm attempt was refused (shown on the OSD), until the switch is released.</summary>
        public ArmBlockReason ArmBlock { get; private set; }
        public FlightMode Mode => _input.mode;
        public DroneInputState Input => _input;
        public BatteryModel Battery => _battery;
        public FlightController FlightController => _fc;
        public ReadOnlySpan<float> MotorSpin => _spin;
        public ref readonly DroneTelemetry Telemetry => ref _telemetry;

        /// <summary>When true, FixedUpdate does nothing and the owner calls <see cref="Step"/> (tests, replays).</summary>
        public bool ManualStepping { get; set; }

        public event Action<Collision> OnCrash;
        public event Action OnReset;
        public event Action OnFailsafe;
        public event Action OnShotDown;

        /// <summary>Control link lost long enough to trigger failsafe (drone disarmed and dropped).</summary>
        public bool Failsafe { get; private set; }

        /// <summary>Control latency applied to stick inputs, s (set by the link model).</summary>
        public float InputLatency { get; set; }

        /// <summary>Control link quality 0..1 (set by the link model; 1 when no link model is present).</summary>
        public float LinkQuality { get; private set; } = 1f;

        const float FailsafeThreshold = 0.1f;
        const float FailsafeHoldTime = 0.5f;
        float _linkLostTime;
        float _extraMass;
        float _payloadMass;
        float _payloadLength;
        readonly System.Collections.Generic.Queue<(float time, DroneInputState state)> _inputQueue =
            new System.Collections.Generic.Queue<(float, DroneInputState)>();
        DroneInputState _applied;

        /// <summary>Velocity at the start of the last physics step, before any collision response.</summary>
        public Vector3 PreImpactVelocity { get; private set; }

        /// <summary>Body forward (payload axis) at the start of the last physics step, before any collision response.</summary>
        public Vector3 PreImpactForward { get; private set; } = Vector3.forward;

        /// <summary>Assigns a config at runtime (e.g. tests or loadout selection) and rebuilds the simulation.</summary>
        public void Initialize(DroneConfig cfg)
        {
            config = cfg;
            Setup();
        }

        void Awake()
        {
            if (config != null)
                Setup();
        }

        void Setup()
        {
            // Work on a runtime copy so pilot settings (rates) never modify the shared asset.
            if (Application.isPlaying && !config.name.EndsWith("(Runtime)"))
            {
                config = Instantiate(config);
                config.name += " (Runtime)";
            }
            _rb = GetComponent<Rigidbody>();
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;
            _rb.useGravity = true;
            _rb.maxAngularVelocity = 60f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.centerOfMass = Vector3.zero;
            _rb.inertiaTensorRotation = Quaternion.identity;
            ApplyMass();

            var col = GetComponent<BoxCollider>();
            if (col != null) col.size = config.colliderSize;
            ApplyModel();

            _fc = new FlightController(config);
            _battery = new BatteryModel(config.cells, config.capacityMah, config.internalResistance);
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
        }

        static readonly string[] PropNames = { "Prop_FR", "Prop_FL", "Prop_RR", "Prop_RL" };

        /// <summary>Swaps the Visual child to this config's model and moves the FPV camera onto the model's lens.</summary>
        void ApplyModel()
        {
            var visual = transform.Find("Visual");
            if (visual == null || config.visualPrefab == null) return;
            Transform model = visual.childCount == 1 && visual.GetChild(0).name == config.visualPrefab.name ? visual.GetChild(0) : null;
            if (model == null)
            {
                // Detach before destroying: Destroy is deferred to the end of the frame, and the old model's props
                // (same names) must not be found below.
                for (int i = visual.childCount - 1; i >= 0; i--)
                {
                    var child = visual.GetChild(i);
                    child.SetParent(null, false);
                    if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject);
                }
                model = Instantiate(config.visualPrefab, visual, false).transform;
                model.name = config.visualPrefab.name;
            }
            var parts = model.GetComponentsInChildren<Transform>(true);
            var props = GetComponent<PropVisuals>();
            if (props != null) props.SetProps(System.Array.ConvertAll(PropNames, n => System.Array.Find(parts, t => t.name == n)));
            var cam = GetComponentInChildren<DroneCamera>(true);
            if (cam != null) cam.SetMount(config.cameraPosition);
        }

        /// <summary>Feeds the current control link quality; a lost link triggers failsafe after a short hold.</summary>
        public void SetLinkQuality(float quality) => LinkQuality = Mathf.Clamp01(quality);

        /// <summary>Additional carried mass (e.g. fiber spool), kg. Thrust is unchanged, so agility drops.</summary>
        public void SetExtraMass(float kg)
        {
            _extraMass = Mathf.Max(0f, kg);
            ApplyMass();
        }

        /// <summary>Fitted payload: adds mass and inertia with thrust unchanged, so the drone climbs and turns slower.</summary>
        public void SetPayload(float kg, float length)
        {
            _payloadMass = Mathf.Max(0f, kg);
            _payloadLength = Mathf.Max(0f, length);
            ApplyMass();
        }

        public float TotalMass => config.frameMass + _payloadMass + _extraMass;
        public float HoverThrottle => config.HoverThrottleFor(TotalMass);
        /// <summary>Thrust-to-weight ratio with the current payload at full battery.</summary>
        public float ThrustToWeight => config.frameMass * config.thrustToWeight / TotalMass;

        void ApplyMass()
        {
            if (_rb == null || config == null) return;
            _rb.mass = TotalMass;
            _rb.inertiaTensor = config.inertia + config.PayloadInertia(_payloadMass, _payloadLength);
        }

        /// <summary>Stick input as received by the flight controller: delayed by latency, frozen while the link is down.</summary>
        DroneInputState ReceivedInput()
        {
            if (LinkQuality < FailsafeThreshold) return _applied; // hold the last received command
            if (InputLatency <= 0f) return _applied = _input;
            float cutoff = Time.time - InputLatency;
            while (_inputQueue.Count > 0 && _inputQueue.Peek().time <= cutoff)
                _applied = _inputQueue.Dequeue().state;
            return _applied;
        }

        void UpdateFailsafe(float dt)
        {
            if (!Armed || LinkQuality >= FailsafeThreshold)
            {
                _linkLostTime = 0f;
                return;
            }
            _linkLostTime += dt;
            if (_linkLostTime < FailsafeHoldTime) return;
            Failsafe = true;
            Armed = false;
            OnFailsafe?.Invoke();
        }

        /// <summary>Applies the pilot's rate profile from the control settings.</summary>
        public void ApplyPilotRates(AxisRates roll, AxisRates pitch, AxisRates yaw)
        {
            config.rollRates = roll;
            config.pitchRates = pitch;
            config.yawRates = yaw;
        }

        public void SetHome(Vector3 position, Quaternion rotation)
        {
            _homePosition = position;
            _homeRotation = rotation;
        }

        public void SetInput(in DroneInputState state)
        {
            _input = state;
            _inputQueue.Enqueue((Time.time, state));
            while (_inputQueue.Count > 64) _inputQueue.Dequeue();

            if (state.armSwitch && !_prevArmSwitch && !Armed)
            {
                bool throttleLow = state.throttleAtRest || state.throttle < 0.05f;
                ArmBlock = Crashed ? ArmBlockReason.Crash : Failsafe ? ArmBlockReason.Failsafe : !throttleLow ? ArmBlockReason.Throttle : ArmBlockReason.None;
                if (ArmBlock == ArmBlockReason.None)
                {
                    Armed = true;
                    _armedTime = 0f;
                }
            }
            if (!state.armSwitch)
            {
                Armed = false;
                ArmBlock = ArmBlockReason.None;
            }
            _prevArmSwitch = state.armSwitch;

            if (state.resetPressed)
                ResetToHome();
        }

        public void ResetToHome()
        {
            Armed = false;
            Crashed = false;
            ArmBlock = ArmBlockReason.None;
            Failsafe = false;
            ShotDownCause = null;
            _linkLostTime = 0f;
            _inputQueue.Clear();
            _prevArmSwitch = true; // require the arm switch to be cycled after a reset
            _fc.Reset();
            _battery.Reset();
            Array.Clear(_spin, 0, _spin.Length);
            Array.Clear(_thrust, 0, _thrust.Length);
            _rb.position = _homePosition;
            _rb.rotation = _homeRotation;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            OnReset?.Invoke();
        }

        /// <summary>Why the drone was lost to enemy action (shown to the pilot).</summary>
        public string ShotDownCause { get; private set; }

        /// <summary>Killed by enemy action (interceptor, ...): the drone drops and the sortie is lost.</summary>
        public void ShootDown(string cause = "ДРОН СБИТ")
        {
            if (Crashed) return;
            ShotDownCause = cause;
            MarkDestroyed();
            OnShotDown?.Invoke();
        }

        /// <summary>Ends the flight immediately (payload detonated, shot down...).</summary>
        public void MarkDestroyed()
        {
            Crashed = true;
            Armed = false;
        }

        void FixedUpdate()
        {
            if (!ManualStepping && _rb != null)
                Step(Time.fixedDeltaTime);
        }

        public void Step(float dt)
        {
            PreImpactVelocity = _rb.linearVelocity;
            PreImpactForward = transform.forward;
            Vector3 up = transform.up;
            Vector3 omegaBody = transform.InverseTransformDirection(_rb.angularVelocity);
            float maxThrust = config.MaxThrustPerMotor;

            UpdateFailsafe(dt);
            if (Armed)
            {
                _armedTime += dt;
                var received = ReceivedInput();
                received.mode = _input.mode;
                Vector3 worldUpInBody = transform.InverseTransformDirection(Vector3.up);
                Vector3 alpha = _fc.Compute(received, omegaBody, worldUpInBody, dt);
                Vector3 torque = Vector3.Scale(alpha, config.inertia);
                float t = Mathf.Clamp01(received.throttle);
                float collective = t * t * maxThrust * QuadMixer.MotorCount;
                QuadMixer.Mix(collective, torque, config.ArmOffset, config.yawTorqueCoefficient, maxThrust, _desired);
            }
            else
            {
                _fc.Reset();
                Array.Clear(_desired, 0, _desired.Length);
            }

            // Motors: command is sqrt of desired thrust fraction (thrust ~ rpm^2), first-order lag on speed.
            float available = maxThrust * _battery.ThrustFactor;
            float axialSpeed = Vector3.Dot(_rb.linearVelocity, up);
            float inflow = Mathf.Clamp(1f - Mathf.Max(0f, axialSpeed) / config.propPitchSpeed, 0.05f, 1f);
            float current = 0f;
            float yawTorque = 0f;

            for (int i = 0; i < QuadMixer.MotorCount; i++)
            {
                float cmd = Mathf.Sqrt(Mathf.Clamp01(_desired[i] / maxThrust));
                float tau = cmd > _spin[i] ? config.motorTauUp : config.motorTauDown;
                _spin[i] += (cmd - _spin[i]) * (1f - Mathf.Exp(-dt / tau));

                float thrust = available * _spin[i] * _spin[i] * inflow;
                _thrust[i] = thrust;
                _rb.AddForceAtPosition(up * thrust, transform.TransformPoint(QuadMixer.MotorPosition(i, config.ArmOffset)));

                yawTorque += config.yawTorqueCoefficient * QuadMixer.Spin[i] * thrust;
                current += config.maxCurrentPerMotor * _spin[i] * _spin[i] * _spin[i];
            }
            _rb.AddRelativeTorque(0f, yawTorque, 0f);

            ApplyDrag();
            _battery.Update(current, dt);
        }

        void ApplyDrag()
        {
            Vector3 v = transform.InverseTransformDirection(_rb.linearVelocity);
            float q = 0.5f * SimConstants.AirDensitySeaLevel;
            Vector3 drag = new Vector3(
                -q * config.dragArea.x * v.x * Mathf.Abs(v.x),
                -q * config.dragArea.y * v.y * Mathf.Abs(v.y),
                -q * config.dragArea.z * v.z * Mathf.Abs(v.z));
            _rb.AddRelativeForce(drag);

            Vector3 w = _rb.angularVelocity;
            _rb.AddTorque(-config.angularDrag * w.magnitude * w);
        }

        void Update()
        {
            if (_rb == null) return;

            Vector3 pos = transform.position;
            Vector3 vel = _rb.linearVelocity;
            float agl = -1f;
            if (Physics.Raycast(pos, Vector3.down, out var hit, AglProbeDistance, GameLayers.TerrainMask | GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                agl = hit.distance;

            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            _telemetry = new DroneTelemetry
            {
                armed = Armed,
                crashed = Crashed,
                mode = _input.mode,
                throttle = _input.throttle,
                speed = vel.magnitude,
                verticalSpeed = vel.y,
                altitudeAgl = agl,
                heightAboveHome = pos.y - _homePosition.y,
                distanceHome = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(_homePosition.x, 0f, _homePosition.z)),
                voltage = _battery.Voltage,
                cellVoltage = _battery.CellVoltage,
                current = _battery.Current,
                usedMah = _battery.UsedMah,
                flightTime = _armedTime,
                pitchDeg = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg,
                rollDeg = -Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg,
                headingDeg = (Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + 360f) % 360f,
            };
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Crashed || collision.relativeVelocity.magnitude < config.crashSpeed)
                return;
            Crashed = true;
            Armed = false;
            OnCrash?.Invoke(collision);
        }
    }
}
