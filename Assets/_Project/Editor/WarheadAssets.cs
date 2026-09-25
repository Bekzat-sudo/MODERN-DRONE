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
                w.mass = 0.5f;
                w.length = 0.3f;
            });
            Ensure(TandemPath, w =>
            {
                w.displayName = "Тандемная";
                w.kind = WarheadKind.ShapedCharge;
                w.tandem = true;
                w.penetration = 300f;
                w.mass = 0.8f;
                w.length = 0.4f;
            });
            Ensure(FragmentationPath, w =>
            {
                w.displayName = "Осколочная";
                w.kind = WarheadKind.Fragmentation;
                w.blastRadius = 6f;
                w.blastDamage = 90f;
                w.fragmentPenetration = 25f;
                w.mass = 0.4f;
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

        static void Ensure(string path, System.Action<WarheadData> setup)
        {
            if (AssetDatabase.LoadAssetAtPath<WarheadData>(path) != null) return;
            var w = ScriptableObject.CreateInstance<WarheadData>();
            setup(w);
            AssetDatabase.CreateAsset(w, path);
        }
    }
}
