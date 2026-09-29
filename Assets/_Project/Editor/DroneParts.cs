using System;
using System.Collections.Generic;
using ModernDrone.Drone;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>
    /// Catalogue of swappable drone parts, defined in code and written to assets on every build.
    /// Sources:
    /// - batteries: LiPo ~150-180 Wh/kg at 100C, Li-ion 21700 ~250-280 Wh/kg at 10-15C (oscarliang.com/li-ion-battery-long-range;
    ///   uavmodel.com Li-ion vs LiPo); Molicel P45B datasheet: 4500 mAh, 45 A, ~70 g per cell;
    ///   semi-solid Si-C packs 350-500 Wh/kg at 5-10C (tattuworld.com semi-solid series);
    /// - cameras: Caddx Ratel 2 5.9 g starlight (caddxfpv.com); Caddx 384 thermal 73.5 g, 384x288, 50 Hz (caddxfpv.com);
    ///   256 / 640 cores with 4 / 9 mm lenses (axisflying.com thermal FPV cameras). FOV = 2*atan(pixels * 12 um / 2 / focal);
    /// - props: bi-blade ~15-20% longer flight than tri-blade, more pitch = more speed and current
    ///   (unmannedtechshop.co.uk prop guide; uavmodel.com prop selection);
    /// - video: free-space loss at 1 km 107.7 dB at 5.8 GHz vs 94.0 dB at 1.2 GHz (oscarliang.com/fpv-frequency);
    ///   range ~ 10^(dB/20), doubling power = x1.41 range;
    /// - fiber: 5-20 km spools, 24-130 g per km of cable (en.wikipedia.org/wiki/Fiber-optic_drone; United UAV listings).
    /// Masses marked "оценка" are estimates for the part class; the prop scales are abstract gameplay values following
    /// the trends above.
    /// </summary>
    public static class DroneParts
    {
        const string Dir = "Assets/_Project/Data/Parts/";

        struct Def
        {
            public string id;
            public Action<DronePart> set;
        }

        static readonly Def[] Catalogue =
        {
            // ---- Batteries (6S)
            new Def { id = "Bat_LiPo2200", set = p => Battery(p, "LiPo 6S 2200 мА·ч 100C", "лёгкая, без просадки, мало запаса", 0.33f, 2200, 0.02f) },
            new Def { id = "Bat_LiPo4500", set = p => Battery(p, "LiPo 6S 4500 мА·ч 100C", "мощная и тяжёлая", 0.68f, 4500, 0.015f) },
            new Def { id = "Bat_LiIon18650_2P", set = p => Battery(p, "Li-ion 6S2P 18650, 5600 мА·ч", "стандарт 7\": 12 элементов по ~46 г", 0.6f, 5600, 0.05f) },
            new Def { id = "Bat_P45B_1P", set = p => Battery(p, "Li-ion 6S1P P45B, 4500 мА·ч", "6 элементов 21700 по 70 г, заметная просадка", 0.46f, 4500, 0.09f) },
            new Def { id = "Bat_P45B_2P", set = p => Battery(p, "Li-ion 6S2P P45B, 9000 мА·ч", "стандарт 10\": дальность, но тяжёлая", 0.9f, 9000, 0.045f) },
            new Def { id = "Bat_SemiSolid5000", set = p => Battery(p, "Полутвёрдая Si-C 6S 5000 мА·ч", "~300 Вт·ч/кг (оценка массы), 10C", 0.37f, 5000, 0.06f) },

            // ---- Cameras (thermal cores are offered for night missions; the first one is the night default)
            new Def { id = "Cam_Analog", set = p => Camera(p, "Аналоговая starlight 1200TVL", "5.9 г, 165°, 8 мс", 0.006f, false, Vector2Int.zero, 0f) },
            new Def { id = "Cam_Thermal384", set = p => Camera(p, "Тепловизор 384×288, 4 мм", "73.5 г, поле 60°, стандарт ночных FPV", 0.0735f, true, new Vector2Int(384, 288), 60f) },
            new Def { id = "Cam_Thermal256", set = p => Camera(p, "Тепловизор 256×192, 4 мм", "~35 г (оценка), поле 42°, крупное зерно", 0.035f, true, new Vector2Int(256, 192), 42f) },
            new Def { id = "Cam_Thermal640", set = p => Camera(p, "Тепловизор 640×512, 9 мм", "~100 г (оценка), поле 49°, детально издали", 0.1f, true, new Vector2Int(640, 512), 49f) },

            // ---- Propellers
            new Def { id = "Prop_3Blade", set = p => Props(p, "Трёхлопастные (стандарт)", "баланс тяги и отклика", 1f, 1f, 1f, 1f) },
            new Def { id = "Prop_2Blade", set = p => Props(p, "Двухлопастные", "на 15% экономнее, тяга −10%, мягче отклик", 0.9f, 0.85f, 1f, 0.9f) },
            new Def { id = "Prop_3BladeHighPitch", set = p => Props(p, "Трёхлопастные, большой шаг", "выше скорость, +20% ток, тяжелее раскрутка", 1.05f, 1.2f, 1.25f, 1.1f) },

            // ---- Video link. Range scale = 10^(free-space loss difference / 20) * sqrt(power / 0.8 W)
            new Def { id = "Vtx_58_800", set = p => Radio(p, "5.8 ГГц 800 мВт", "стандарт: глушится, листва гасит сигнал", 0.02f, 1f, 0.8f) },
            new Def { id = "Vtx_58_2500", set = p => Radio(p, "5.8 ГГц 2.5 Вт", "дальность ×1.8, листва всё так же гасит (масса — оценка)", 0.035f, 1.77f, 0.8f) },
            new Def { id = "Vtx_33_2000", set = p => Radio(p, "3.3 ГГц 2 Вт", "дальность ×2.8, лучше проходит листву (масса — оценка)", 0.045f, 2.8f, 0.87f) },
            new Def { id = "Vtx_12_1500", set = p => Radio(p, "1.2 ГГц 1.5 Вт", "дальность ×6.6, почти не теряется в листве (масса — оценка)", 0.06f, 6.6f, 0.93f) },
            new Def { id = "Fiber_5", set = p => Fiber(p, "Оптоволокно 5 км", "не глушится; катушка 0.35 кг", 0.35f, 5000f) },
            new Def { id = "Fiber_10", set = p => Fiber(p, "Оптоволокно 10 км", "не глушится; катушка 0.6 кг (оценка)", 0.6f, 10000f) },
            new Def { id = "Fiber_20", set = p => Fiber(p, "Оптоволокно 20 км", "не глушится; катушка 1.1 кг (оценка)", 1.1f, 20000f) },
        };

        public const string Fpv7Battery = "Bat_LiIon18650_2P";
        public const string NonStopBattery = "Bat_P45B_2P";
        public static readonly string[] StockCommon = { "Cam_Analog", "Prop_3Blade", "Vtx_58_800" };

        public static DronePart[] LoadAll()
        {
            var list = new List<DronePart>();
            foreach (var d in Catalogue) list.Add(Load(d.id));
            AssetDatabase.SaveAssets();
            return list.ToArray();
        }

        public static DronePart Load(string id)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Parts")) AssetDatabase.CreateFolder("Assets/_Project/Data", "Parts");
            string path = Dir + id + ".asset";
            var p = AssetDatabase.LoadAssetAtPath<DronePart>(path);
            bool created = p == null;
            if (created) p = ScriptableObject.CreateInstance<DronePart>();
            Array.Find(Catalogue, d => d.id == id).set(p);
            p.model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Parts/" + id + ".glb");
            if (created) AssetDatabase.CreateAsset(p, path);
            else EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>Stock parts of a drone model: its battery plus the common analog camera, 3-blade props and 5.8 GHz VTX.</summary>
        public static DronePart[] Stock(string battery)
        {
            var list = new List<DronePart> { Load(battery) };
            foreach (var id in StockCommon) list.Add(Load(id));
            return list.ToArray();
        }

        static void Battery(DronePart p, string name, string summary, float mass, float mah, float ohm)
        {
            Common(p, PartSlot.Battery, name, summary, mass);
            p.cells = 6;
            p.capacityMah = mah;
            p.internalResistance = ohm;
        }

        static void Camera(DronePart p, string name, string summary, float mass, bool thermal, Vector2Int res, float fov)
        {
            Common(p, PartSlot.Camera, name, summary, mass);
            p.thermal = thermal;
            p.thermalResolution = res;
            p.horizontalFov = fov;
        }

        static void Props(DronePart p, string name, string summary, float thrust, float current, float pitch, float spin)
        {
            Common(p, PartSlot.Propellers, name, summary, 0f); // prop mass is part of the airframe
            p.thrustScale = thrust;
            p.currentScale = current;
            p.pitchSpeedScale = pitch;
            p.spinUpScale = spin;
        }

        static void Radio(DronePart p, string name, string summary, float mass, float range, float foliage)
        {
            Common(p, PartSlot.VideoLink, name, summary, mass);
            p.fiber = false;
            p.videoRangeScale = range;
            p.foliageBlock = foliage;
        }

        static void Fiber(DronePart p, string name, string summary, float mass, float length)
        {
            Common(p, PartSlot.VideoLink, name, summary, mass);
            p.fiber = true;
            p.spoolLength = length;
        }

        static void Common(DronePart p, PartSlot slot, string name, string summary, float mass)
        {
            p.slot = slot;
            p.displayName = name;
            p.summary = summary;
            p.mass = mass;
        }
    }
}
