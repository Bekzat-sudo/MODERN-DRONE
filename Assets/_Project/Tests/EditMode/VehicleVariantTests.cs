using ModernDrone.Damage;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class VehicleVariantTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        VehicleDamageModel NewVehicle()
        {
            _root = new GameObject("V");
            return _root.AddComponent<VehicleDamageModel>();
        }

        VehicleModule Add(ModuleType type, float hp = 50f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(_root.transform);
            var m = go.AddComponent<VehicleModule>();
            m.Configure(type, hp, 5f);
            return m;
        }

        [Test]
        public void DestroyedWheels_GiveMobilityKill()
        {
            var model = NewVehicle();
            var wheels = Add(ModuleType.Wheels);
            model.Rebuild();
            wheels.ApplyDamage(1000f);
            Assert.AreNotEqual(KillState.None, model.State & KillState.Mobility);
        }

        [Test]
        public void LaunchVehicle_LosesFirepowerOnlyWhenAllLaunchersGone()
        {
            var model = NewVehicle();
            model.SetRandom(new System.Random(123));
            var a = Add(ModuleType.Launcher);
            var b = Add(ModuleType.Launcher);
            model.Rebuild();
            a.ApplyDamage(1000f);
            Assert.AreEqual(KillState.None, model.State & KillState.Firepower);
            b.ApplyDamage(1000f);
            Assert.AreNotEqual(KillState.None, model.State & KillState.Firepower);
        }

        [Test]
        public void Decoy_IsReportedAsFalseTarget_AndRaisesFirstStruck()
        {
            var model = NewVehicle();
            model.SetDisplayName("Танк");
            model.SetDecoy(true);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.transform.SetParent(_root.transform);
            plate.transform.localScale = new Vector3(3f, 2f, 0.1f);
            plate.AddComponent<ArmorElement>().Configure(ArmorKind.Plate, 1f);
            model.Rebuild();
            Physics.SyncTransforms();

            int struck = 0;
            model.FirstStruck += _ => struck++;
            var w = ScriptableObject.CreateInstance<WarheadData>();
            w.blastRadius = 0f;
            var report = DamageSolver.Detonate(w, new Vector3(0f, 0f, -0.05f), Vector3.forward, new System.Random(1));
            DamageSolver.Detonate(w, new Vector3(0f, 0f, -0.05f), Vector3.forward, new System.Random(2));
            Object.DestroyImmediate(w);

            Assert.IsTrue(report.HitDecoy);
            StringAssert.Contains("ЛОЖНАЯ ЦЕЛЬ", report.Summary());
            Assert.AreEqual(1, struck, "first-strike event fires once");
        }
    }
}
