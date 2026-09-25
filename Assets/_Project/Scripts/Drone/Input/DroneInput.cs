using ModernDrone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.Drone
{
    public enum InputSource
    {
        Joystick,
        Gamepad,
        KeyboardMouse
    }

    /// <summary>
    /// Turns the active device into <see cref="DroneInputState"/> using <see cref="ControlSettings"/>:
    /// RC transmitter / joystick (calibrated axes), gamepad (Mode 2, selectable throttle mode) or
    /// keyboard + mouse (mouse = pitch/roll virtual stick, W/S throttle, A/D yaw).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DroneInput : MonoBehaviour
    {
        const float TiltStep = 5f;

        [SerializeField] DroneController drone;
        [SerializeField] DroneCamera droneCamera;

        bool _armToggle;
        FlightMode _mode;
        float _heldThrottle;      // keyboard / incremental gamepad throttle
        Vector2 _mouseStick;      // x = roll, y = pitch

        public InputSource ActiveSource { get; private set; }

        /// <summary>Missions disable the pilot's reset key: a lost drone is handled by the mission.</summary>
        public bool AllowReset { get; set; } = true;

        /// <summary>
        /// While suspended (briefing, killcam, menus owned by the mission) sticks and buttons are not sent to the drone,
        /// but the active device is still tracked so on-screen hints stay correct.
        /// </summary>
        public bool Suspended { get; set; }

        /// <summary>Current virtual mouse stick (x = roll, y = pitch).</summary>
        public Vector2 MouseStick => _mouseStick;

        void Reset()
        {
            drone = GetComponent<DroneController>();
            droneCamera = GetComponentInChildren<DroneCamera>();
        }

        void Awake()
        {
            _mode = ControlSettings.Current.startMode;
            ActiveSource = ResolveSource(ControlSettings.Current.device, ControlInput.PreferredJoystick, Gamepad.current);
        }

        void OnEnable()
        {
            if (drone != null) drone.OnReset += ClearLatches;
            ControlSettings.Changed += Apply;
        }

        void OnDisable()
        {
            if (drone != null) drone.OnReset -= ClearLatches;
            ControlSettings.Changed -= Apply;
            ReleaseCursor();
        }

        void Start() => Apply(ControlSettings.Current);

        /// <summary>Pushes rates and camera settings to the drone.</summary>
        void Apply(ControlSettings s)
        {
            if (drone != null && drone.Config != null)
            {
                drone.ApplyPilotRates(s.rollRates, s.pitchRates, s.yawRates);
                drone.Config.maxAngle = Mathf.Clamp(s.angleMaxTilt, 10f, 80f);
                drone.Config.setpointCutoffHz = Mathf.Clamp(s.stickSmoothingHz, 0f, 100f);
            }
            if (droneCamera != null)
            {
                droneCamera.TiltDeg = s.cameraTilt;
                droneCamera.HorizontalFov = ModernDrone.Core.NightMode.Thermal ? ModernDrone.Core.NightMode.ThermalHorizontalFov : s.cameraFov;
            }
        }

        /// <summary>Any reset (pilot or mission) starts disarmed with zero held throttle.</summary>
        void ClearLatches()
        {
            _armToggle = false;
            _heldThrottle = 0f;
            _mouseStick = Vector2.zero;
        }

        void Update()
        {
            if (drone == null) return;
            if (GamePause.IsPaused)
            {
                ReleaseCursor();
                return;
            }

            var s = ControlSettings.Current;
            var joystick = ControlInput.PreferredJoystick;
            var gamepad = Gamepad.current;
            ActiveSource = ResolveSource(s.device, joystick, gamepad);
            if (Suspended)
            {
                ReleaseCursor();
                return;
            }

            var state = new DroneInputState();
            switch (ActiveSource)
            {
                case InputSource.Joystick: ReadJoystick(s, joystick, ref state); break;
                case InputSource.Gamepad: ReadGamepad(s, gamepad, ref state); break;
                default: ReadKeyboardMouse(s, ref state); break;
            }

            if (ControlInput.WasPressed(PilotAction.Arm)) _armToggle = !_armToggle;
            if (ControlInput.WasPressed(PilotAction.Mode)) _mode = _mode == FlightMode.Acro ? FlightMode.Angle : FlightMode.Acro;
            state.resetPressed = AllowReset && ControlInput.WasPressed(PilotAction.Reset);

            // A crash disarms the drone; drop the toggle so the next press arms instead of disarming.
            if (drone.Crashed) _armToggle = false;

            bool joystickArm = ActiveSource == InputSource.Joystick && s.jsUseArmAxis;
            if (!joystickArm) state.armSwitch = _armToggle;
            bool joystickMode = ActiveSource == InputSource.Joystick && s.jsUseModeAxis;
            if (!joystickMode) state.mode = _mode;

            if (droneCamera != null)
            {
                if (ControlInput.WasPressed(PilotAction.TiltUp)) SetTilt(droneCamera.TiltDeg + TiltStep);
                if (ControlInput.WasPressed(PilotAction.TiltDown)) SetTilt(droneCamera.TiltDeg - TiltStep);
            }

            if (ActiveSource == InputSource.KeyboardMouse) CaptureCursor();
            else ReleaseCursor();

            drone.SetInput(state);
        }

        void SetTilt(float tilt)
        {
            droneCamera.TiltDeg = tilt;
            ControlSettings.Current.cameraTilt = droneCamera.TiltDeg;
        }

        static InputSource ResolveSource(ControlDevice preferred, Joystick joystick, Gamepad gamepad)
        {
            switch (preferred)
            {
                case ControlDevice.Joystick when joystick != null: return InputSource.Joystick;
                case ControlDevice.Gamepad when gamepad != null: return InputSource.Gamepad;
                case ControlDevice.KeyboardMouse: return InputSource.KeyboardMouse;
            }
            if (joystick != null) return InputSource.Joystick;
            if (gamepad != null) return InputSource.Gamepad;
            return InputSource.KeyboardMouse;
        }

        void ReadJoystick(ControlSettings s, Joystick js, ref DroneInputState state)
        {
            float db = s.jsDeadband;
            state.roll = Deadband(s.jsRoll.ReadBipolar(js) ?? 0f, db);
            state.pitch = Deadband(s.jsPitch.ReadBipolar(js) ?? 0f, db);
            state.yaw = Deadband(s.jsYaw.ReadBipolar(js) ?? 0f, db);
            state.throttle = Mathf.Clamp01(s.jsThrottle.ReadUnipolar(js) ?? 0f);
            if (s.jsUseArmAxis)
                // Switch centre is calibrated at the DISARM position, so require a clear throw.
                state.armSwitch = (s.jsArm.ReadBipolar(js) ?? -1f) > 0.5f;
            if (s.jsUseModeAxis)
                state.mode = (s.jsMode.ReadBipolar(js) ?? -1f) > 0.5f ? FlightMode.Angle : FlightMode.Acro;
        }

        void ReadGamepad(ControlSettings s, Gamepad gp, ref DroneInputState state)
        {
            // Unprocessed: the Input System's default stick deadzone (0.125) is far too large for FPV.
            Vector2 left = gp.leftStick.ReadUnprocessedValue();
            Vector2 right = gp.rightStick.ReadUnprocessedValue();
            float dz = s.gpDeadzone;

            state.yaw = Curve(Deadband(left.x, dz), s.gpExpo);
            // Stick up is +1; the flight controller treats +pitch as nose down.
            float tilt = s.gpTiltSensitivity;
            state.roll = Curve(Deadband(right.x, dz), s.gpExpo) * tilt * (s.gpInvertRoll ? -1f : 1f);
            state.pitch = Curve(Deadband(right.y, dz), s.gpExpo) * tilt * (s.gpPitchUpIsNoseUp ? -1f : 1f);

            float ly = Deadband(left.y, dz);
            // Spring-centred throttle: released stick counts as "throttle at rest" for arming.
            state.throttleAtRest = ly <= 0.1f &&
                (s.gpThrottleMode == GamepadThrottleMode.CenterIsHalf || s.gpThrottleMode == GamepadThrottleMode.CenterIsHover);
            switch (s.gpThrottleMode)
            {
                case GamepadThrottleMode.CenterIsHalf:
                    state.throttle = Mathf.Clamp01((left.y + 1f) * 0.5f);
                    break;
                case GamepadThrottleMode.CenterIsHover:
                    float hover = drone.Config != null ? drone.HoverThrottle : 0.5f;
                    state.throttle = ly >= 0f ? Mathf.Lerp(hover, 1f, ly) : Mathf.Lerp(hover, 0f, -ly);
                    break;
                case GamepadThrottleMode.Incremental:
                    _heldThrottle = Mathf.Clamp01(_heldThrottle + ly * s.gpIncrementalSpeed * Time.deltaTime);
                    state.throttle = _heldThrottle;
                    break;
                case GamepadThrottleMode.Trigger:
                    state.throttle = Mathf.Clamp01(gp.rightTrigger.ReadUnprocessedValue());
                    break;
            }
        }

        void ReadKeyboardMouse(ControlSettings s, ref DroneInputState state)
        {
            if (ControlInput.IsHeld(PilotAction.ThrottleUp)) _heldThrottle += s.kbThrottleSpeed * Time.deltaTime;
            if (ControlInput.IsHeld(PilotAction.ThrottleDown)) _heldThrottle -= s.kbThrottleSpeed * Time.deltaTime;
            if (ControlInput.WasPressed(PilotAction.ThrottleCut)) _heldThrottle = 0f;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                _heldThrottle += mouse.scroll.ReadValue().y * 0.0005f;

                // Mouse is a virtual stick for pitch/roll: movement deflects it, it springs back to centre.
                Vector2 delta = mouse.delta.ReadValue();
                _mouseStick.x += delta.x * s.mouseSensitivity;
                _mouseStick.y += delta.y * s.mouseSensitivity;
                if (s.mouseReturn > 0f)
                    _mouseStick = Vector2.MoveTowards(_mouseStick, Vector2.zero, s.mouseReturn * Time.deltaTime);
                _mouseStick = Vector2.ClampMagnitude(_mouseStick, 1f);
                if (mouse.middleButton.wasPressedThisFrame) _mouseStick = Vector2.zero;
            }
            _heldThrottle = Mathf.Clamp01(_heldThrottle);

            state.throttle = _heldThrottle;
            state.roll = _mouseStick.x * (s.mouseInvertRoll ? -1f : 1f);
            // Mouse up is +y; the flight controller treats +pitch as nose down.
            state.pitch = _mouseStick.y * (s.mousePitchUpIsNoseUp ? -1f : 1f);
            float yaw = (ControlInput.IsHeld(PilotAction.YawRight) ? 1f : 0f) - (ControlInput.IsHeld(PilotAction.YawLeft) ? 1f : 0f);
            state.yaw = yaw * s.kbYaw;
        }

        static void CaptureCursor()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void ReleaseCursor()
        {
            if (Cursor.lockState == CursorLockMode.None) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static float Curve(float v, float expo) => expo <= 0f ? v : v * (1f - expo) + v * v * v * expo;

        static float Deadband(float v, float db)
        {
            float abs = Mathf.Abs(v);
            if (abs <= db) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((abs - db) / (1f - db));
        }
    }
}
