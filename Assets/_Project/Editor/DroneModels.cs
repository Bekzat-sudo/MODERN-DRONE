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

        public const string Hornet = Dir + "Hornet.asset";

        /// <summary>The quadcopters of the hangar (the Hornet flies only its own mission).</summary>
        public static DroneConfig[] LoadAll() => new[] { Load(Fpv7), Load(NonStop) };

        /// <summary>Creates the asset if needed and (re)applies the model's parameters, so they stay defined in code.</summary>
        public static DroneConfig Load(string path)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<DroneConfig>(path);
            bool created = cfg == null;
            if (created) cfg = ScriptableObject.CreateInstance<DroneConfig>(); // field defaults = the 7" model
            if (path == Hornet)
            {
                ConfigureHornet(cfg);
                if (created) AssetDatabase.CreateAsset(cfg, path);
                else EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
                return cfg;
            }
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
            // 10" frame 0.22-0.27 kg (GEPRC Mark4 10"), four 3115 motors 4 x 116 g (T-Motor V3115), 10" props 4 x 23 g,
            // ESC, FC, wiring: ~0.86 kg, as the SkyCraft RMD-10 without its battery (865 g). Thrust: ~4.0 kgf per motor,
            // mid-range of the 3.5-4.6 kgf a 3115 900 KV gives on 10" props (T-Motor) - TWR 9 on the 1.79 kg stock build.
            // With pack sag that lifts the 4.5 kg PG-7VR at ~1.45, as 10" strike drones carry it ("Piranha 10": payload
            // up to 4.5 kg, PG-7VR; www1.ru 2024-02-29). The earlier 2.9 kgf left it at 1.05 (30 km/h top speed).
            c.airframeMass = 0.86f;
            c.motorDiagonal = 0.495f;
            c.inertia = new Vector3(0.03f, 0.05f, 0.03f);
            c.colliderSize = new Vector3(0.45f, 0.2f, 0.45f);
            c.payloadDepth = 0.06f;
            c.thrustToWeight = 9f;
            c.motorTauUp = 0.04f;
            c.motorTauDown = 0.06f;
            c.yawTorqueCoefficient = 0.02f;
            c.propPitchSpeed = 35f;
            c.dragArea = new Vector3(0.02f, 0.07f, 0.022f);
            c.maxCurrentPerMotor = 60f;
            c.cameraTilt = 0f; // the model's lens looks straight ahead, so the warhead nose and fuze wires show at the bottom
        }

        /// <summary>
        /// "UAV Hornet" by warmwork: fixed-wing one-way attack drone. Published figures: 2.2 m span, ~1.4 m length,
        /// ~15 kg take-off weight with a ~5 kg payload, cruise 100-120 km/h, dash ~200 km/h, dive 200-300 km/h,
        /// 300-500 m operating altitude, Starlink link (euromaidanpress.com 2026-05-22; root-nation.com; thepage.ua).
        /// Wing area, polar, propulsion and inertia are estimates chosen so FixedWingAero reproduces those speeds
        /// (FixedWingTests); the electric drive figures are estimates too.
        /// </summary>
        static void ConfigureHornet(DroneConfig c)
        {
            c.displayName = "Hornet";
            c.summary = "дальний ударный дрон-самолёт, спутниковая связь";
            c.visualPrefab = DroneRigBuilder.VisualPrefab(DroneRigBuilder.Model.Hornet);
            c.stockParts = new DronePart[0];
            c.fixedWing = true;
            c.satelliteLink = true;
            c.cameraPosition = new Vector3(0f, -0.07f, 0.74f); // ahead of the nose cone (model nose at z = 0.72)
            c.cameraTilt = -10f; // looks down ahead: the ground fills most of the frame in the published footage
            c.cameraHorizontalFov = 70f; // ordinary wide-format view in the published footage (not measured)
            c.airframeMass = c.frameMass = 10f; // 15 kg take-off weight less the 5 kg warhead
            c.colliderSize = new Vector3(2.2f, 0.3f, 1.4f);
            c.inertia = new Vector3(1.0f, 1.6f, 0.8f); // (pitch, yaw, roll), estimate for 15 kg over a 2.2 m span
            c.wingSpan = 2.2f;
            c.wingArea = 0.55f;  // estimate: ~0.25 m mean chord (aspect ratio 8.8)
            c.liftAtZeroAlpha = 0.45f; // wing incidence + camber: level fuselage at cruise (CL = W / qS ~ 0.45)
            c.liftSlope = 5f;    // 2 pi / (1 + 2 / AR), finite-wing lift slope
            c.maxLift = 1.2f;    // stall at ~69 km/h loaded
            c.zeroLiftDrag = 0.05f;
            c.oswald = 0.8f;
            c.sideForceSlope = 0.4f;
            c.pitchStability = 0.3f;
            c.yawStability = 0.15f;
            c.staticThrust = 60f;
            c.thrustPower = 2900f; // sets the dash: ~200 km/h level at full throttle
            c.cruiseSpeed = 31f;   // ~110 km/h
            c.controlSpeed = 25f;
            c.motorTauUp = 0.15f;
            c.motorTauDown = 0.2f;
            c.motorKv = 300f;
            c.propBlades = 3;
            c.cells = 12;
            c.capacityMah = 30000f; // ~2 h at cruise (published endurance)
            c.internalResistance = 0.02f;
            c.maxCurrentPerMotor = 70f; // ~3 kW at 12S
            // Autopilot-style rates and limits of a fixed-wing (abstract): no flips, gentle pitch.
            c.rollRates = new AxisRates(60f, 180f, 0.3f);
            c.pitchRates = new AxisRates(40f, 90f, 0.3f);
            c.yawRates = new AxisRates(20f, 50f, 0.2f);
            c.maxAngle = 50f;
            c.angleMaxRate = 120f;
            c.crashSpeed = 7f;
        }
    }
}
