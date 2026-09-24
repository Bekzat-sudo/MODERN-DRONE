using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Physics layer indices and masks. Must match ProjectSettings/TagManager.asset.
    /// </summary>
    public static class GameLayers
    {
        public const int Drone = 8;
        public const int Terrain = 9;
        public const int Environment = 10;
        public const int VehicleHull = 11;
        public const int Armor = 12;
        public const int Module = 13;
        public const int ERA = 14;
        public const int Screen = 15;
        public const int Debris = 16;

        /// <summary>Layer names in index order starting at <see cref="Drone"/>.</summary>
        public static readonly string[] Names =
        {
            "Drone", "Terrain", "Environment", "VehicleHull", "Armor", "Module", "ERA", "Screen", "Debris"
        };

        public const int DroneMask = 1 << Drone;
        public const int TerrainMask = 1 << Terrain;
        public const int EnvironmentMask = 1 << Environment;
        public const int VehicleHullMask = 1 << VehicleHull;
        public const int ArmorMask = 1 << Armor;
        public const int ModuleMask = 1 << Module;
        public const int ERAMask = 1 << ERA;
        public const int ScreenMask = 1 << Screen;
        public const int DebrisMask = 1 << Debris;

        /// <summary>Everything that blocks line of sight for the video/control link.</summary>
        public const int SignalBlockerMask = TerrainMask | EnvironmentMask | VehicleHullMask;

        /// <summary>What a warhead fuze can be triggered by.</summary>
        public const int FuzeTriggerMask = TerrainMask | EnvironmentMask | VehicleHullMask | ArmorMask | ERAMask | ScreenMask;

        /// <summary>Layers traversed by a shaped-charge jet or spall ray.</summary>
        public const int PenetrationMask = ArmorMask | ERAMask | ScreenMask | ModuleMask;

        /// <summary>Physical collisions to disable. Modules are query-only; debris never hits the drone.</summary>
        public static readonly (int a, int b)[] IgnoredCollisionPairs = BuildIgnoredPairs();

        static (int, int)[] BuildIgnoredPairs()
        {
            var pairs = new System.Collections.Generic.List<(int, int)>();
            for (int i = 0; i < 32; i++)
                pairs.Add((Module, i));
            pairs.Add((Debris, Drone));
            return pairs.ToArray();
        }

        public static bool IsInMask(int layer, int mask) => (mask & (1 << layer)) != 0;

        public static bool IsInMask(GameObject go, int mask) => IsInMask(go.layer, mask);
    }
}
