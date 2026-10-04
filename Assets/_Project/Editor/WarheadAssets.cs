using ModernDrone.Damage;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>Creates the default gameplay warhead presets (abstract balance numbers).</summary>
    public static class WarheadAssets
    {
        const string Dir = "Assets/_Project/Data/Warheads";
        public const string ShapedChargePath = Dir + "/ShapedCharge_Basic.asset";
        public const string TandemPath = Dir + "/ShapedCharge_Tandem.asset";
        public const string FragmentationPath = Dir + "/Fragmentation_Basic.asset";

        [MenuItem("Modern Drone/Setup/Create Warhead Presets")]
        public static void CreateAll()
        {
            Ensure(ShapedChargePath, w =>
            {
                w.displayName = "Кумулятивная";
                w.kind = WarheadKind.ShapedCharge;
                w.mass = 2.2f; // PG-7 grenade on an FPV, 2.2 kg (war-sanctions.gur.gov.ua/en/uav/486, KVN)
                w.length = 0.3f;
            });
            Ensure(TandemPath, w =>
            {
                w.displayName = "Тандемная";
                w.kind = WarheadKind.ShapedCharge;
                w.tandem = true;
                w.penetration = 300f;
                w.mass = 4.5f; // PG-7VR tandem, 4.5 kg (en.wikipedia.org/wiki/RPG-7): a load for 10" and up
                w.length = 0.4f;
            });
            Ensure(FragmentationPath, w =>
            {
                w.displayName = "Осколочная";
                w.kind = WarheadKind.Fragmentation;
                w.blastRadius = 6f;
                w.blastDamage = 90f;
                w.fragmentPenetration = 25f;
                w.mass = 1.35f; // OG-7V fragmentation grenade, 1.35 kg (vmz.bg, OG-7V)
                w.length = 0.2f;
            });
            AssetDatabase.SaveAssets();
        }

        public static WarheadData Load(string path)
        {
            var w = AssetDatabase.LoadAssetAtPath<WarheadData>(path);
            if (w == null)
            {
                CreateAll();
                w = AssetDatabase.LoadAssetAtPath<WarheadData>(path);
            }
            return w;
        }

        /// <summary>Creates the preset if needed and (re)applies its values, so they stay defined in code.</summary>
        static void Ensure(string path, System.Action<WarheadData> setup)
        {
            var w = AssetDatabase.LoadAssetAtPath<WarheadData>(path);
            bool created = w == null;
            if (created) w = ScriptableObject.CreateInstance<WarheadData>();
            setup(w);
            if (created) AssetDatabase.CreateAsset(w, path);
            else EditorUtility.SetDirty(w);
        }
    }
}
