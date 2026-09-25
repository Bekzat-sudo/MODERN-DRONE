using ModernDrone.Damage;
using ModernDrone.Missions;
using ModernDrone.Vehicles;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Placeholder vehicle family built from primitives. Silhouettes are loosely modelled on common vehicle classes;
    /// armor and module values are abstract gameplay numbers. Replace the visuals with imported models later —
    /// keep the plate / module colliders.
    /// </summary>
    public static class VehicleCatalog
    {
        const string Dir = "Assets/_Project/Prefabs/Vehicles/";

        public const string Ifv = Dir + "IFV_Placeholder.prefab";
        public const string Apc = Dir + "APC_Placeholder.prefab";
        public const string Spg = Dir + "SPG_Placeholder.prefab";
        public const string Howitzer = Dir + "Howitzer_Placeholder.prefab";
        public const string Spaag = Dir + "SPAAG_Placeholder.prefab";
        public const string SamLauncher = Dir + "SAM_Launcher_Placeholder.prefab";
        public const string SamRadar = Dir + "SAM_Radar_Placeholder.prefab";
        public const string Mlrs = Dir + "MLRS_Placeholder.prefab";
        public const string Truck = Dir + "Truck_Placeholder.prefab";
        public const string DecoyTank = Dir + "Decoy_Tank.prefab";
        public const string DecoyMlrs = Dir + "Decoy_MLRS.prefab";
        public const string DecoySpaag = Dir + "Decoy_SPAAG.prefab";
        public const string TankCage = Dir + "Tank_Cage.prefab";
        public const string InterceptorSite = Dir + "InterceptorSite_Placeholder.prefab";

        static readonly Color Green = new Color(0.29f, 0.33f, 0.22f);

        [MenuItem("Modern Drone/Setup/Build Vehicle Catalog")]
        public static void BuildAll()
        {
            BuildIfv();
            BuildApc();
            BuildSpg();
            BuildHowitzer();
            BuildSpaag();
            BuildSamLauncher();
            BuildSamRadar();
            BuildMlrs();
            BuildTruck();
            BuildDecoyTank();
            BuildDecoyMlrs();
            BuildDecoySpaag();
            BuildTankWithCage();
            BuildInterceptorSite();
            Debug.Log("[ModernDrone] Vehicle catalog built.");
        }

        static Vector3 v(float x, float y, float z) => new Vector3(x, y, z);

        static Quaternion Pitch(float deg) => Quaternion.Euler(deg, 0f, 0f);

        // ------------------------------------------------------------------ tracked

        static void BuildIfv()
        {
            var k = new VehicleKit("IFV_Placeholder", Green);
            k.Tracks(1.45f, 0.5f, 0.8f, 6.6f);
            k.Plate("UpperFront", v(0, 1.25f, 3.2f), v(2.6f, 1.0f, 0.07f), 80f, rot: Pitch(-60f));
            k.Plate("LowerFront", v(0, 0.75f, 3.35f), v(2.6f, 0.6f, 0.07f), 60f, rot: Pitch(45f));
            k.Plate("Side_L", v(-1.3f, 1.0f, 0), v(0.06f, 1.1f, 6.4f), 35f);
            k.Plate("Side_R", v(1.3f, 1.0f, 0), v(0.06f, 1.1f, 6.4f), 35f);
            k.Plate("Rear", v(0, 1.0f, -3.2f), v(2.6f, 1.1f, 0.06f), 25f);
            k.Plate("Roof", v(0, 1.55f, -0.2f), v(2.6f, 0.05f, 6.0f), 20f);
            k.Plate("Floor", v(0, 0.45f, 0), v(2.6f, 0.05f, 6.4f), 15f);

            var turret = k.Group("Turret", v(0, 1.85f, 0.5f));
            k.Plate("Turret_Front", v(0, 0, 0.8f), v(1.5f, 0.55f, 0.1f), 60f, turret);
            k.Plate("Turret_Side_L", v(-0.75f, 0, 0), v(0.06f, 0.55f, 1.6f), 30f, turret);
            k.Plate("Turret_Side_R", v(0.75f, 0, 0), v(0.06f, 0.55f, 1.6f), 30f, turret);
            k.Plate("Turret_Rear", v(0, 0, -0.8f), v(1.5f, 0.55f, 0.06f), 25f, turret);
            k.Plate("Turret_Roof", v(0, 0.28f, 0), v(1.5f, 0.05f, 1.6f), 15f, turret);
            var gun = k.Visual("Gun", v(0, 0.05f, 2.2f), v(0.09f, 0.09f, 2.8f), k.Dark, turret);
            k.Module("Module_Gun", ModuleType.Gun, 80f, 20f, Vector3.zero, Vector3.one * 1.1f, gun.transform);
            k.Module("Module_Breech", ModuleType.Breech, 90f, 30f, v(0, 0, 0.3f), v(0.4f, 0.35f, 0.8f), turret);
            k.Module("Module_TurretDrive", ModuleType.TurretDrive, 60f, 15f, v(0.5f, -0.1f, 0), v(0.3f, 0.3f, 0.3f), turret);
            k.Module("Module_Optics", ModuleType.Optics, 30f, 5f, v(-0.45f, 0.4f, 0.4f), v(0.25f, 0.2f, 0.25f), turret, visible: true);

            k.Module("Module_Engine", ModuleType.Engine, 150f, 40f, v(0.7f, 1.0f, 2.2f), v(1.1f, 0.8f, 1.3f));
            k.Module("Module_Transmission", ModuleType.Transmission, 120f, 40f, v(0, 0.8f, 2.95f), v(2.3f, 0.5f, 0.4f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 90f, 25f, v(-0.75f, 1.1f, 2.1f), v(0.8f, 0.7f, 1.0f));
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 70f, 15f, v(0, 1.1f, 0.5f), v(1.3f, 0.5f, 1.2f));
            k.Module("Module_Fuel_L", ModuleType.FuelTank, 70f, 15f, v(-0.8f, 1.0f, -2.8f), v(0.5f, 0.8f, 0.5f));
            k.Module("Module_Fuel_R", ModuleType.FuelTank, 70f, 15f, v(0.8f, 1.0f, -2.8f), v(0.5f, 0.8f, 0.5f));
            k.Finish("БМП (заглушка)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Ifv);
        }

        static void BuildSpg()
        {
            var k = new VehicleKit("SPG_Placeholder", Green);
            k.Tracks(1.75f, 0.6f, 0.9f, 7.0f);
            k.Plate("UpperFront", v(0, 1.25f, 3.55f), v(3.0f, 1.2f, 0.08f), 120f, rot: Pitch(-62f));
            k.Plate("LowerFront", v(0, 0.75f, 3.85f), v(3.0f, 0.7f, 0.08f), 90f, rot: Pitch(45f));
            k.Plate("Side_L", v(-1.5f, 1.0f, 0), v(0.07f, 1.1f, 7.0f), 45f);
            k.Plate("Side_R", v(1.5f, 1.0f, 0), v(0.07f, 1.1f, 7.0f), 45f);
            k.Plate("Rear", v(0, 1.0f, -3.4f), v(3.0f, 1.1f, 0.07f), 30f);
            k.Plate("RoofFront", v(0, 1.55f, 2.4f), v(3.0f, 0.05f, 1.9f), 25f);
            k.Plate("EngineDeck", v(0, 1.55f, -2.8f), v(3.0f, 0.05f, 1.2f), 15f);
            k.Plate("Floor", v(0, 0.45f, 0), v(3.0f, 0.06f, 7.0f), 20f);

            // Large, thin-skinned turret with the ammunition in the bustle: the classic weak spot.
            var turret = k.Group("Turret", v(0, 2.25f, -0.3f));
            k.Plate("Turret_Front", v(0, 0, 1.9f), v(3.0f, 1.3f, 0.15f), 60f, turret);
            k.Plate("Turret_Side_L", v(-1.5f, 0, 0), v(0.08f, 1.3f, 3.8f), 30f, turret);
            k.Plate("Turret_Side_R", v(1.5f, 0, 0), v(0.08f, 1.3f, 3.8f), 30f, turret);
            k.Plate("Turret_Rear", v(0, 0, -1.9f), v(3.0f, 1.3f, 0.06f), 20f, turret);
            k.Plate("Turret_Roof", v(0, 0.65f, 0), v(3.0f, 0.06f, 3.8f), 20f, turret);
            var gun = k.Visual("Gun", v(0, 0.1f, 5.4f), v(0.22f, 0.22f, 7.0f), k.Dark, turret);
            k.Module("Module_Gun", ModuleType.Gun, 120f, 30f, Vector3.zero, Vector3.one * 1.05f, gun.transform);
            k.Module("Module_Breech", ModuleType.Breech, 120f, 40f, v(0, 0, 0.9f), v(0.6f, 0.6f, 1.4f), turret);
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 90f, 20f, v(0, 0, -1.3f), v(2.4f, 0.9f, 1.0f), turret);
            k.Module("Module_TurretDrive", ModuleType.TurretDrive, 70f, 20f, v(1.0f, -0.4f, 0.2f), v(0.4f, 0.4f, 0.4f), turret);
            k.Module("Module_Optics", ModuleType.Optics, 40f, 5f, v(-0.9f, 0.8f, 1.2f), v(0.3f, 0.25f, 0.3f), turret, visible: true);

            k.Module("Module_Engine", ModuleType.Engine, 180f, 50f, v(0, 1.0f, -2.6f), v(2.6f, 0.8f, 1.3f));
            k.Module("Module_Transmission", ModuleType.Transmission, 120f, 40f, v(0, 0.8f, -3.15f), v(2.7f, 0.6f, 0.4f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 90f, 25f, v(-0.9f, 1.1f, 2.6f), v(0.9f, 0.7f, 1.0f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 70f, 15f, v(0.9f, 1.0f, 2.6f), v(0.7f, 0.8f, 1.0f));
            k.Finish("САУ (заглушка)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Spg);
        }

        // ------------------------------------------------------------------ wheeled

        static void BuildApc()
        {
            var k = new VehicleKit("APC_Placeholder", Green);
            k.Wheels(new[] { 2.4f, 1.0f, -1.0f, -2.4f }, 1.35f, 0.55f, 0.4f);
            k.Plate("UpperFront", v(0, 1.7f, 3.4f), v(2.5f, 0.9f, 0.06f), 30f, rot: Pitch(-55f));
            k.Plate("LowerFront", v(0, 1.0f, 3.55f), v(2.5f, 0.7f, 0.06f), 25f, rot: Pitch(40f));
            k.Plate("Side_L", v(-1.3f, 1.35f, -0.1f), v(0.06f, 1.3f, 7.0f), 25f);
            k.Plate("Side_R", v(1.3f, 1.35f, -0.1f), v(0.06f, 1.3f, 7.0f), 25f);
            k.Plate("Rear", v(0, 1.35f, -3.6f), v(2.6f, 1.3f, 0.06f), 20f);
            k.Plate("Roof", v(0, 2.0f, -0.1f), v(2.6f, 0.05f, 7.0f), 15f);
            k.Plate("Floor", v(0, 0.7f, 0), v(2.6f, 0.05f, 7.0f), 15f);

            var turret = k.Group("Turret", v(0, 2.3f, 0.8f));
            k.Plate("Turret_Front", v(0, 0, 0.5f), v(1.0f, 0.5f, 0.06f), 20f, turret);
            k.Plate("Turret_Side_L", v(-0.5f, 0, 0), v(0.05f, 0.5f, 1.0f), 15f, turret);
            k.Plate("Turret_Side_R", v(0.5f, 0, 0), v(0.05f, 0.5f, 1.0f), 15f, turret);
            k.Plate("Turret_Rear", v(0, 0, -0.5f), v(1.0f, 0.5f, 0.05f), 12f, turret);
            k.Plate("Turret_Roof", v(0, 0.25f, 0), v(1.0f, 0.05f, 1.0f), 12f, turret);
            var gun = k.Visual("Gun", v(0, 0.05f, 1.3f), v(0.06f, 0.06f, 1.8f), k.Dark, turret);
            k.Module("Module_Gun", ModuleType.Gun, 60f, 10f, Vector3.zero, Vector3.one * 1.1f, gun.transform);
            k.Module("Module_Optics", ModuleType.Optics, 25f, 5f, v(0.3f, 0.35f, 0.3f), v(0.2f, 0.2f, 0.2f), turret, visible: true);

            k.Module("Module_Engine", ModuleType.Engine, 130f, 35f, v(0, 1.4f, -2.6f), v(1.8f, 0.9f, 1.4f));
            k.Module("Module_Transmission", ModuleType.Transmission, 100f, 30f, v(0, 1.0f, -1.4f), v(1.6f, 0.5f, 0.5f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 80f, 20f, v(0, 1.4f, 2.5f), v(1.6f, 0.8f, 0.9f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 15f, v(1.0f, 1.2f, -1.0f), v(0.5f, 0.8f, 1.0f));
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 50f, 10f, v(0, 1.6f, 0.8f), v(0.6f, 0.5f, 0.6f));
            k.Finish("БТР (заглушка)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Apc);
        }

        /// <summary>Truck chassis with an unarmored cab: engine, driver cab, fuel tank, wheels. Returns front z.</summary>
        static float TruckBase(VehicleKit k, float length, float cabArmor)
        {
            float f = length * 0.5f;
            k.Visual("Chassis", v(0, 0.85f, 0), v(2.3f, 0.45f, length), k.Dark);
            var zs = length > 9.5f ? new[] { f - 0.9f, 0f, -f + 2.0f, -f + 0.8f } : new[] { f - 0.9f, -f + 2.0f, -f + 0.8f };
            k.Wheels(zs, 1.05f, 0.55f, 0.4f);

            k.Plate("Cab_Front", v(0, 1.9f, f), v(2.4f, 1.8f, 0.06f), cabArmor);
            k.Plate("Cab_Side_L", v(-1.2f, 1.9f, f - 0.85f), v(0.06f, 1.8f, 1.7f), cabArmor);
            k.Plate("Cab_Side_R", v(1.2f, 1.9f, f - 0.85f), v(0.06f, 1.8f, 1.7f), cabArmor);
            k.Plate("Cab_Roof", v(0, 2.8f, f - 0.85f), v(2.4f, 0.06f, 1.7f), cabArmor);
            k.Plate("Cab_Back", v(0, 1.9f, f - 1.7f), v(2.4f, 1.8f, 0.06f), cabArmor);

            k.Module("Module_Engine", ModuleType.Engine, 120f, 30f, v(0, 1.3f, f - 0.45f), v(1.4f, 0.6f, 0.9f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 60f, 10f, v(0, 1.9f, f - 1.0f), v(1.8f, 0.9f, 1.0f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 10f, v(1.0f, 0.85f, f - 2.6f), v(0.5f, 0.5f, 1.2f));
            return f;
        }

        static void BuildSpaag()
        {
            var k = new VehicleKit("SPAAG_Placeholder", Green);
            TruckBase(k, 9f, 15f);

            // Armored equipment box carrying the combat module.
            k.Plate("Base_Side_L", v(-1.2f, 1.9f, -1.8f), v(0.06f, 1.2f, 3.0f), 15f);
            k.Plate("Base_Side_R", v(1.2f, 1.9f, -1.8f), v(0.06f, 1.2f, 3.0f), 15f);
            k.Plate("Base_Front", v(0, 1.9f, -0.3f), v(2.4f, 1.2f, 0.06f), 15f);
            k.Plate("Base_Rear", v(0, 1.9f, -3.3f), v(2.4f, 1.2f, 0.06f), 15f);
            k.Plate("Base_Roof", v(0, 2.5f, -1.8f), v(2.4f, 0.06f, 3.0f), 15f);
            k.Module("Module_Generator", ModuleType.Generator, 70f, 20f, v(0, 1.8f, -2.8f), v(1.2f, 0.8f, 1.0f));

            var turret = k.Group("Turret", v(0, 2.95f, -1.8f));
            k.Plate("Turret_Front", v(0, 0, 0.9f), v(1.8f, 0.9f, 0.06f), 20f, turret);
            k.Plate("Turret_Side_L", v(-0.9f, 0, 0), v(0.06f, 0.9f, 1.8f), 20f, turret);
            k.Plate("Turret_Side_R", v(0.9f, 0, 0), v(0.06f, 0.9f, 1.8f), 20f, turret);
            k.Plate("Turret_Rear", v(0, 0, -0.9f), v(1.8f, 0.9f, 0.06f), 20f, turret);
            k.Plate("Turret_Roof", v(0, 0.45f, 0), v(1.8f, 0.06f, 1.8f), 20f, turret);
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 60f, 15f, v(0, -0.1f, -0.2f), v(1.0f, 0.5f, 1.0f), turret);

            // Rotating search radar on a short mast and a tracking radar on the turret face.
            k.Visual("RadarMast", v(0, 0.95f, -0.4f), v(0.12f, 1.1f, 0.12f), k.Metal, turret, collider: false);
            var search = k.Group("SearchRadar", v(0, 1.6f, -0.4f), turret);
            search.gameObject.AddComponent<Rotator>();
            k.Module("Module_SearchRadar", ModuleType.Radar, 60f, 5f, Vector3.zero, v(1.8f, 0.9f, 0.12f), search, visible: true, mat: k.Metal);
            k.Module("Module_TrackingRadar", ModuleType.Radar, 50f, 5f, v(0, 0.1f, 1.0f), v(0.9f, 0.9f, 0.15f), turret, visible: true, mat: k.Metal);

            k.Module("Module_Launcher_L", ModuleType.Launcher, 70f, 10f, v(-1.3f, 0.1f, 0.2f), v(0.7f, 0.6f, 2.4f), turret, visible: true);
            k.Module("Module_Launcher_R", ModuleType.Launcher, 70f, 10f, v(1.3f, 0.1f, 0.2f), v(0.7f, 0.6f, 2.4f), turret, visible: true);
            foreach (float x in new[] { -0.6f, 0.6f })
            {
                var barrel = k.Visual(x < 0 ? "Gun_L" : "Gun_R", v(x, -0.2f, 1.9f), v(0.08f, 0.08f, 2.2f), k.Dark, turret);
                k.Module(x < 0 ? "Module_Gun_L" : "Module_Gun_R", ModuleType.Gun, 60f, 10f, Vector3.zero, Vector3.one * 1.2f, barrel.transform);
            }

            k.Finish("ЗРПК (заглушка)");
            k.Save(Spaag);
        }

        static void BuildSamLauncher()
        {
            var k = new VehicleKit("SAM_Launcher_Placeholder", Green);
            TruckBase(k, 12f, 12f);
            k.Visual("LiftFrame", v(0, 1.6f, -1.5f), v(2.0f, 0.3f, 6.5f), k.Dark);
            var cans = k.Group("Canisters", v(0, 2.2f, -1.5f), rot: Pitch(-8f));
            int i = 0;
            foreach (float y in new[] { 0f, 0.85f })
            foreach (float x in new[] { -0.45f, 0.45f })
                k.Module($"Module_Canister_{i++}", ModuleType.Launcher, 60f, 10f, v(x, y, 0), v(0.8f, 0.8f, 7.5f), cans, visible: true);
            k.Finish("ПУ ЗРК (заглушка)");
            k.Save(SamLauncher);
        }

        static void BuildSamRadar()
        {
            var k = new VehicleKit("SAM_Radar_Placeholder", Green);
            TruckBase(k, 10f, 12f);
            k.Plate("Cabin_Side_L", v(-1.2f, 2.1f, -1.0f), v(0.06f, 2.0f, 3.5f), 10f);
            k.Plate("Cabin_Side_R", v(1.2f, 2.1f, -1.0f), v(0.06f, 2.0f, 3.5f), 10f);
            k.Plate("Cabin_Front", v(0, 2.1f, 0.75f), v(2.4f, 2.0f, 0.06f), 10f);
            k.Plate("Cabin_Rear", v(0, 2.1f, -2.75f), v(2.4f, 2.0f, 0.06f), 10f);
            k.Plate("Cabin_Roof", v(0, 3.1f, -1.0f), v(2.4f, 0.06f, 3.5f), 10f);
            k.Module("Module_Equipment", ModuleType.ControlStation, 70f, 15f, v(0, 2.0f, -1.0f), v(2.0f, 1.4f, 2.5f));
            k.Module("Module_Generator", ModuleType.Generator, 80f, 25f, v(0, 1.6f, -3.6f), v(1.2f, 1.0f, 1.0f), visible: true);
            k.Visual("Mast", v(0, 3.7f, -1.8f), v(0.25f, 1.2f, 0.25f), k.Metal, collider: false);
            var panel = k.Group("RadarPanel", v(0, 5.0f, -1.8f), rot: Pitch(-20f));
            k.Module("Module_Radar", ModuleType.Radar, 90f, 10f, Vector3.zero, v(4.0f, 3.0f, 0.25f), panel, visible: true, mat: k.Metal);
            k.Finish("РЛС ЗРК (заглушка)");
            k.Save(SamRadar);
        }

        static void BuildMlrs()
        {
            var k = new VehicleKit("MLRS_Placeholder", Green);
            TruckBase(k, 8f, 10f);
            var pod = k.Group("RocketPod", v(0, 2.3f, -2.0f), rot: Pitch(-12f));
            k.Module("Module_Rockets", ModuleType.AmmoRack, 60f, 10f, Vector3.zero, v(1.9f, 1.1f, 3.0f), pod, visible: true, mat: k.Hull);
            k.Module("Module_Launcher", ModuleType.Launcher, 70f, 20f, v(0, -0.75f, 0), v(0.8f, 0.4f, 0.8f), pod);
            k.Finish("РСЗО (заглушка)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Mlrs);
        }

        static void BuildTruck()
        {
            var k = new VehicleKit("Truck_Placeholder", Green);
            TruckBase(k, 8f, 5f);
            k.Visual("Bed_L", v(-1.15f, 1.4f, -1.6f), v(0.05f, 0.6f, 4.5f), k.Hull);
            k.Visual("Bed_R", v(1.15f, 1.4f, -1.6f), v(0.05f, 0.6f, 4.5f), k.Hull);
            k.Visual("Bed_Rear", v(0, 1.4f, -3.85f), v(2.3f, 0.6f, 0.05f), k.Hull);
            k.Module("Module_Cargo", ModuleType.AmmoRack, 50f, 5f, v(0, 1.6f, -1.6f), v(1.8f, 0.8f, 3.0f), visible: true, mat: VehicleKit.Material("CrateWood", new Color(0.42f, 0.35f, 0.22f)));
            k.Finish("Грузовик снабжения (заглушка)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Truck);
        }

        /// <summary>
        /// Interceptor-drone team: pickup with a launch table and drone stock, observation mast with an antenna,
        /// and a camouflaged operator post next to it.
        /// </summary>
        static void BuildInterceptorSite()
        {
            var k = new VehicleKit("InterceptorSite_Placeholder", Green);
            TruckBase(k, 5.5f, 5f);
            var table = k.Visual("LaunchTable", v(0, 1.35f, -1.4f), v(1.8f, 0.08f, 1.8f), k.Dark);
            k.Module("Module_DroneStock", ModuleType.AmmoRack, 40f, 5f, v(0.6f, 1.6f, -2.3f), v(0.9f, 0.5f, 0.6f), visible: true,
                mat: VehicleKit.Material("CrateWood", new Color(0.42f, 0.35f, 0.22f)));

            // Observation mast a few metres from the truck.
            k.Visual("Mast", v(-3.5f, 3.5f, -1.0f), v(0.1f, 7.0f, 0.1f), k.Metal, collider: false);
            var antenna = k.Module("Module_Antenna", ModuleType.Antenna, 40f, 5f, v(-3.5f, 7.2f, -1.0f), v(0.6f, 0.6f, 0.1f), visible: true, mat: k.Metal);

            // Operator post under a camo net.
            var net = VehicleKit.Material("CamoNet", new Color(0.24f, 0.28f, 0.17f));
            k.Visual("CamoNet", v(4.0f, 1.3f, -1.5f), v(3.0f, 0.05f, 3.0f), net);
            foreach (float x in new[] { 2.6f, 5.4f })
            foreach (float z in new[] { -2.9f, -0.1f })
                k.Visual("Pole", v(x, 0.65f, z), v(0.06f, 1.3f, 0.06f), k.Dark, collider: false);
            k.Module("Module_OperatorPost", ModuleType.ControlStation, 60f, 5f, v(4.0f, 0.6f, -1.5f), v(1.6f, 1.1f, 1.6f), visible: true, mat: k.ModuleMat);

            k.Finish("Расчёт перехватчиков (заглушка)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            var site = k.Root.AddComponent<ModernDrone.Missions.InterceptorSite>();
            site.Configure(antenna.transform, table.transform, VehicleKit.Material("InterceptorBody", new Color(0.18f, 0.18f, 0.2f)));
            k.Save(InterceptorSite);
        }

        // ------------------------------------------------------------------ towed artillery

        static void BuildHowitzer()
        {
            var k = new VehicleKit("Howitzer_Placeholder", Green);
            k.Visual("Carriage", v(0, 0.9f, 0), v(1.0f, 0.5f, 1.0f), k.Hull);
            for (int i = 0; i < 3; i++)
            {
                var rot = Quaternion.Euler(0f, 60f + i * 120f, 0f);
                k.Visual("Trail", rot * v(0, 0, -1.5f) + v(0, 0.3f, 0), v(0.15f, 0.15f, 3.0f), k.Hull, rot: rot);
            }
            k.Plate("Shield", v(0, 1.3f, 0.6f), v(1.8f, 1.1f, 0.05f), 8f);
            var barrel = k.Visual("Gun", v(0, 1.35f, 2.7f), v(0.15f, 0.15f, 4.8f), k.Dark);
            k.Module("Module_Gun", ModuleType.Gun, 80f, 10f, Vector3.zero, Vector3.one * 1.1f, barrel.transform);
            k.Module("Module_Breech", ModuleType.Breech, 80f, 20f, v(0, 1.35f, 0), v(0.45f, 0.45f, 0.9f), visible: true, mat: k.Dark);
            k.Module("Module_Sight", ModuleType.Optics, 15f, 2f, v(-0.4f, 1.6f, 0.4f), v(0.15f, 0.2f, 0.2f), visible: true);
            k.Module("Module_AmmoCrates", ModuleType.AmmoRack, 40f, 5f, v(2.2f, 0.25f, -2.0f), v(1.5f, 0.5f, 1.0f), visible: true,
                mat: VehicleKit.Material("CrateWood", new Color(0.42f, 0.35f, 0.22f)));
            k.Finish("Гаубица (заглушка)");
            k.Save(Howitzer);
        }

        // ------------------------------------------------------------------ decoy & variants

        /// <summary>
        /// Inflatable tank decoy. Tells: slightly glossy fabric, over-thick short barrel, oversized turret,
        /// no reactive armor blocks, a faint sag. No internals: striking it wastes the drone.
        /// </summary>
        static void BuildDecoyTank()
        {
            var k = new VehicleKit("Decoy_Tank", new Color(0.31f, 0.36f, 0.23f), "DecoyFabric", 0.55f);
            var body = k.Group("Body", Vector3.zero, rot: Quaternion.Euler(0f, 0f, 1.2f));
            k.Plate("Hull", v(0, 1.0f, 0), v(3.4f, 1.1f, 6.9f), 1f, body);
            k.Plate("Track_L", v(-1.9f, 0.45f, 0.3f), v(0.55f, 0.85f, 6.9f), 1f, body, mat: k.Dark);
            k.Plate("Track_R", v(1.9f, 0.45f, 0.3f), v(0.55f, 0.85f, 6.9f), 1f, body, mat: k.Dark);
            k.Plate("Turret", v(0, 1.9f, 0.4f), v(2.5f, 0.75f, 2.6f), 1f, body);
            k.Plate("Gun", v(0, 1.95f, 3.4f), v(0.26f, 0.26f, 4.0f), 1f, body);
            k.Finish("Танк", decoy: true);
            k.Save(DecoyTank);
        }

        /// <summary>Inflatable MLRS decoy: truck-and-pod silhouette, slightly glossy, rounded proportions, no internals.</summary>
        static void BuildDecoyMlrs()
        {
            var k = new VehicleKit("Decoy_MLRS", new Color(0.31f, 0.36f, 0.23f), "DecoyFabric", 0.55f);
            var body = k.Group("Body", Vector3.zero, rot: Quaternion.Euler(0f, 0f, -1f));
            k.Plate("Chassis", v(0, 0.9f, 0), v(2.4f, 0.6f, 8.0f), 1f, body, mat: k.Dark);
            k.Plate("Cab", v(0, 1.9f, 3.1f), v(2.5f, 1.8f, 1.8f), 1f, body);
            k.Plate("Pod", v(0, 2.3f, -2.0f), v(2.1f, 1.2f, 3.2f), 1f, body, Quaternion.Euler(-12f, 0f, 0f));
            k.Finish("РСЗО", decoy: true);
            k.Save(DecoyMlrs);
        }

        /// <summary>Inflatable SPAAG decoy: combat module and radar panel on a truck, no internals.</summary>
        static void BuildDecoySpaag()
        {
            var k = new VehicleKit("Decoy_SPAAG", new Color(0.31f, 0.36f, 0.23f), "DecoyFabric", 0.55f);
            var body = k.Group("Body", Vector3.zero, rot: Quaternion.Euler(0f, 0f, 1f));
            k.Plate("Chassis", v(0, 0.9f, 0), v(2.4f, 0.6f, 9.0f), 1f, body, mat: k.Dark);
            k.Plate("Cab", v(0, 1.9f, 3.6f), v(2.5f, 1.8f, 1.8f), 1f, body);
            k.Plate("Module", v(0, 2.5f, -1.8f), v(2.6f, 2.0f, 3.2f), 1f, body);
            k.Plate("Radar", v(0, 4.4f, -2.2f), v(2.0f, 1.0f, 0.2f), 1f, body, mat: k.Metal);
            k.Finish("ЗРПК", decoy: true);
            k.Save(DecoySpaag);
        }

        /// <summary>Main battle tank with anti-drone cages over the turret roof and engine deck.</summary>
        static void BuildTankWithCage()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.TankPrefabPath) ?? VehicleBuilder.BuildTank();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Tank_Cage";
            var k = new VehicleKit(go);
            k.Cage("Cage_Turret", v(0, 1.0f, 0.1f), v(2.9f, 0.7f, 3.0f), 0.6f, go.transform.Find("Turret"));
            k.Cage("Cage_EngineDeck", v(0, 2.15f, -1.7f), v(3.1f, 0.6f, 2.6f), 0.5f);
            go.GetComponent<VehicleDamageModel>().SetDisplayName("Танк с «мангалом» (заглушка)");
            k.Save(TankCage);
        }
    }
}
