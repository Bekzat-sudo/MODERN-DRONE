using ModernDrone.Drone;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Drone class presets by frame size. Airframe numbers follow public hobby FPV data: 5" ~550-650 g AUW with
    /// high thrust-to-weight, 7" long-range ~0.6-1 kg at TWR ~4-6, larger frames heavier and slower to respond
    /// (unmannedtechshop.co.uk TWR/AUW guide; oscarliang.com 7" builds and reviews). Payload mass is abstract gameplay.
    /// </summary>
    public static class DroneClasses
    {
        const string Dir = "Assets/_Project/Data/Drones";
        public const string Light = Dir + "/FPV_5in.asset";
        public const string Standard = Dir + "/FPV_7in.asset";
        public const string Heavy = Dir + "/FPV_10in.asset";

        public static DroneConfig[] LoadAll() => new[] { Load(Light), Load(Standard), Load(Heavy) };

        public static DroneConfig Load(string path)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<DroneConfig>(path);
            if (cfg != null) return cfg;
            cfg = ScriptableObject.CreateInstance<DroneConfig>(); // field defaults = 7" class
            if (path == Light) Configure5(cfg);
            else if (path == Heavy) Configure10(cfg);
            else Configure7(cfg);
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        static void Configure7(DroneConfig c)
        {
            c.displayName = "FPV 7\" (основной)";
            c.summary = "баланс скорости, дальности и нагрузки";
        }

        static void Configure5(DroneConfig c)
        {
            c.displayName = "FPV 5\" (лёгкий)";
            c.summary = "быстрый и вёрткий, малая нагрузка и запас батареи";
            c.frameMass = 0.62f;
            c.payloadMass = 0.25f;
            c.motorDiagonal = 0.225f;
            c.inertia = new Vector3(0.0035f, 0.006f, 0.0035f);
            c.colliderSize = new Vector3(0.22f, 0.06f, 0.22f);
            c.thrustToWeight = 7f;
            c.motorTauUp = 0.02f;
            c.motorTauDown = 0.03f;
            c.yawTorqueCoefficient = 0.012f;
            c.propPitchSpeed = 45f;
            c.dragArea = new Vector3(0.008f, 0.03f, 0.009f);
            c.capacityMah = 1300f;
            c.internalResistance = 0.03f;
            c.maxCurrentPerMotor = 40f;
            c.cameraTilt = 30f;
        }

        static void Configure10(DroneConfig c)
        {
            c.displayName = "FPV 10\" (тяжёлый)";
            c.summary = "большая нагрузка и запас батареи, инертный";
            c.frameMass = 1.6f;
            c.payloadMass = 1.0f;
            c.motorDiagonal = 0.42f;
            c.inertia = new Vector3(0.03f, 0.05f, 0.03f);
            c.colliderSize = new Vector3(0.42f, 0.1f, 0.42f);
            c.thrustToWeight = 4f;
            c.motorTauUp = 0.04f;
            c.motorTauDown = 0.06f;
            c.yawTorqueCoefficient = 0.02f;
            c.propPitchSpeed = 35f;
            c.dragArea = new Vector3(0.02f, 0.07f, 0.022f);
            c.capacityMah = 6000f;
            c.internalResistance = 0.04f;
            c.maxCurrentPerMotor = 60f;
            c.cameraTilt = 20f;
        }
    }
}