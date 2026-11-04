namespace ModernDrone.Damage
{
    /// <summary>How a module was struck.</summary>
    public enum HitKind
    {
        /// <summary>Shaped-charge jet: very hot, atomises liquids, initiates explosives.</summary>
        Jet,
        /// <summary>Behind-armor debris.</summary>
        Spall,
        /// <summary>Blast fragments.</summary>
        Fragment,
        /// <summary>Fire heating.</summary>
        Heat
    }

    /// <summary>
    /// Fuel and ammunition fire rules shared by every substance (the per-substance values live in
    /// <see cref="SubstanceData"/> assets, built by Modern Drone/Setup/Build Substance Assets with these sources).
    /// Diesel: flash point 52-96 C (gasoline -45 C), so the vapour over the fuel is too lean to explode and bullets or
    /// fragments rarely light it (en.wikipedia.org/wiki/Flash_point). A shaped-charge jet through a diesel tank throws a
    /// mist fireball; whether a residual fire follows depends on the fuel (DTIC ADA090129 / OSTI 6344813, M48 and M113
    /// tests). Kerosene: TS-1 flash point 28 C, Jet A 38 C, autoignition 210 C (en.wikipedia.org/wiki/Jet_fuel) - lights
    /// more easily than diesel; military helicopter tanks are self-sealing and foam-filled against vapour explosion
    /// (dsiac.dtic.mil, self-sealing fuel tanks). Propellant: nitrocellulose autoignites at ~160-170 C; one burning
    /// charge sets off the next and the overpressure lifts the turret (en.wikipedia.org/wiki/Cooking_off; Zaloga in
    /// taskandpurpose.com). Rocket motors burn fiercely when hit or heated (fast cook-off). High explosive is initiated
    /// by a shaped-charge jet (Held v2d criterion, OSTI LA-8647) but only cooks off after long heating.
    /// Gasoline: flash point about -45 C, autoignition ~280 C (en.wikipedia.org/wiki/Gasoline): the vapour over the
    /// fuel is flammable at almost any temperature, so even fragments and bullets light it far more often than diesel.
    /// </summary>
    public static class Combustion
    {
        /// <summary>Chance that a hit on a holed liquid container leaves it leaking (if it did not catch fire).</summary>
        public static float LeakChance(HitKind kind) => kind switch
        {
            HitKind.Jet => 1f,
            HitKind.Spall => 0.7f,
            HitKind.Fragment => 0.5f,
            _ => 0f
        };

        /// <summary>Self-sealing, foam-filled tanks: fewer fires, most holes close up.</summary>
        public const float ProtectedTankIgnition = 0.4f, ProtectedTankLeak = 0.3f;

        /// <summary>Fraction of its load a holed tank loses per second.</summary>
        public const float LeakRate = 0.04f;

        /// <summary>Fire damage per second at full intensity at the flame centre (before module resistance).</summary>
        public const float FireDamagePerSecond = 8f;

        /// <summary>How quickly fire hurts a module type (electronics and crew fast, steel slowly).</summary>
        public static float FireSensitivity(ModuleType t) => t switch
        {
            ModuleType.Optics => 2f,
            ModuleType.ControlStation => 2f,
            ModuleType.Radar => 1.5f,
            ModuleType.Antenna => 1.5f,
            ModuleType.Wheels => 1.5f,
            ModuleType.Generator => 1.2f,
            ModuleType.TurretDrive => 1.2f,
            ModuleType.Rotor => 0.8f,
            ModuleType.TailRotor => 0.8f,
            ModuleType.Transmission => 0.6f,
            ModuleType.Breech => 0.4f,
            ModuleType.Gun => 0.3f,
            ModuleType.Track => 0.3f,
            _ => 1f
        };
    }
}
