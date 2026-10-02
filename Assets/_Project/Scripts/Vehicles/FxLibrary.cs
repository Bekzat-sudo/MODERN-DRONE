using ModernDrone.Core;
using UnityEngine;

namespace ModernDrone.Vehicles
{
    /// <summary>
    /// The standard effect recipes: explosions by kind, fires, seeping smoke, propellant jets, smoke screens
    /// (Resources/FxLibrary, built by Modern Drone/Setup/Build Substance Assets). Substances and vehicles may name their
    /// own recipes; these are what plays when they do not.
    /// </summary>
    [CreateAssetMenu(menuName = "Modern Drone/FX Library", fileName = "FxLibrary")]
    public sealed class FxLibrary : ScriptableObject
    {
        [Header("Explosions by kind")]
        public FxRecipe ground;
        public FxRecipe armor, air, era, fuel, ammo;

        [Header("Fires")]
        public FxRecipe fire;
        public FxRecipe fireGrey, seep, seepGrey, jet, smokeScreen;
        [Tooltip("Short tongue out of the gun's muzzle while the charges burn.")]
        public FxRecipe muzzleJet;

        static FxLibrary _instance;

        public static FxLibrary Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<FxLibrary>("FxLibrary");
                return _instance;
            }
        }

        public FxRecipe Explosion(ExplosionKind kind) => kind switch
        {
            ExplosionKind.Ground => ground,
            ExplosionKind.Armor => armor,
            ExplosionKind.Air => air,
            ExplosionKind.Era => era,
            ExplosionKind.Fuel => fuel,
            ExplosionKind.Ammo => ammo,
            _ => armor,
        };
    }
}
