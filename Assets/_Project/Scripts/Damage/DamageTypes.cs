using System;

namespace ModernDrone.Damage
{
    public enum ModuleType
    {
        Engine,
        Transmission,
        Track,
        FuelTank,
        AmmoRack,
        Gun,
        Breech,
        TurretDrive,
        Optics,
        Radar,
        Launcher,
        ControlStation,
        Generator
    }

    public enum ModuleState
    {
        Operational,
        Damaged,
        Destroyed
    }

    [Flags]
    public enum KillState
    {
        None = 0,
        Mobility = 1 << 0,
        Firepower = 1 << 1,
        Sensors = 1 << 2,
        Destroyed = 1 << 3
    }

    public enum DestructionCause
    {
        None,
        AmmoDetonation,
        BurnedOut
    }

    public enum WarheadKind
    {
        /// <summary>Directed jet that defeats armor along a line, then spall inside.</summary>
        ShapedCharge,
        /// <summary>Omnidirectional blast and fragments; weak against armor.</summary>
        Fragmentation
    }

    public enum ArmorKind
    {
        /// <summary>Solid armor: reduces penetration by its effective (angle-adjusted) value.</summary>
        Plate,
        /// <summary>One-shot reactive block: multiplies incoming jet penetration.</summary>
        Reactive,
        /// <summary>Cage / net: chance to disrupt a shaped charge.</summary>
        Screen,
        /// <summary>Internal module: absorbs a little penetration and takes damage.</summary>
        Module
    }

    public static class DamageNames
    {
        public static string Ru(ModuleType t) => t switch
        {
            ModuleType.Engine => "Двигатель",
            ModuleType.Transmission => "Трансмиссия",
            ModuleType.Track => "Гусеница",
            ModuleType.FuelTank => "Топливный бак",
            ModuleType.AmmoRack => "Боекомплект",
            ModuleType.Gun => "Орудие",
            ModuleType.Breech => "Казённик",
            ModuleType.TurretDrive => "Привод башни",
            ModuleType.Optics => "Оптика",
            ModuleType.Radar => "РЛС",
            ModuleType.Launcher => "Пусковая",
            ModuleType.ControlStation => "Пост управления",
            ModuleType.Generator => "Генератор",
            _ => t.ToString()
        };

        public static string Ru(ModuleState s) => s switch
        {
            ModuleState.Operational => "цел",
            ModuleState.Damaged => "повреждён",
            ModuleState.Destroyed => "уничтожен",
            _ => s.ToString()
        };

        public static string Ru(KillState k)
        {
            if (k == KillState.None) return "не выведен из строя";
            if ((k & KillState.Destroyed) != 0) return "УНИЧТОЖЕН";
            var parts = new System.Collections.Generic.List<string>();
            if ((k & KillState.Mobility) != 0) parts.Add("обездвижен");
            if ((k & KillState.Firepower) != 0) parts.Add("без огня");
            if ((k & KillState.Sensors) != 0) parts.Add("ослеплён");
            return string.Join(", ", parts);
        }
    }
}
