using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModernDrone.Drone
{
    /// <summary>One RC channel read from a USB joystick axis, with calibration range and inversion.</summary>
    [Serializable]
    public sealed class RcAxisBinding
    {
        [Tooltip("Control path on the joystick device, e.g. stick/x, stick/y, z, rx, ry, rz, slider.")]
        public string control;
        public bool invert;
        public float min = -1f;
        public float max = 1f;

        public RcAxisBinding(string control, bool invert = false)
        {
            this.control = control;
            this.invert = invert;
        }

        /// <summary>Returns the calibrated value in [-1, 1], or null if the control is missing.</summary>
        public float? Read(InputDevice device)
        {
            if (device == null || string.IsNullOrEmpty(control))
                return null;
            var axis = device.TryGetChildControl<AxisControl>(control);
            if (axis == null)
                return null;

            float raw = axis.ReadUnprocessedValue();
            float v = Mathf.Approximately(max, min) ? 0f : Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(min, max, raw));
            return invert ? -v : v;
        }
    }

    /// <summary>
    /// Channel mapping for an RC transmitter in USB joystick mode.
    /// Defaults assume AETR channel order (EdgeTX/OpenTX default): CH1 X, CH2 Y, CH3 Z, CH4 RX, CH5 RY.
    /// </summary>
    [Serializable]
    public sealed class RcJoystickMapping
    {
        public RcAxisBinding roll = new RcAxisBinding("stick/x");
        public RcAxisBinding pitch = new RcAxisBinding("stick/y");
        public RcAxisBinding throttle = new RcAxisBinding("z");
        public RcAxisBinding yaw = new RcAxisBinding("rx");

        [Tooltip("Use an aux channel as the arm switch (value above 0 = armed).")]
        public bool useArmChannel = true;
        public RcAxisBinding arm = new RcAxisBinding("ry");

        [Tooltip("Use an aux channel for flight mode (value above 0 = Angle).")]
        public bool useModeChannel;
        public RcAxisBinding mode = new RcAxisBinding("rz");

        [Range(0f, 0.1f)] public float deadband = 0.01f;
    }
}
