namespace ModernDrone.Drone
{
    public enum FlightMode
    {
        Acro,
        Angle
    }

    /// <summary>One frame of pilot input, already normalised.</summary>
    public struct DroneInputState
    {
        /// <summary>0..1</summary>
        public float throttle;
        /// <summary>-1..1, right positive.</summary>
        public float roll;
        /// <summary>-1..1, stick forward (nose down) positive.</summary>
        public float pitch;
        /// <summary>-1..1, right positive.</summary>
        public float yaw;

        /// <summary>Level state of the arm switch (buttons are converted to a toggle by the input layer).</summary>
        public bool armSwitch;
        public FlightMode mode;

        /// <summary>
        /// Set by the input layer when the throttle control is at rest even though <see cref="throttle"/> is not zero
        /// (spring-centred gamepad stick). Allows arming without pushing the stick down.
        /// </summary>
        public bool throttleAtRest;

        /// <summary>True on the frame a reset was requested.</summary>
        public bool resetPressed;
    }
}
