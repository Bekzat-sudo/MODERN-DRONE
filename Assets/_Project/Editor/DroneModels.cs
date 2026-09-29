using ModernDrone.Drone;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Drone types are drone models: each asset pairs an imported model with the flight parameters of its size class.
    /// Airframe numbers follow public hobby FPV data (unmannedtechshop.co.uk TWR/AUW guide; oscarliang.com 7" and 10"
    /// builds; oscarliang.com/li-ion-battery-long-range). Thrust-to-weight is for the bare airframe; the fitted warhead
    /// (abstract mass) lowers it.
    /// </summary>
    public static class DroneModels
    {
        const string Dir = "Assets/_Project/Data/Drones/";
        public const string Fpv7 = Dir + "FPV_7in.asset";
        public const string NonStop = Dir + "FPV_NonStop.asset";

        public static DroneConfig[] LoadAll() => new[] { Load(Fpv7), Load(NonStop) };

        /// <summary>Creates the asset if needed and (re)applies the model's parameters, so they stay defined in code.</summary>
        public static DroneConfig Load(string path)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<DroneConfig>(path);
            bool created = cfg == null;
            if (created) cfg = ScriptableObject.CreateInstance<DroneConfig>(); // field defaults = the 7" model
            if (path == NonStop) ConfigureNonStop(cfg); else ConfigureFpv7(cfg);
            ApplyStock(cfg, DroneParts.Stock(path == NonStop ? DroneParts.NonStopBattery : DroneParts.Fpv7Battery));
            if (created) AssetDatabase.CreateAsset(cfg, path);
            else EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        /// <summary>All-up weight and battery come from the stock parts, so the loadout can swap them.</summary>
        static void ApplyStock(DroneConfig c, DronePart[] stock)
        {
            c.stockParts = stock;
            c.frameMass = c.airframeMass;
            foreach (var p in stock)
            {
                c.frameMass += p.mass;
                if (p.slot != PartSlot.Battery) continue;
                c.cells = p.cells;
                c.capacityMah = p.capacityMah;
                c.internalResistance = p.internalResistance;
            }
        }

        /// <summary>"FPV Drone" by Jeyhun1985: 7" long-range quad, ~0.5 kg airframe + 6S2P Li-ion ~0.55 kg.</summary>
        static void ConfigureFpv7(DroneConfig c)
        {
            c.displayName = "FPV 7\"";
            c.summary = "лёгкий и манёвренный, малая нагрузка";
            c.visualPrefab = DroneRigBuilder.VisualPrefab(DroneRigBuilder.Model.Fpv7);
            c.cameraPosition = new Vector3(0f, -0.003f, 0.095f);
            c.cameraTilt = 8f; // lens face normal measured on the model: 7.6 deg up
            c.batteryMount = new Vector3(0f, 0.075f, -0.04f);
            c.antennaMount = new Vector3(0f, 0.07f, -0.16f);
            c.spoolMount = new Vector3(0f, -0.08f, -0.08f);
            c.motorKv = 1300f; // 2807 motors for 7" on 6S: 1100-1500 KV (uavmodel.com / yadovex listings)
            c.propBlades = 3;
            c.airframeMass = 0.47f; // 7" frame, 2807 motors, 4-in-1 ESC, FC, props: ~0.45-0.5 kg (oscarliang.com 7" builds)
            c.motorDiagonal = 0.3f;
            c.colliderSize = new Vector3(0.3f, 0.08f, 0.3f);
        }

        /// <summary>
        /// "FPV-dron NonStop" by Viktor_: 10" three-blade props (the model is scaled so they measure 254 mm), 0.495 m
        /// motor diagonal, battery on top, payload slung under the frame. Typical 10" builds: ~1.6 kg with battery,
        /// 6S, TWR ~4-6, 3115-class motors (beastfpv.ru 10", ukr-drone.com.ua LR10 listings).
        /// </summary>
        static void ConfigureNonStop(DroneConfig c)
        {
            c.displayName = "NonStop 10\"";
            c.summary = "тяжёлый: большая нагрузка и запас батареи, инертный";
            c.visualPrefab = DroneRigBuilder.VisualPrefab(DroneRigBuilder.Model.NonStop);
            c.cameraPosition = new Vector3(0f, 0.004f, 0.14f);
            c.batteryMount = new Vector3(0f, 0.15f, -0.03f);
            c.antennaMount = new Vector3(0f, 0.13f, -0.2f);
            c.spoolMount = new Vector3(0f, -0.08f, -0.1f);
            c.motorKv = 900f; // 3115 900 KV class for 10" (Hobbywing / DarwinFPV listings)
            c.propBlades = 3;
            c.airframeMass = 0.68f; // 10" frame, 3115 motors, ESC, FC, props (~1.6 kg AUW with a 6S2P 21700 pack)
            c.motorDiagonal = 0.495f;
            c.inertia = new Vector3(0.03f, 0.05f, 0.03f);
            c.colliderSize = new Vector3(0.45f, 0.2f, 0.45f);
            c.payloadDepth = 0.06f;
            c.thrustToWeight = 6.5f;
            c.motorTauUp = 0.04f;
            c.motorTauDown = 0.06f;
            c.yawTorqueCoefficient = 0.02f;
            c.propPitchSpeed = 35f;
            c.dragArea = new Vector3(0.02f, 0.07f, 0.022f);
            c.maxCurrentPerMotor = 60f;
            c.cameraTilt = 0f; // the model's lens looks straight ahead, so the warhead nose and fuze wires show at the bottom
        }
    }
}
