using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.Drone
{
    public enum InputSource
    {
        Auto,
        RcTransmitter,
        Gamepad,
        Keyboard
    }

    /// <summary>
    /// Reads pilot input from an RC transmitter (USB joystick), a gamepad (Mode 2) or the keyboard
    /// and feeds <see cref="DroneController"/>. Stick values are read unprocessed so the Input System's
    /// default gamepad deadzone does not eat small corrections.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DroneInput : MonoBehaviour
    {
        [SerializeField] DroneController drone;
        [SerializeField] DroneCamera droneCamera;
        [SerializeField] InputSource preferredSource = InputSource.Auto;
        [SerializeField] RcJoystickMapping rcMapping = new RcJoystickMapping();

        [Header("Gamepad")]
        [Range(0f, 0.2f)] [SerializeField] float gamepadDeadzone = 0.03f;

        [Header("Keyboard (debug)")]
        [SerializeField] float keyboardThrottleSpeed = 0.5f;
        [SerializeField] float keyboardStick = 0.6f;

        [Header("Camera tilt")]
        [SerializeField] float tiltStep = 5f;

        bool _armToggle;
        FlightMode _mode = FlightMode.Angle;
        float _keyboardThrottle;

        public InputSource ActiveSource { get; private set; }
        public RcJoystickMapping RcMapping => rcMapping;

        void Reset()
        {
            drone = GetComponent<DroneController>();
            droneCamera = GetComponentInChildren<DroneCamera>();
        }

        void Update()
        {
            if (drone == null) return;

            var state = new DroneInputState();
            var gamepad = Gamepad.current;
            var keyboard = Keyboard.current;
            var joystick = Joystick.current;

            ActiveSource = ResolveSource(gamepad, keyboard, joystick);
            switch (ActiveSource)
            {
                case InputSource.RcTransmitter: ReadRc(joystick, ref state); break;
                case InputSource.Gamepad: ReadGamepad(gamepad, ref state); break;
                case InputSource.Keyboard: ReadKeyboard(keyboard, ref state); break;
            }

            // Buttons work from any device.
            bool armPressed = (gamepad?.rightShoulder.wasPressedThisFrame ?? false) || (keyboard?.spaceKey.wasPressedThisFrame ?? false);
            bool modePressed = (gamepad?.leftShoulder.wasPressedThisFrame ?? false) || (keyboard?.mKey.wasPressedThisFrame ?? false);
            state.resetPressed = (gamepad?.startButton.wasPressedThisFrame ?? false) || (keyboard?.rKey.wasPressedThisFrame ?? false);

            if (armPressed) _armToggle = !_armToggle;
            if (modePressed) _mode = _mode == FlightMode.Acro ? FlightMode.Angle : FlightMode.Acro;
            if (state.resetPressed)
            {
                _armToggle = false;
                _keyboardThrottle = 0f;
            }
            // A crash disarms the drone; drop the toggle so the next press arms instead of disarming.
            if (drone.Crashed) _armToggle = false;

            bool rcArm = ActiveSource == InputSource.RcTransmitter && rcMapping.useArmChannel;
            if (!rcArm) state.armSwitch = _armToggle;

            bool rcMode = ActiveSource == InputSource.RcTransmitter && rcMapping.useModeChannel;
            if (!rcMode) state.mode = _mode;

            HandleCameraTilt(gamepad, keyboard);
            drone.SetInput(state);
        }

        InputSource ResolveSource(Gamepad gamepad, Keyboard keyboard, Joystick joystick)
        {
            switch (preferredSource)
            {
                case InputSource.RcTransmitter when joystick != null: return InputSource.RcTransmitter;
                case InputSource.Gamepad when gamepad != null: return InputSource.Gamepad;
                case InputSource.Keyboard when keyboard != null: return InputSource.Keyboard;
            }
            if (joystick != null) return InputSource.RcTransmitter;
            if (gamepad != null) return InputSource.Gamepad;
            return InputSource.Keyboard;
        }

        void ReadRc(Joystick joystick, ref DroneInputState s)
        {
            float db = rcMapping.deadband;
            s.roll = Deadband(rcMapping.roll.Read(joystick) ?? 0f, db);
            s.pitch = Deadband(rcMapping.pitch.Read(joystick) ?? 0f, db);
            s.yaw = Deadband(rcMapping.yaw.Read(joystick) ?? 0f, db);
            s.throttle = Mathf.Clamp01(((rcMapping.throttle.Read(joystick) ?? -1f) + 1f) * 0.5f);
            if (rcMapping.useArmChannel)
                s.armSwitch = (rcMapping.arm.Read(joystick) ?? -1f) > 0f;
            if (rcMapping.useModeChannel)
                s.mode = (rcMapping.mode.Read(joystick) ?? -1f) > 0f ? FlightMode.Angle : FlightMode.Acro;
        }

        void ReadGamepad(Gamepad gamepad, ref DroneInputState s)
        {
            Vector2 left = gamepad.leftStick.ReadUnprocessedValue();
            Vector2 right = gamepad.rightStick.ReadUnprocessedValue();
            // Mode 2: left stick throttle/yaw, right stick pitch/roll. Spring-centred throttle: centre = 50%.
            s.throttle = Mathf.Clamp01((left.y + 1f) * 0.5f);
            s.yaw = Deadband(left.x, gamepadDeadzone);
            s.pitch = Deadband(right.y, gamepadDeadzone);
            s.roll = Deadband(right.x, gamepadDeadzone);
        }

        void ReadKeyboard(Keyboard kb, ref DroneInputState s)
        {
            if (kb.wKey.isPressed) _keyboardThrottle += keyboardThrottleSpeed * Time.deltaTime;
            if (kb.sKey.isPressed) _keyboardThrottle -= keyboardThrottleSpeed * Time.deltaTime;
            if (kb.xKey.wasPressedThisFrame) _keyboardThrottle = 0f;
            _keyboardThrottle = Mathf.Clamp01(_keyboardThrottle);

            s.throttle = _keyboardThrottle;
            s.yaw = Axis(kb.aKey.isPressed, kb.dKey.isPressed) * keyboardStick;
            s.pitch = Axis(kb.downArrowKey.isPressed, kb.upArrowKey.isPressed) * keyboardStick;
            s.roll = Axis(kb.leftArrowKey.isPressed, kb.rightArrowKey.isPressed) * keyboardStick;
        }

        void HandleCameraTilt(Gamepad gamepad, Keyboard keyboard)
        {
            if (droneCamera == null) return;
            if ((gamepad?.dpad.up.wasPressedThisFrame ?? false) || (keyboard?.rightBracketKey.wasPressedThisFrame ?? false))
                droneCamera.TiltDeg += tiltStep;
            if ((gamepad?.dpad.down.wasPressedThisFrame ?? false) || (keyboard?.leftBracketKey.wasPressedThisFrame ?? false))
                droneCamera.TiltDeg -= tiltStep;
        }

        static float Axis(bool negative, bool positive) => (positive ? 1f : 0f) - (negative ? 1f : 0f);

        static float Deadband(float v, float db)
        {
            float abs = Mathf.Abs(v);
            if (abs <= db) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((abs - db) / (1f - db));
        }
    }
}
