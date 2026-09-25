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
        public const string Mrap = Dir + "MRAP_Typhoon.prefab";
        public const string IfvEra = Dir + "IFV_BMP3M_ERA.prefab";
        public const string Mtlb = Dir + "MTLB_Tractor.prefab";
        public const string SamLauncherTravel = Dir + "SAM_Launcher_Travel.prefab";
        public const string Helicopter = Dir + "Helicopter_Mi28.prefab";
        const string ModelDir = "Assets/_Project/Art/Models/Vehicles/";

        static readonly Color Green = new Color(0.29f, 0.33f, 0.22f);

        [MenuItem("Modern Drone/Setup/Build Vehicle Catalog")]
        public static void BuildAll()
        {
            BuildIfv();
            BuildIfvEra();
            BuildMtlb();
            BuildApc();
            BuildSpg();
            BuildHowitzer();
            BuildSpaag();
            BuildSamLauncher();
            BuildSamLauncherTravel();
            BuildSamRadar();
            BuildMlrs();
            BuildTruck();
            BuildMrap();
            BuildHelicopter();
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

        /// <summary>
        /// BMP-3M: 7.14 x 3.15 x 2.3 m, 100 mm + 30 mm turret, engine at the rear (en.wikipedia.org/wiki/BMP-3).
        /// Models "BMP-3M" / "BMP-3M ERA" by 42manako (CC BY-NC). The ERA variant adds reactive blocks along the sides and
        /// slat screens over the rear flanks and turret rear. Armor points are abstract.
        /// </summary>
        static void BuildIfv() => BuildBmp3(false);

        static void BuildIfvEra() => BuildBmp3(true);

        static void BuildBmp3(bool era)
        {
            var k = new VehicleKit(era ? "IFV_BMP3M_ERA" : "IFV_Placeholder", Green);
            k.Tracks(1.27f, 0.37f, 1.2f, 6.4f, z: 0.41f);
            k.Plate("UpperFront", v(0, 1.75f, 3.35f), v(2.9f, 1.0f, 0.07f), 45f, rot: Pitch(-70f));
            k.Plate("LowerFront", v(0, 1.0f, 3.75f), v(2.9f, 0.8f, 0.07f), 40f, rot: Pitch(40f));
            k.Plate("Side_L", v(-1.45f, 1.35f, 0.2f), v(0.06f, 1.2f, 6.8f), 25f);
            k.Plate("Side_R", v(1.45f, 1.35f, 0.2f), v(0.06f, 1.2f, 6.8f), 25f);
            k.Plate("Rear", v(0, 1.3f, -3.4f), v(2.9f, 1.1f, 0.06f), 20f);
            k.Plate("Roof", v(0, 1.85f, 0.2f), v(2.9f, 0.05f, 6.6f), 18f);
            k.Plate("Floor", v(0, 0.5f, 0.2f), v(2.9f, 0.05f, 6.8f), 15f);

            var turret = k.Group("Turret", v(0.001f, 1.636f, 0.391f));
            k.Plate("Turret_Front", v(0, 0.55f, 1.05f), v(2.0f, 0.8f, 0.1f), 45f, turret, Pitch(-30f));
            k.Plate("Turret_Side_L", v(-1.0f, 0.55f, 0), v(0.06f, 0.8f, 2.0f), 25f, turret);
            k.Plate("Turret_Side_R", v(1.0f, 0.55f, 0), v(0.06f, 0.8f, 2.0f), 25f, turret);
            k.Plate("Turret_Rear", v(0, 0.55f, -1.0f), v(2.0f, 0.8f, 0.06f), 18f, turret);
            k.Plate("Turret_Roof", v(0, 0.97f, 0), v(2.0f, 0.05f, 2.0f), 15f, turret);
            var gun = k.Visual("Gun", v(0, 0.45f, 2.4f), v(0.14f, 0.14f, 2.6f), k.Dark, turret);
            k.Module("Module_Gun", ModuleType.Gun, 80f, 20f, Vector3.zero, Vector3.one * 1.1f, gun.transform);
            k.Module("Module_Breech", ModuleType.Breech, 90f, 30f, v(0, 0.45f, 0.4f), v(0.5f, 0.4f, 0.9f), turret);
            k.Module("Module_TurretDrive", ModuleType.TurretDrive, 60f, 15f, v(0.6f, 0.2f, 0), v(0.3f, 0.3f, 0.3f), turret);
            k.Module("Module_Optics", ModuleType.Optics, 30f, 5f, v(-0.35f, 0.95f, 0.35f), v(0.3f, 0.3f, 0.3f), turret);
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 70f, 15f, v(0, 0.1f, 0), v(1.6f, 0.4f, 1.6f), turret);

            k.Module("Module_Engine", ModuleType.Engine, 150f, 40f, v(0, 1.2f, -2.4f), v(2.2f, 0.8f, 1.6f));
            k.Module("Module_Transmission", ModuleType.Transmission, 120f, 40f, v(0, 0.85f, -3.1f), v(2.4f, 0.5f, 0.5f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 90f, 25f, v(0, 1.35f, 2.7f), v(1.4f, 0.8f, 1.0f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 70f, 15f, v(0, 1.0f, 1.8f), v(2.0f, 0.6f, 0.6f));
            if (era)
            {
                // Reactive blocks along the side frames, slat screens over the rear flanks and the turret rear.
                foreach (float x in new[] { -1.6f, 1.6f })
                    for (int i = 0; i < 6; i++)
                        k.Era($"Era_Side_{(x < 0 ? "L" : "R")}{i}", v(x, 1.35f, 3.4f - i * 0.85f), v(0.12f, 0.8f, 0.8f));
                k.Cage("Slat_L", v(-1.62f, 1.33f, -2.19f), v(0.3f, 0.8f, 1.3f), 0.5f);
                k.Cage("Slat_R", v(1.62f, 1.33f, -2.19f), v(0.3f, 0.8f, 1.3f), 0.5f);
                k.Cage("Slat_Turret", v(0, 0.45f, -0.55f), v(2.7f, 0.4f, 1.2f), 0.5f, turret);
            }
            k.AttachModel(ModelDir + (era ? "BMP3M/bmp3m_era.glb" : "BMP3M/bmp3m.glb"), Vector3.zero, turret, "Turret");
            k.SliceHull();
            k.Finish(era ? "БМП-3М с ДЗ" : "БМП-3М");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(era ? IfvEra : Ifv);
        }

        /// <summary>
        /// 2S19M1 "Msta-S": 152 mm, 11.9 m with the gun forward, 3.38 x 2.99 m, big turret with the ammunition, light armor
        /// (en.wikipedia.org/wiki/2S19_Msta). Model "2S19M1 MSTA-S SPH" by Jeyhun1985 (CC BY). Armor points are abstract.
        /// </summary>
        static void BuildSpg()
        {
            var k = new VehicleKit("SPG_Placeholder", Green);
            k.Tracks(1.4f, 0.58f, 1.0f, 6.6f, z: 0.1f);
            k.Plate("UpperFront", v(0, 1.4f, 3.6f), v(3.2f, 1.2f, 0.08f), 45f, rot: Pitch(-65f));
            k.Plate("LowerFront", v(0, 0.75f, 3.8f), v(3.2f, 0.7f, 0.08f), 40f, rot: Pitch(45f));
            k.Plate("Side_L", v(-1.62f, 1.2f, 0), v(0.07f, 1.4f, 7.4f), 25f);
            k.Plate("Side_R", v(1.62f, 1.2f, 0), v(0.07f, 1.4f, 7.4f), 25f);
            k.Plate("Rear", v(0, 1.2f, -3.75f), v(3.2f, 1.4f, 0.07f), 18f);
            k.Plate("Roof", v(0, 1.9f, 1.6f), v(3.2f, 0.05f, 4.0f), 18f);
            k.Plate("EngineDeck", v(0, 1.9f, -3.0f), v(3.2f, 0.05f, 1.5f), 12f);
            k.Plate("Floor", v(0, 0.45f, 0), v(3.2f, 0.06f, 7.4f), 15f);

            // Large, thin-skinned turret with the ammunition in the rear: the classic weak spot.
            var turret = k.Group("Turret", v(0, 0.519f, -0.738f));
            k.Plate("Turret_Front", v(0, 2.3f, 1.9f), v(3.2f, 1.4f, 0.12f), 30f, turret);
            k.Plate("Turret_Side_L", v(-1.62f, 2.3f, 0), v(0.07f, 1.4f, 3.9f), 18f, turret);
            k.Plate("Turret_Side_R", v(1.62f, 2.3f, 0), v(0.07f, 1.4f, 3.9f), 18f, turret);
            k.Plate("Turret_Rear", v(0, 2.3f, -1.95f), v(3.2f, 1.4f, 0.06f), 15f, turret);
            k.Plate("Turret_Roof", v(0, 3.02f, 0), v(3.2f, 0.06f, 3.9f), 15f, turret);
            var gun = k.Visual("Gun", v(0, 1.95f, 5.2f), v(0.25f, 0.25f, 6.6f), k.Dark, turret);
            k.Module("Module_Gun", ModuleType.Gun, 120f, 30f, Vector3.zero, Vector3.one * 1.05f, gun.transform);
            k.Module("Module_Breech", ModuleType.Breech, 120f, 40f, v(0, 2.0f, 0.9f), v(0.7f, 0.7f, 1.5f), turret);
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 90f, 20f, v(0, 2.1f, -1.2f), v(2.6f, 1.0f, 1.1f), turret);
            k.Module("Module_TurretDrive", ModuleType.TurretDrive, 70f, 20f, v(1.0f, 1.6f, 0.2f), v(0.4f, 0.4f, 0.4f), turret);
            k.Module("Module_Optics", ModuleType.Optics, 40f, 5f, v(-1.0f, 3.1f, 1.2f), v(0.35f, 0.3f, 0.35f), turret);

            k.Module("Module_Engine", ModuleType.Engine, 180f, 50f, v(0, 1.1f, -2.8f), v(2.6f, 0.9f, 1.4f));
            k.Module("Module_Transmission", ModuleType.Transmission, 120f, 40f, v(0, 0.8f, -3.45f), v(2.7f, 0.6f, 0.4f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 90f, 25f, v(0, 1.2f, 3.0f), v(1.2f, 0.7f, 1.0f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 70f, 15f, v(1.0f, 1.1f, 2.4f), v(0.7f, 0.8f, 1.0f));
            k.AttachModel(ModelDir + "Msta/msta_s.glb", Vector3.zero, turret, "Turret");
            k.SliceHull();
            k.Finish("САУ 2С19М1 «Мста-С»");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Spg);
        }

        // ------------------------------------------------------------------ wheeled

        /// <summary>
        /// BTR-82A, 8x8: 7.7 x 2.9 x 2.41 m, 10 mm hull / 7 mm turret, 30 mm 2A72 (en.wikipedia.org/wiki/BTR-80).
        /// Model "BTR-82" by 42manako (CC BY-NC). Armor points stay abstract.
        /// </summary>
        static void BuildApc()
        {
            var k = new VehicleKit("APC_Placeholder", Green);
            k.Wheels(new[] { 2.18f, 0.74f, -0.98f, -2.39f }, 1.26f, 0.56f, 0.4f);
            k.Plate("UpperFront", v(0, 1.9f, 2.95f), v(2.8f, 1.45f, 0.06f), 30f, rot: Pitch(-62f));
            k.Plate("LowerFront", v(0, 1.2f, 3.72f), v(2.6f, 0.7f, 0.06f), 25f, rot: Pitch(35f));
            k.Plate("Side_L", v(-1.44f, 1.4f, -0.2f), v(0.06f, 1.4f, 6.6f), 25f);
            k.Plate("Side_R", v(1.44f, 1.4f, -0.2f), v(0.06f, 1.4f, 6.6f), 25f);
            k.Plate("Rear", v(0, 1.4f, -3.78f), v(2.8f, 1.4f, 0.06f), 20f);
            k.Plate("Roof", v(0, 2.12f, -0.8f), v(2.8f, 0.05f, 5.8f), 15f);
            k.Plate("Floor", v(0, 0.62f, 0), v(2.8f, 0.05f, 7.0f), 15f);

            var turret = k.Group("Turret", v(0, 2.39f, 0.85f));
            k.Plate("Turret_Front", v(0, 0, 0.62f), v(1.45f, 0.9f, 0.06f), 20f, turret);
            k.Plate("Turret_Side_L", v(-0.74f, 0, 0), v(0.05f, 0.9f, 1.2f), 15f, turret);
            k.Plate("Turret_Side_R", v(0.74f, 0, 0), v(0.05f, 0.9f, 1.2f), 15f, turret);
            k.Plate("Turret_Rear", v(0, 0, -0.62f), v(1.45f, 0.9f, 0.05f), 12f, turret);
            k.Plate("Turret_Roof", v(0, 0.47f, 0), v(1.45f, 0.05f, 1.2f), 12f, turret);
            var gun = k.Visual("Gun", v(0, -0.05f, 1.7f), v(0.1f, 0.1f, 2.2f), k.Dark, turret);
            k.Module("Module_Gun", ModuleType.Gun, 60f, 10f, Vector3.zero, Vector3.one * 1.1f, gun.transform);
            k.Module("Module_Optics", ModuleType.Optics, 25f, 5f, v(0.45f, 0.4f, 0.35f), v(0.25f, 0.25f, 0.25f), turret);

            k.Module("Module_Engine", ModuleType.Engine, 130f, 35f, v(0, 1.35f, -2.7f), v(2.2f, 0.9f, 1.5f));
            k.Module("Module_Transmission", ModuleType.Transmission, 100f, 30f, v(0, 0.95f, -1.6f), v(1.8f, 0.5f, 0.6f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 80f, 20f, v(0, 1.5f, 2.4f), v(1.6f, 0.8f, 1.0f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 15f, v(1.0f, 1.4f, -1.6f), v(0.6f, 0.8f, 1.0f));
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 50f, 10f, v(0, 1.75f, 0.85f), v(0.7f, 0.5f, 0.7f));
            k.AttachModel(ModelDir + "BTR82/btr82.glb", Vector3.zero, turret, "Turret");
            k.SliceHull();
            k.Finish("БТР-82А");
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

        /// <summary>
        /// 96K6 Pantsir-S1 on the KamAZ-6560 8x8: combat module with two twin 30 mm guns, twelve missiles, a search radar on
        /// top and a tracking radar in front (en.wikipedia.org/wiki/Pantsir_missile_system). Model "96K6 Pantsir-S1" by ALI1388
        /// (CC BY), decimated. Armor points are abstract.
        /// </summary>
        static void BuildSpaag()
        {
            var k = new VehicleKit("SPAAG_Placeholder", Green);
            k.Wheels(new[] { 3.6f, 2.1f, -1.9f, -3.4f }, 1.15f, 0.62f, 0.5f);
            k.Plate("Cab_Front", v(0, 2.2f, 4.7f), v(2.5f, 1.6f, 0.06f), 12f);
            k.Plate("Cab_Side_L", v(-1.25f, 2.2f, 3.6f), v(0.06f, 1.6f, 2.2f), 12f);
            k.Plate("Cab_Side_R", v(1.25f, 2.2f, 3.6f), v(0.06f, 1.6f, 2.2f), 12f);
            k.Plate("Cab_Roof", v(0, 3.0f, 3.6f), v(2.5f, 0.06f, 2.2f), 12f);
            k.Module("Module_ControlStation", ModuleType.ControlStation, 60f, 10f, v(0, 2.2f, 3.8f), v(2.2f, 1.2f, 1.4f));
            k.Module("Module_Engine", ModuleType.Engine, 120f, 30f, v(0, 1.5f, 2.3f), v(1.4f, 0.9f, 1.3f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 10f, v(1.1f, 1.1f, 0.8f), v(0.5f, 0.6f, 1.3f));
            k.Module("Module_Generator", ModuleType.Generator, 70f, 20f, v(0, 2.0f, -3.9f), v(2.0f, 1.0f, 1.2f));

            // Combat module on the rear
            k.Plate("Module_Side_L", v(-1.3f, 3.0f, -1.6f), v(0.06f, 1.6f, 4.2f), 15f);
            k.Plate("Module_Side_R", v(1.3f, 3.0f, -1.6f), v(0.06f, 1.6f, 4.2f), 15f);
            k.Plate("Module_Front", v(0, 3.0f, 0.5f), v(2.6f, 1.6f, 0.06f), 15f);
            k.Plate("Module_Roof", v(0, 3.8f, -1.6f), v(2.6f, 0.06f, 4.2f), 15f);
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 60f, 15f, v(0, 3.0f, -1.6f), v(1.6f, 0.8f, 1.8f));
            k.Module("Module_SearchRadar", ModuleType.Radar, 60f, 5f, v(0, 5.0f, -1.7f), v(2.0f, 1.2f, 0.4f));
            k.Module("Module_TrackingRadar", ModuleType.Radar, 50f, 5f, v(0, 3.6f, 0.9f), v(1.2f, 1.2f, 0.3f));
            k.Module("Module_Launcher_L", ModuleType.Launcher, 70f, 10f, v(-1.2f, 3.6f, -1.6f), v(0.8f, 1.0f, 3.2f));
            k.Module("Module_Launcher_R", ModuleType.Launcher, 70f, 10f, v(1.2f, 3.6f, -1.6f), v(0.8f, 1.0f, 3.2f));
            k.Module("Module_Gun_L", ModuleType.Gun, 60f, 10f, v(-0.9f, 3.3f, -0.4f), v(0.35f, 0.35f, 2.4f));
            k.Module("Module_Gun_R", ModuleType.Gun, 60f, 10f, v(0.9f, 3.3f, -0.4f), v(0.35f, 0.35f, 2.4f));
            k.AttachModel(ModelDir + "Pantsir/pantsir_s1.glb");
            k.SliceHull();
            k.Finish("ЗРПК «Панцирь-С1»");
            k.Save(Spaag);
        }

        /// <summary>
        /// S-400 launcher: BAZ-64022 tractor with a 5P85SE2 semitrailer and four missile canisters
        /// (en.wikipedia.org/wiki/S-400_missile_system). Model "S-400 Triumf SAM system" by 42manako (CC BY-NC),
        /// straightened in Blender and re-posed with the canisters lying on the trailer; the pack pivots on the erector
        /// hinge, so the same model serves the deployed launcher (canisters up) and the travelling one.
        /// </summary>
        static void BuildSamLauncher() => BuildSamLauncher(true, SamLauncher, "SAM_Launcher_Placeholder", "ПУ ЗРК С-400");

        static void BuildSamLauncherTravel() => BuildSamLauncher(false, SamLauncherTravel, "SAM_Launcher_Travel", "ПУ ЗРК С-400 (на марше)");

        static void BuildSamLauncher(bool erected, string path, string name, string label)
        {
            var k = new VehicleKit(name, Green);
            k.Wheels(new[] { 6.5f, 2.25f, 0.84f }, 1.1f, 0.68f, 0.5f);
            k.Wheels(new[] { -4.33f, -5.86f }, 1.1f, 0.62f, 0.5f);
            k.Plate("Cab_Front", v(0, 2.4f, 9.0f), v(2.5f, 1.8f, 0.06f), 8f);
            k.Plate("Cab_Side_L", v(-1.25f, 2.4f, 8.2f), v(0.06f, 1.8f, 1.6f), 8f);
            k.Plate("Cab_Side_R", v(1.25f, 2.4f, 8.2f), v(0.06f, 1.8f, 1.6f), 8f);
            k.Plate("Cab_Roof", v(0, 3.3f, 8.2f), v(2.5f, 0.06f, 1.6f), 8f);
            k.Module("Module_ControlStation", ModuleType.ControlStation, 60f, 10f, v(0, 2.5f, 8.2f), v(2.2f, 1.4f, 1.4f));
            k.Module("Module_Engine", ModuleType.Engine, 120f, 30f, v(0, 1.8f, 6.3f), v(1.8f, 1.2f, 1.6f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 10f, v(1.0f, 1.2f, 4.8f), v(0.5f, 0.6f, 1.2f));
            k.Plate("Trailer_Shelter_L", v(-1.3f, 2.7f, 3.0f), v(0.06f, 1.4f, 2.0f), 6f);
            k.Plate("Trailer_Shelter_R", v(1.3f, 2.7f, 3.0f), v(0.06f, 1.4f, 2.0f), 6f);
            k.Module("Module_Generator", ModuleType.Generator, 70f, 15f, v(0, 2.7f, 3.0f), v(2.2f, 1.2f, 1.8f));

            // Canister pack on the erector hinge, laid out in its travel pose (lying forward along the trailer).
            var pack = k.Group("Canisters", v(0.007f, 2.134f, -6.913f));
            k.Visual("PackHull", v(0, 0.73f, 2.5f), v(2.9f, 2.8f, 8.3f), k.Dark, pack);
            int i = 0;
            foreach (float y in new[] { 0.03f, 1.43f })
            foreach (float x in new[] { -0.7f, 0.7f })
                k.Module($"Module_Canister_{i++}", ModuleType.Launcher, 60f, 10f, v(x, y, 2.5f), v(1.3f, 1.3f, 8.2f), pack);
            k.AttachModel(ModelDir + "S400/s400_launcher.glb", Vector3.zero, pack, "Canisters");
            // Outriggers (jack beams and foot pads) are deployed only at a firing position.
            if (!erected)
                foreach (var t in k.T.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Outriggers") t.gameObject.SetActive(false);
            k.SliceHull(0.75f);
            if (erected) pack.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            k.Finish(label);
            k.Save(path);
        }

        /// <summary>
        /// S-400 radar on an 8x8 chassis, antenna post raised (en.wikipedia.org/wiki/S-400_missile_system).
        /// Model "S400 Trioumf Radar" by HWHNL (CC BY-NC). Armor points are abstract.
        /// </summary>
        static void BuildSamRadar()
        {
            var k = new VehicleKit("SAM_Radar_Placeholder", Green);
            k.Wheels(new[] { 5.6f, 4.0f, -2.6f, -4.2f }, 1.15f, 0.7f, 0.5f);
            k.Plate("Cab_Front", v(0, 2.3f, 7.4f), v(2.9f, 1.8f, 0.06f), 10f);
            k.Plate("Cab_Side_L", v(-1.45f, 2.3f, 6.2f), v(0.06f, 1.8f, 2.4f), 10f);
            k.Plate("Cab_Side_R", v(1.45f, 2.3f, 6.2f), v(0.06f, 1.8f, 2.4f), 10f);
            k.Plate("Cab_Roof", v(0, 3.25f, 6.2f), v(2.9f, 0.06f, 2.4f), 10f);
            k.Plate("Cabin_Side_L", v(-1.5f, 2.6f, -2.5f), v(0.06f, 2.6f, 5.0f), 10f);
            k.Plate("Cabin_Side_R", v(1.5f, 2.6f, -2.5f), v(0.06f, 2.6f, 5.0f), 10f);
            k.Plate("Cabin_Roof", v(0, 3.95f, -2.5f), v(3.0f, 0.06f, 5.0f), 10f);
            k.Module("Module_Cab", ModuleType.ControlStation, 60f, 10f, v(0, 2.3f, 6.2f), v(2.4f, 1.4f, 1.8f));
            k.Module("Module_Engine", ModuleType.Engine, 120f, 30f, v(0, 1.6f, 4.6f), v(1.8f, 1.1f, 1.4f));
            k.Module("Module_Equipment", ModuleType.ControlStation, 70f, 15f, v(0, 2.5f, -2.5f), v(2.6f, 2.0f, 4.4f));
            k.Module("Module_Generator", ModuleType.Generator, 80f, 25f, v(0, 1.8f, -6.4f), v(1.4f, 1.2f, 1.2f));
            k.Module("Module_Radar", ModuleType.Radar, 90f, 10f, v(0, 7.5f, 2.6f), v(3.2f, 5.5f, 1.0f));
            k.AttachModel(ModelDir + "S400/s400_radar.glb");
            k.SliceHull();
            k.Finish("РЛС ЗРК С-400");
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

        /// <summary>
        /// Ural-4320 6x6 with a tented cargo bed: 7.37 x 2.5 x 2.72 m (3.0 m with tent), front-mounted engine
        /// (en.wikipedia.org/wiki/Ural-4320). Model "Ural 4320" by Brout (CC BY).
        /// </summary>
        static void BuildTruck()
        {
            var k = new VehicleKit("Truck_Placeholder", Green);
            k.Visual("Chassis", v(0, 0.95f, 0), v(2.2f, 0.4f, 7.2f), k.Dark);
            k.Wheels(new[] { 2.6f, -0.93f, -2.33f }, 1.0f, 0.6f, 0.45f);
            k.Plate("Hood_Front", v(0, 1.5f, 3.68f), v(2.1f, 1.1f, 0.06f), 8f);
            k.Plate("Hood_Top", v(0, 2.08f, 3.1f), v(2.1f, 0.06f, 1.2f), 8f);
            k.Plate("Cab_Front", v(0, 2.45f, 2.2f), v(2.3f, 0.9f, 0.06f), 8f);
            k.Plate("Cab_Side_L", v(-1.2f, 2.2f, 1.55f), v(0.06f, 1.4f, 1.3f), 8f);
            k.Plate("Cab_Side_R", v(1.2f, 2.2f, 1.55f), v(0.06f, 1.4f, 1.3f), 8f);
            k.Plate("Cab_Roof", v(0, 2.9f, 1.55f), v(2.3f, 0.06f, 1.3f), 8f);
            k.Plate("Cab_Back", v(0, 2.2f, 0.9f), v(2.3f, 1.4f, 0.06f), 8f);
            k.Module("Module_Engine", ModuleType.Engine, 120f, 30f, v(0, 1.6f, 3.1f), v(1.2f, 0.8f, 1.1f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 60f, 10f, v(0, 2.2f, 1.55f), v(1.8f, 1.0f, 1.1f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 10f, v(1.05f, 1.1f, 0.3f), v(0.5f, 0.5f, 1.0f));
            // Bed and tent: the fuze fires on them, the jet ignores them.
            k.Visual("Bed", v(0, 1.45f, -1.8f), v(2.4f, 0.6f, 4.1f), k.Hull);
            k.Visual("Tent", v(0, 2.55f, -1.8f), v(2.4f, 1.4f, 4.1f), k.Hull);
            k.Module("Module_Cargo", ModuleType.AmmoRack, 50f, 5f, v(0, 2.0f, -1.8f), v(2.1f, 1.6f, 3.8f));
            k.AttachModel(ModelDir + "Ural4320/ural4320.glb");
            k.SliceHull();
            k.Finish("Урал-4320 (снабжение)");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Truck);
        }

        /// <summary>
        /// KamAZ-63968 "Typhoon-K" MRAP, 6x6: 8.99 x 2.45 x 3.32 m, 21 t, STANAG 4569 level 4 ballistic / 3b mine
        /// protection (en.wikipedia.org/wiki/Kamaz_Typhoon). Model "Kamaz Typhoon" by dugito03 (CC BY).
        /// Armor points are abstract: well above the BTR, far below a tank.
        /// </summary>
        static void BuildMrap()
        {
            var k = new VehicleKit("MRAP_Typhoon", Green);
            k.Wheels(new[] { 2.75f, 0.66f, -2.77f }, 1.06f, 0.66f, 0.5f);
            k.Plate("Front", v(0, 2.3f, 4.5f), v(2.62f, 1.9f, 0.06f), 50f, rot: Pitch(-10f));
            k.Plate("Side_L", v(-1.31f, 2.2f, 0.1f), v(0.06f, 2.1f, 8.9f), 45f);
            k.Plate("Side_R", v(1.31f, 2.2f, 0.1f), v(0.06f, 2.1f, 8.9f), 45f);
            k.Plate("Rear", v(0, 2.2f, -4.38f), v(2.62f, 2.1f, 0.06f), 40f);
            k.Plate("Roof", v(0, 3.25f, 0.1f), v(2.62f, 0.06f, 8.9f), 35f);
            k.Plate("Floor", v(0, 1.1f, 0.1f), v(2.62f, 0.08f, 8.9f), 60f);
            k.Module("Module_Engine", ModuleType.Engine, 130f, 35f, v(0, 1.45f, 3.4f), v(1.4f, 0.9f, 1.3f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 70f, 15f, v(0, 2.35f, 3.6f), v(2.2f, 1.2f, 1.4f));
            k.Module("Module_Transmission", ModuleType.Transmission, 100f, 30f, v(0, 0.95f, 0.3f), v(1.0f, 0.5f, 4.5f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 15f, v(1.0f, 1.3f, -1.4f), v(0.5f, 0.7f, 1.2f));
            k.AttachModel(ModelDir + "Typhoon/typhoon.glb");
            k.SliceHull();
            k.Finish("КамАЗ «Тайфун-К»");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Mrap);
        }

        /// <summary>
        /// Mi-28NM attack helicopter: 17.01 m long, 17.2 m rotor, 3.82 m tall, two VK-2500P engines, armored cockpit,
        /// composite blades that survive 30 mm hits, H025 radar above the rotor (en.wikipedia.org/wiki/Mil_Mi-28).
        /// Model "Mil Mi-28NM Havoc" by VTX (CC BY-NC-SA). Armor points are abstract.
        /// </summary>
        static void BuildHelicopter()
        {
            var k = new VehicleKit("Helicopter_Mi28", Green);
            // Physical shapes for the contact fuze (the rotor disc is left open: blades cover little of it).
            k.Visual("Fuselage", v(0, 2.0f, 0.75f), v(1.9f, 2.1f, 10.5f), k.Dark);
            k.Visual("TailBoom", v(0, 2.7f, -8.0f), v(0.8f, 0.9f, 7.5f), k.Dark);
            k.Visual("Fin", v(0, 4.4f, -11.2f), v(0.4f, 3.4f, 2.4f), k.Dark);
            k.Visual("StubWings", v(0, 2.3f, 0.3f), v(4.9f, 0.2f, 1.3f), k.Dark);

            k.Plate("Cockpit_Front", v(0, 2.0f, 5.95f), v(1.2f, 1.6f, 0.06f), 40f, rot: Pitch(-25f));
            k.Plate("Cockpit_Side_L", v(-0.6f, 2.1f, 4.4f), v(0.06f, 1.6f, 3.0f), 40f);
            k.Plate("Cockpit_Side_R", v(0.6f, 2.1f, 4.4f), v(0.06f, 1.6f, 3.0f), 40f);
            k.Plate("Cockpit_Floor", v(0, 1.2f, 4.4f), v(1.2f, 0.06f, 3.0f), 40f);
            k.Plate("Side_L", v(-0.95f, 2.0f, -0.5f), v(0.05f, 2.0f, 7.8f), 10f);
            k.Plate("Side_R", v(0.95f, 2.0f, -0.5f), v(0.05f, 2.0f, 7.8f), 10f);
            k.Plate("Belly", v(0, 0.95f, -0.5f), v(1.9f, 0.05f, 7.8f), 12f);
            k.Plate("Top", v(0, 3.05f, -0.5f), v(1.9f, 0.05f, 7.8f), 10f);
            k.Plate("Engine_Cowl_L", v(-1.0f, 3.05f, -0.6f), v(0.8f, 0.8f, 2.4f), 20f);
            k.Plate("Engine_Cowl_R", v(1.0f, 3.05f, -0.6f), v(0.8f, 0.8f, 2.4f), 20f);

            k.Module("Module_ControlStation", ModuleType.ControlStation, 90f, 25f, v(0, 2.1f, 4.3f), v(1.1f, 1.1f, 2.4f));
            k.Module("Module_Engine_L", ModuleType.Engine, 120f, 30f, v(-1.0f, 3.05f, -0.6f), v(0.65f, 0.65f, 2.2f));
            k.Module("Module_Engine_R", ModuleType.Engine, 120f, 30f, v(1.0f, 3.05f, -0.6f), v(0.65f, 0.65f, 2.2f));
            k.Module("Module_Gearbox", ModuleType.Transmission, 150f, 40f, v(0, 3.3f, 0), v(1.0f, 0.7f, 1.2f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 70f, 15f, v(0, 1.6f, -0.6f), v(1.4f, 0.8f, 2.2f));
            k.Module("Module_Gun", ModuleType.Gun, 60f, 15f, v(0, 0.8f, 5.0f), v(0.35f, 0.35f, 1.3f));
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 50f, 10f, v(0, 1.3f, 3.0f), v(0.8f, 0.6f, 1.0f));
            k.Module("Module_Launcher_L", ModuleType.Launcher, 50f, 10f, v(-2.2f, 1.9f, 0.3f), v(1.1f, 0.6f, 2.2f));
            k.Module("Module_Launcher_R", ModuleType.Launcher, 50f, 10f, v(2.2f, 1.9f, 0.3f), v(1.1f, 0.6f, 2.2f));
            k.Module("Module_Radar", ModuleType.Radar, 40f, 5f, v(0, 4.65f, 0), v(1.0f, 0.8f, 1.0f));
            k.Module("Module_Optics", ModuleType.Optics, 30f, 5f, v(0, 1.3f, 6.0f), v(0.5f, 0.5f, 0.5f));
            k.Module("Module_TailShaft", ModuleType.TailRotor, 60f, 10f, v(0, 2.9f, -7.5f), v(0.35f, 0.35f, 7.5f));
            k.Module("Module_TailRotor", ModuleType.TailRotor, 60f, 5f, v(0.4f, 4.6f, -11.3f), v(0.6f, 3.8f, 3.8f));
            var rotor = k.Group("Rotor", v(0, 3.911f, 0));
            k.Module("Module_Rotor", ModuleType.Rotor, 200f, 5f, v(0, 0.1f, 0), v(16.5f, 0.25f, 16.5f), rotor);

            k.AttachModel(ModelDir + "Mi28NM/mi28nm.glb", Vector3.zero, rotor, "Rotor");
            k.SliceHull();
            k.Finish("Ми-28НМ");
            k.Root.AddComponent<HelicopterFlight>().Configure(new Vector3[0], rotor);
            k.Save(Helicopter);
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

        /// <summary>
        /// D-30 122 mm towed howitzer in firing position on its three spread trails (en.wikipedia.org/wiki/122_mm_howitzer_2A18_(D-30)).
        /// Model "D-30 howitzer" by 42manako (CC BY), converted from feet.
        /// </summary>
        static void BuildHowitzer()
        {
            var k = new VehicleKit("Howitzer_Placeholder", Green);
            k.Plate("Shield", v(0, 1.3f, 0.7f), v(1.8f, 1.1f, 0.05f), 8f);
            var barrel = k.Visual("Gun", v(0, 1.45f, 2.6f), v(0.2f, 0.2f, 4.6f), k.Dark);
            k.Module("Module_Gun", ModuleType.Gun, 80f, 10f, Vector3.zero, Vector3.one * 1.1f, barrel.transform);
            k.Module("Module_Breech", ModuleType.Breech, 80f, 20f, v(0, 1.45f, 0.1f), v(0.5f, 0.5f, 0.9f));
            k.Module("Module_Sight", ModuleType.Optics, 15f, 2f, v(-0.45f, 1.7f, 0.5f), v(0.2f, 0.25f, 0.25f));
            k.Module("Module_AmmoCrates", ModuleType.AmmoRack, 40f, 5f, v(2.2f, 0.25f, -1.2f), v(1.5f, 0.5f, 1.0f), visible: true,
                mat: VehicleKit.Material("CrateWood", new Color(0.42f, 0.35f, 0.22f)));
            k.AttachModel(ModelDir + "D30/d30.glb");
            k.SliceHull(0.5f);
            k.Finish("Гаубица Д-30");
            k.Save(Howitzer);
        }

        /// <summary>
        /// MT-LBM multipurpose tracked carrier (tows the D-30): 6.45 x 2.86 x 1.87 m, 7-14 mm armor, 7.62 mm MG turret
        /// (en.wikipedia.org/wiki/MT-LB). Model "MT-LBM" by 42manako (CC BY-NC). Armor points are abstract.
        /// </summary>
        static void BuildMtlb()
        {
            var k = new VehicleKit("MTLB_Tractor", Green);
            k.Tracks(1.33f, 0.42f, 1.05f, 5.9f, z: -0.07f);
            k.Plate("UpperFront", v(0, 1.5f, 3.0f), v(2.9f, 0.8f, 0.06f), 20f, rot: Pitch(-55f));
            k.Plate("LowerFront", v(0, 0.95f, 3.3f), v(2.9f, 0.6f, 0.06f), 18f, rot: Pitch(40f));
            k.Plate("Side_L", v(-1.5f, 1.25f, 0), v(0.06f, 1.0f, 6.3f), 15f);
            k.Plate("Side_R", v(1.5f, 1.25f, 0), v(0.06f, 1.0f, 6.3f), 15f);
            k.Plate("Rear", v(0, 1.25f, -3.2f), v(2.9f, 1.0f, 0.06f), 12f);
            k.Plate("Roof", v(0, 1.75f, -0.4f), v(2.9f, 0.05f, 5.4f), 12f);
            var turret = k.Group("Turret", v(0.643f, 1.673f, 1.394f));
            k.Plate("Turret_Shell", v(0, 0.15f, 0), v(0.84f, 0.3f, 0.84f), 12f, turret);
            k.Module("Module_Gun", ModuleType.Gun, 40f, 5f, v(0, 0.15f, 0.5f), v(0.2f, 0.2f, 0.6f), turret);
            k.Module("Module_Engine", ModuleType.Engine, 120f, 35f, v(0, 1.1f, 1.0f), v(1.2f, 0.8f, 1.2f));
            k.Module("Module_Transmission", ModuleType.Transmission, 90f, 30f, v(0, 0.8f, 2.6f), v(2.2f, 0.5f, 0.6f));
            k.Module("Module_ControlStation", ModuleType.ControlStation, 70f, 20f, v(-0.6f, 1.35f, 2.2f), v(0.9f, 0.7f, 1.0f));
            k.Module("Module_Fuel", ModuleType.FuelTank, 60f, 15f, v(1.0f, 1.1f, -2.4f), v(0.5f, 0.7f, 1.0f));
            k.Module("Module_AmmoRack", ModuleType.AmmoRack, 50f, 10f, v(0, 1.2f, -1.2f), v(1.4f, 0.5f, 1.2f));
            k.AttachModel(ModelDir + "MTLBM/mtlbm.glb", Vector3.zero, turret, "Turret");
            k.SliceHull();
            k.Finish("МТ-ЛБМ");
            k.Root.AddComponent<CloseInDefense>().Configure(null, 40f);
            k.Save(Mtlb);
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
            go.GetComponent<VehicleDamageModel>().SetDisplayName("Т-72Б3 с «мангалом»");
            k.Save(TankCage);
        }
    }
}
