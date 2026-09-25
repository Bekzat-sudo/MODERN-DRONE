namespace ModernDrone.Damage
{
    /// <summary>What a module holds that can burn or explode.</summary>
    public enum Combustible
    {
        None,
        /// <summary>Diesel / multi-fuel (T-72 V-92, BMP-3 UTD-29, KamAZ, BAZ, Ural YaMZ engines).</summary>
        Diesel,
        /// <summary>Aviation kerosene TS-1 / Jet A (helicopter turboshafts).</summary>
        JetFuel,
        /// <summary>Engine / gearbox oil and residual fuel in engine lines.</summary>
        Oil,
        /// <summary>Gun propellant charges (nitrocellulose, partly combustible cases).</summary>
        Propellant,
        /// <summary>Solid rocket motors (MLRS rockets, SAM missiles).</summary>
        RocketPropellant
    }

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

    /// <summary>Burning behaviour of one combustible. Chances and burn figures are abstract gameplay values, ordered after the sources below.</summary>
    public readonly struct CombustionProfile
    {
        /// <summary>Chance that a hit starts a sustained fire (jet, spall, fragment).</summary>
        public readonly float igniteJet, igniteSpall, igniteFragment;
        /// <summary>A jet through the liquid throws a short mist fireball even if nothing keeps burning.</summary>
        public readonly bool mistFireball;
        /// <summary>Liquid that leaks out of a holed container.</summary>
        public readonly bool liquid;
        /// <summary>Fire exposure (seconds at full intensity) before it ignites by heat alone.</summary>
        public readonly float heatToIgnite;
        /// <summary>Burn time at full load, s; heat output and reach of the flames.</summary>
        public readonly float burnSeconds, intensity, radius;
        /// <summary>Burns so fast in a closed hull that it can blow the vehicle apart (deflagration).</summary>
        public readonly bool violent;
        /// <summary>Halon crew/engine suppression can put it out (not self-oxidising propellants).</summary>
        public readonly bool suppressible;

        public CombustionProfile(float jet, float spall, float fragment, bool mist, bool liquid, float heatToIgnite,
            float burnSeconds, float intensity, float radius, bool violent, bool suppressible)
        {
            igniteJet = jet; igniteSpall = spall; igniteFragment = fragment;
            mistFireball = mist; this.liquid = liquid; this.heatToIgnite = heatToIgnite;
            this.burnSeconds = burnSeconds; this.intensity = intensity; this.radius = radius;
            this.violent = violent; this.suppressible = suppressible;
        }

        public float IgniteChance(HitKind kind) => kind switch
        {
            HitKind.Jet => igniteJet,
            HitKind.Spall => igniteSpall,
            HitKind.Fragment => igniteFragment,
            _ => 0f
        };
    }

    /// <summary>
    /// Fuel and ammunition fire rules.
    /// Diesel: flash point 52-96 C (gasoline -45 C), so the vapour over the fuel is too lean to explode and bullets or
    /// fragments rarely light it (en.wikipedia.org/wiki/Flash_point). A shaped-charge jet through a diesel tank throws a
    /// mist fireball; whether a residual fire follows depends on the fuel (DTIC ADA090129 / OSTI 6344813, M48 and M113
    /// tests). Kerosene: TS-1 flash point 28 C, Jet A 38 C, autoignition 210 C (en.wikipedia.org/wiki/Jet_fuel) - lights
    /// more easily than diesel; military helicopter tanks are self-sealing and foam-filled against vapour explosion
    /// (dsiac.dtic.mil, self-sealing fuel tanks). Propellant: nitrocellulose autoignites at ~160-170 C; one burning
    /// charge sets off the next and the overpressure lifts the turret (en.wikipedia.org/wiki/Cooking_off; Zaloga in
    /// taskandpurpose.com). Rocket motors burn fiercely when hit or heated (fast cook-off). High explosive is initiated
    /// by a shaped-charge jet (Held v2d criterion, OSTI LA-8647) but only cooks off after long heating.
    /// </summary>
    public static class Combustion
    {
        static readonly CombustionProfile NoneProfile = new CombustionProfile(0f, 0f, 0f, false, false, float.PositiveInfinity, 0f, 0f, 0f, false, true);
        static readonly CombustionProfile DieselProfile = new CombustionProfile(0.35f, 0.08f, 0.02f, true, true, 6f, 40f, 1.0f, 2.2f, false, true);
        static readonly CombustionProfile JetFuelProfile = new CombustionProfile(0.55f, 0.2f, 0.08f, true, true, 3f, 25f, 1.2f, 2.5f, false, true);
        static readonly CombustionProfile OilProfile = new CombustionProfile(0.25f, 0.05f, 0.01f, false, true, 5f, 15f, 0.6f, 1.5f, false, true);
        static readonly CombustionProfile PropellantProfile = new CombustionProfile(0.9f, 0.5f, 0.15f, false, false, 12f, 8f, 3.0f, 3.5f, true, false);
        static readonly CombustionProfile RocketProfile = new CombustionProfile(0.85f, 0.45f, 0.15f, false, false, 10f, 12f, 2.5f, 4.0f, false, false);

        public static CombustionProfile Of(Combustible c) => c switch
        {
            Combustible.Diesel => DieselProfile,
            Combustible.JetFuel => JetFuelProfile,
            Combustible.Oil => OilProfile,
            Combustible.Propellant => PropellantProfile,
            Combustible.RocketPropellant => RocketProfile,
            _ => NoneProfile
        };

        /// <summary>Chance that a hit on a holed liquid container leaves it leaking (if it did not catch fire).</summary>
        public static float LeakChance(HitKind kind) => kind switch
        {
            HitKind.Jet => 1f,
            HitKind.Spall => 0.7f,
            HitKind.Fragment => 0.5f,
            _ => 0f
        };

        /// <summary>Chance that a hit detonates high explosive (shells, warheads). Fire cooks it off only after long heating.</summary>
        public static float DetonationChance(HitKind kind) => kind switch
        {
            HitKind.Jet => 0.35f, // mixed loads: many rounds are not HE, and the shell casing blunts the jet
            HitKind.Spall => 0.05f,
            _ => 0f
        };

        /// <summary>Fire exposure (seconds at full intensity) before high explosive cooks off.</summary>
        public const float HeatToDetonate = 18f;

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

        /// <summary>What a module type holds unless a builder says otherwise.</summary>
        public static Combustible DefaultFor(ModuleType t) => t switch
        {
            ModuleType.FuelTank => Combustible.Diesel,
            ModuleType.AmmoRack => Combustible.Propellant,
            ModuleType.Launcher => Combustible.RocketPropellant,
            ModuleType.Engine => Combustible.Oil,
            ModuleType.Transmission => Combustible.Oil,
            _ => Combustible.None
        };

        public static bool DefaultHighExplosive(ModuleType t) => t == ModuleType.AmmoRack || t == ModuleType.Launcher;
    }
}
