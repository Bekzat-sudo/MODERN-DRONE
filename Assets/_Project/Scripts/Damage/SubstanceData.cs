using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>How a substance behaves when a vehicle burns or blows up.</summary>
    public enum SubstanceClass
    {
        /// <summary>Nothing to burn.</summary>
        Inert,
        /// <summary>Diesel, petrol, kerosene, oil: leaks, pools, burns with smoke; a jet can flash the vapour.</summary>
        LiquidFuel,
        /// <summary>Gun propellant charges: burn very fast without detonating, jet out of openings, pressure blows hatches.</summary>
        GunPropellant,
        /// <summary>Shells and mines with explosive fill: detonate, chain off one another with delays, fragments.</summary>
        HighExplosive,
        /// <summary>Solid rocket motors (MLRS, ATGM, SAM): fierce burning, dense white smoke, rockets break loose and fly.</summary>
        RocketMotor,
        /// <summary>Missile in a sealed launch canister: burns inside and blows out through the canister's ends.</summary>
        CanisterMissile,
        /// <summary>Small-arms and autocannon rounds: long crackle and pops, no big explosion.</summary>
        SmallArms
    }

    public enum AmountUnit { Litres, Charges, Rounds, Rockets, Missiles, Kilograms }

    /// <summary>
    /// A substance a vehicle module holds, as data: how easily it lights or detonates, how it burns (gameplay values,
    /// abstract, ordered after the sources in <see cref="Combustion"/>), its published fuel physics for sizing the
    /// effects (<see cref="FireScaling"/>), and the effects and sounds it plays. Warheads and explosives stay abstract:
    /// an "equivalent mass" only sizes the visuals.
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Substance", fileName = "Substance")]
    public sealed class SubstanceData : ScriptableObject
    {
        public string displayName = "";
        public SubstanceClass kind = SubstanceClass.LiquidFuel;
        public AmountUnit unit = AmountUnit.Litres;
        [Tooltip("Mass of one unit, kg (a litre of fuel, one charge, one rocket).")]
        public float massPerUnit = 0.84f;

        [Header("Ignition (abstract chances)")]
        [Range(0f, 1f)] public float igniteJet, igniteSpall, igniteFragment;
        [Tooltip("A jet through it throws a short mist fireball even if nothing keeps burning.")]
        public bool mistFireball;
        [Tooltip("Liquid: leaks out of a holed container and pools.")]
        public bool liquid;
        [Tooltip("Fire exposure (seconds at full intensity) before it lights by heat alone.")]
        public float heatToIgnite = float.PositiveInfinity;
        [Tooltip("Halon suppression can put it out (not self-oxidising propellants).")]
        public bool suppressible = true;
        [Tooltip("Burns so fast in a closed hull that the pressure blows hatches and can blow the vehicle apart.")]
        public bool violent;

        [Header("Burning (gameplay)")]
        [Tooltip("Burn time at full load, s.")]
        public float burnSeconds = 30f;
        [Tooltip("Heat output (damage to modules nearby), abstract.")]
        public float intensity = 1f;
        [Tooltip("Reach of the flames inside the vehicle, m.")]
        public float radius = 2f;

        [Header("Detonation")]
        public bool detonates;
        [Range(0f, 1f)] public float detonateJet, detonateSpall;
        [Tooltip("Fire exposure before it cooks off, s at full intensity.")]
        public float heatToDetonate = 18f;
        [Tooltip("Abstract explosive equivalent per unit, kg (sizes the visual fireball only, FireScaling.DetonationDiameter).")]
        public float equivalentPerUnit;

        [Header("Fuel physics (published, see Combustion)")]
        [Tooltip("Heat of combustion, MJ/kg.")]
        public float heatOfCombustion = 44f;
        [Tooltip("Large-pool mass burning rate m''inf, kg/m^2 s (Babrauskas).")]
        public float burningRate = 0.045f;
        [Tooltip("k beta, 1/m (Babrauskas).")]
        public float kBeta = 2.1f;
        [Tooltip("Heat release of a vehicle fire fed by it, MW (abstract, scaled from published vehicle-fire peaks).")]
        public float heatReleaseMW = 4f;
        [Tooltip("Share of the contents that flashes into a fireball at once: the mist a jet atomises from a holed tank (abstract).")]
        public float mistFraction = 0.005f;

        [Header("Look and sound")]
        [Tooltip("Flames and smoke while it burns (out of an opening, or a pool on the ground).")]
        public FxRecipe fire;
        [Tooltip("Flame jet out of an opening when it deflagrates (propellant, rocket motor).")]
        public FxRecipe ventJet;
        [Tooltip("Flash of atomised fuel or of a deflagration.")]
        public FxRecipe flash;
        [Tooltip("When it detonates.")]
        public FxRecipe detonation;
        [Tooltip("Each secondary bang in a burning wreck (one unit going off; empty = the detonation recipe).")]
        public FxRecipe chainBang;
        [Tooltip("Smoke seeping out of a closed hull while it burns inside.")]
        public FxRecipe seep;
        [Tooltip("Crackle, pops and bangs while it cooks off in a wreck (the recipes carry their own sounds).")]
        public SoundBank cookOffSound;

        [Header("Cook-off after destruction (visual)")]
        [Tooltip("Secondary bangs (shells going off one by one), at most.")]
        public int chainCount;
        [Tooltip("Seconds between them (random up to this).")]
        public float chainSpacing = 1.5f;
        [Tooltip("Seconds of crackle and pops (small-arms rounds cooking off).")]
        public float crackleSeconds;
        [Tooltip("Share of rockets that break loose and fly off when their motors light.")]
        [Range(0f, 1f)] public float flyOffShare;

        public float IgniteChance(HitKind hit) => hit switch
        {
            HitKind.Jet => igniteJet,
            HitKind.Spall => igniteSpall,
            HitKind.Fragment => igniteFragment,
            _ => 0f
        };

        public float DetonateChance(HitKind hit) => !detonates ? 0f : hit switch
        {
            HitKind.Jet => detonateJet,
            HitKind.Spall => detonateSpall,
            _ => 0f
        };

        /// <summary>Mass of <paramref name="amount"/> units, kg.</summary>
        public float Mass(float amount) => Mathf.Max(0f, amount) * massPerUnit;

        public bool Burns => kind != SubstanceClass.Inert;
    }
}
