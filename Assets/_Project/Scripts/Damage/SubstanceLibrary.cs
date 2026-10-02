using UnityEngine;

namespace ModernDrone.Damage
{
    /// <summary>
    /// The standard substances and what a module of each type holds unless its vehicle says otherwise
    /// (Resources/SubstanceLibrary, built by Modern Drone/Setup/Build Substance Assets).
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/Substance Library", fileName = "SubstanceLibrary")]
    public sealed class SubstanceLibrary : ScriptableObject
    {
        public SubstanceData diesel, gasoline, jetFuel, oil, gunPropellant, highExplosiveShells, rocketMotor, canisterMissile, smallArms;

        [Header("Default amounts")]
        public float fuelLitres = 400f, oilLitres = 30f, propellantCharges = 20f, shells = 20f, rockets = 4f;

        static SubstanceLibrary _instance;

        public static SubstanceLibrary Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<SubstanceLibrary>("SubstanceLibrary");
                return _instance;
            }
        }

        /// <summary>What a module type holds by default (burning contents).</summary>
        public SubstanceData DefaultContents(ModuleType t) => t switch
        {
            ModuleType.FuelTank => diesel,
            ModuleType.AmmoRack => gunPropellant,
            ModuleType.Launcher => rocketMotor,
            ModuleType.Engine => oil,
            ModuleType.Transmission => oil,
            _ => null
        };

        /// <summary>Explosive fill a module type carries by default (shells next to the charges).</summary>
        public SubstanceData DefaultExplosive(ModuleType t) => t == ModuleType.AmmoRack || t == ModuleType.Launcher ? highExplosiveShells : null;

        public float DefaultAmount(ModuleType t) => t switch
        {
            ModuleType.FuelTank => fuelLitres,
            ModuleType.AmmoRack => propellantCharges,
            ModuleType.Launcher => rockets,
            ModuleType.Engine => oilLitres,
            ModuleType.Transmission => oilLitres,
            _ => 0f
        };

        public float DefaultExplosiveCount(ModuleType t) => t == ModuleType.AmmoRack ? shells : t == ModuleType.Launcher ? rockets : 0f;
    }
}
