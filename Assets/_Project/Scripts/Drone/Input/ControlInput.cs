using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModernDrone.Drone
{
    /// <summary>Device lookup and binding evaluation shared by flight input and menus.</summary>
    public static class ControlInput
    {
        /// <summary>Preferred joystick by display name, else the first joystick-class device.</summary>
        public static Joystick FindJoystick(string name)
        {
            Joystick first = null;
            foreach (var d in InputSystem.devices)
            {
                if (!(d is Joystick j)) continue;
                if (first == null) first = j;
                if (!string.IsNullOrEmpty(name) && (j.displayName == name || j.name == name)) return j;
            }
            return first;
        }

        public static Joystick PreferredJoystick => FindJoystick(ControlSettings.Current.joystickName);

        static bool _padPrompts;
        static int _promptFrame = -1;

        /// <summary>
        /// Menu prompts show gamepad buttons once a gamepad was used, keyboard keys once the keyboard or mouse was
        /// (the last device touched wins; polled at most once per frame, from the drawing code that asks).
        /// </summary>
        public static bool PadPrompts
        {
            get
            {
                if (_promptFrame == UnityEngine.Time.frameCount) return _padPrompts;
                _promptFrame = UnityEngine.Time.frameCount;
                var gp = Gamepad.current;
                if (gp != null && (gp.buttonSouth.isPressed || gp.buttonEast.isPressed || gp.startButton.isPressed
                    || gp.dpad.ReadValue().sqrMagnitude > 0.25f || gp.leftStick.ReadValue().sqrMagnitude > 0.25f
                    || gp.leftShoulder.isPressed || gp.rightShoulder.isPressed))
                    _padPrompts = true;
                else if ((Keyboard.current != null && Keyboard.current.anyKey.isPressed)
                    || (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.delta.ReadValue().sqrMagnitude > 4f)))
                    _padPrompts = false;
                return _padPrompts;
            }
        }

        /// <summary>The keyboard or the gamepad name of a menu control, whichever device is in use.</summary>
        public static string Key(string keyboard, string gamepad) => PadPrompts ? gamepad : keyboard;

        public static bool WasPressed(PilotAction action) => Check(action, pressedThisFrame: true);

        public static bool IsHeld(PilotAction action) => Check(action, pressedThisFrame: false);

        static bool Check(PilotAction action, bool pressedThisFrame)
        {
            var b = ControlSettings.Current.Binding(action);
            if (Button(Keyboard.current, b.keyboard, pressedThisFrame)) return true;
            if (Button(Mouse.current, b.keyboard, pressedThisFrame)) return true;
            // Any connected gamepad, not only Gamepad.current: a second pad must not swallow presses.
            foreach (var gp in Gamepad.all)
                if (Button(gp, b.gamepad, pressedThisFrame)) return true;
            return Button(PreferredJoystick, b.joystick, pressedThisFrame);
        }

        static bool Button(InputDevice device, string path, bool pressedThisFrame)
        {
            if (device == null || string.IsNullOrEmpty(path)) return false;
            if (!(InputControlPath.TryFindControl(device, path) is ButtonControl button)) return false;
            return pressedThisFrame ? button.wasPressedThisFrame : button.isPressed;
        }

        /// <summary>How to trigger an action on the given device family, for on-screen hints.</summary>
        public static string HintFor(PilotAction action, InputSource source)
        {
            var s = ControlSettings.Current;
            if (action == PilotAction.Arm && source == InputSource.Joystick && s.jsUseArmAxis) return "ARM SWITCH";
            var b = s.Binding(action);
            string path = source == InputSource.Gamepad ? b.gamepad : source == InputSource.Joystick ? b.joystick : b.keyboard;
            if (string.IsNullOrEmpty(path)) path = b.keyboard;
            if (source == InputSource.Gamepad && GamepadLabel(path, Gamepad.current) is string pad) return pad;
            // Prefer the device's own control name over the generic path.
            InputDevice device = source == InputSource.Gamepad ? Gamepad.current : source == InputSource.Joystick ? (InputDevice)PreferredJoystick : Keyboard.current;
            var control = device != null && !string.IsNullOrEmpty(path) ? InputControlPath.TryFindControl(device, path) : null;
            return (control != null ? control.displayName : DisplayName(path)).ToUpperInvariant();
        }

        public static string DisplayName(string path) => string.IsNullOrEmpty(path)
            ? "—"
            : GamepadLabel(path, Gamepad.current)
              ?? InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);

        /// <summary>
        /// Short gamepad button label as printed on the pad: Xbox layout (A B X Y, LB RB, LT RT, Menu, View, LS RS)
        /// by default, PlayStation names (Cross, Circle, Square, Triangle, L1 R1, L2 R2, Options, Share, L3 R3) when a
        /// DualShock / DualSense is connected. Null for anything that is not a gamepad button.
        /// </summary>
        public static string GamepadLabel(string path, Gamepad pad)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("<Gamepad>/")) return null;
            bool ps = pad is UnityEngine.InputSystem.DualShock.DualShockGamepad;
            switch (path.Substring("<Gamepad>/".Length))
            {
                case "buttonSouth": return ps ? "Cross" : "A";
                case "buttonEast": return ps ? "Circle" : "B";
                case "buttonWest": return ps ? "Square" : "X";
                case "buttonNorth": return ps ? "Triangle" : "Y";
                case "leftShoulder": return ps ? "L1" : "LB";
                case "rightShoulder": return ps ? "R1" : "RB";
                case "leftTrigger": return ps ? "L2" : "LT";
                case "rightTrigger": return ps ? "R2" : "RT";
                case "startButton": return ps ? "Options" : "Menu";
                case "selectButton": return ps ? "Share" : "View";
                case "leftStickPress": return ps ? "L3" : "LS";
                case "rightStickPress": return ps ? "R3" : "RS";
                case "dpad/up": return "D-pad ↑";
                case "dpad/down": return "D-pad ↓";
                case "dpad/left": return "D-pad ←";
                case "dpad/right": return "D-pad →";
                default: return null;
            }
        }

        /// <summary>Generic binding path for a control, e.g. "&lt;Gamepad&gt;/buttonSouth", matching any device of that family.</summary>
        public static string GenericPath(InputControl control)
        {
            string family = control.device is Keyboard ? "Keyboard"
                : control.device is Gamepad ? "Gamepad"
                : control.device is Joystick ? "Joystick"
                : control.device is Mouse ? "Mouse"
                : control.device.layout;
            string relative = control.path.Substring(control.device.path.Length + 1);
            return $"<{family}>/{relative}";
        }
    }
}
