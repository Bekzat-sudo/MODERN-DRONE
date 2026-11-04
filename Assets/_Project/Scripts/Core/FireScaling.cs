using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// Published empirical relations that turn an amount of fuel, propellant or explosive into the size and duration of
    /// what is seen (plain math, unit-tested). Effects are authored at a reference size and scaled by these.
    /// - Fireball of a flashing fuel mass M (kg): maximum diameter D = 5.8 M^(1/3) m, duration t = 0.45 M^(1/3) s for
    ///   M &lt; 30 000 kg (CCPS 1994, "Guidelines for Evaluating the Characteristics of Vapor Cloud Explosions, Flash Fires,
    ///   and BLEVEs"; also TNO Yellow Book). Used for propellant deflagrating in a hull and for atomised fuel.
    /// - Pool fire: mass burning rate m'' = m''inf (1 - exp(-k beta D)) kg/m^2 s (Babrauskas, SFPE Handbook of Fire
    ///   Protection Engineering, ch. "Heat release rates"), heat release Q = m'' dHc A.
    /// - Flame height L = 0.235 Q^(2/5) - 1.02 D (m, Q in kW; Heskestad, SFPE Handbook ch. "Fire plumes, flame height").
    /// - Fireball of a high-explosive detonation: D ~ 3.4 W^(1/3) m for an equivalent charge W (kg) (empirical TNT
    ///   fireball fits, e.g. D(ft) = 8.5 W(lb)^0.341 quoted in DDESB-related literature); explosive amounts in this game
    ///   are abstract equivalents, so this only sizes the visual.
    /// - Fireball of burning propellant Q (kg): d = 3.86 Q^0.32 m, t = 0.299 Q^0.32 s (Baker et al., "Explosion Hazards and
    ///   Evaluation", 1983, as given in IATG 01.80 (2021) table 34).
    /// - Ventilation limit of a fire burning out of an opening of area A (m^2) and height H (m): Q = 1.5 A sqrt(H) MW
    ///   (Kawagoe 1958; Babrauskas, SFPE Handbook, "Heat release rates"). A fire inside a hull can release no more heat
    ///   through a hatch than the air coming in through it can burn.
    /// </summary>
    public static class FireScaling
    {
        /// <summary>Maximum fireball diameter, m, for <paramref name="massKg"/> of flashing fuel (CCPS).</summary>
        public static float FireballDiameter(float massKg) => 5.8f * Mathf.Pow(Mathf.Max(0f, massKg), 1f / 3f);

        /// <summary>Fireball duration, s (CCPS, M &lt; 30 t; above it t = 2.6 M^(1/6)).</summary>
        public static float FireballDuration(float massKg)
        {
            massKg = Mathf.Max(0f, massKg);
            return massKg < 30000f ? 0.45f * Mathf.Pow(massKg, 1f / 3f) : 2.6f * Mathf.Pow(massKg, 1f / 6f);
        }

        /// <summary>Fireball diameter, m, of a detonation of <paramref name="equivalentKg"/> (abstract equivalent charge).</summary>
        public static float DetonationDiameter(float equivalentKg) => 3.4f * Mathf.Pow(Mathf.Max(0f, equivalentKg), 1f / 3f);

        /// <summary>Fireball diameter, m, of <paramref name="massKg"/> of propellant burning at once (Baker / IATG 01.80).</summary>
        public static float PropellantFireballDiameter(float massKg) => 3.86f * Mathf.Pow(Mathf.Max(0f, massKg), 0.32f);

        /// <summary>Duration of a propellant fireball, s (Baker / IATG 01.80).</summary>
        public static float PropellantFireballDuration(float massKg) => 0.299f * Mathf.Pow(Mathf.Max(0f, massKg), 0.32f);

        /// <summary>Most heat, MW, a fire can release through an opening of <paramref name="areaM2"/> (Kawagoe), taking a round opening.</summary>
        public static float VentilationLimit(float areaM2)
        {
            areaM2 = Mathf.Max(0f, areaM2);
            return 1.5f * areaM2 * Mathf.Sqrt(OpeningDiameter(areaM2));
        }

        /// <summary>Diameter, m, of a round opening of <paramref name="areaM2"/>.</summary>
        public static float OpeningDiameter(float areaM2) => Mathf.Sqrt(4f * Mathf.Max(0f, areaM2) / Mathf.PI);

        /// <summary>
        /// Height, m, of the flames out of an opening of <paramref name="areaM2"/> for a fire of <paramref name="heatReleaseMW"/>
        /// inside: its heat release capped by the opening's ventilation limit, through Heskestad's flame height.
        /// </summary>
        public static float OpeningFlameHeight(float heatReleaseMW, float areaM2) =>
            FlameHeight(1000f * Mathf.Min(Mathf.Max(0f, heatReleaseMW), VentilationLimit(areaM2)), OpeningDiameter(areaM2));

        /// <summary>Mass burning rate of a pool of diameter <paramref name="diameter"/> m, kg/m^2 s (Babrauskas).</summary>
        public static float BurningRate(float rateInfinite, float kBeta, float diameter) =>
            rateInfinite * (1f - Mathf.Exp(-kBeta * Mathf.Max(0f, diameter)));

        /// <summary>Heat release rate, kW, of a burning pool (mass rate times heat of combustion times area).</summary>
        public static float PoolHeatRelease(float rateInfinite, float kBeta, float heatOfCombustionMJ, float diameter)
        {
            float area = Mathf.PI * 0.25f * diameter * diameter;
            return BurningRate(rateInfinite, kBeta, diameter) * heatOfCombustionMJ * 1000f * area;
        }

        /// <summary>Mean flame height, m, of a fire releasing <paramref name="heatReleaseKW"/> over a source <paramref name="diameter"/> m wide (Heskestad).</summary>
        public static float FlameHeight(float heatReleaseKW, float diameter) =>
            Mathf.Max(0f, 0.235f * Mathf.Pow(Mathf.Max(0f, heatReleaseKW), 0.4f) - 1.02f * Mathf.Max(0f, diameter));

        /// <summary>Seconds a pool of <paramref name="massKg"/> takes to burn away at diameter <paramref name="diameter"/>.</summary>
        public static float PoolBurnTime(float massKg, float rateInfinite, float kBeta, float diameter)
        {
            float area = Mathf.PI * 0.25f * diameter * diameter;
            float rate = BurningRate(rateInfinite, kBeta, diameter) * area;
            return rate > 0f ? massKg / rate : 0f;
        }
    }
}
