namespace ModernDrone.Core
{
    /// <summary>Simulation-wide constants.</summary>
    public static class SimConstants
    {
        /// <summary>Physics tick rate. The flight model needs a high rate for stable acro control.</summary>
        public const float PhysicsHz = 250f;
        public const float FixedDeltaTime = 1f / PhysicsHz;

        public const float AirDensitySeaLevel = 1.225f; // kg/m^3
        public const float Gravity = 9.81f;             // m/s^2
    }
}
