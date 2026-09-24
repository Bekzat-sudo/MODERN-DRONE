using ModernDrone.Damage;
using ModernDrone.Missions;
using ModernDrone.Video;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class JammerStationTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        VehicleModule Add(ModuleType type, float hp)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetParent(_root.transform);
            go.AddComponent<BoxCollider>();
            var m = go.AddComponent<VehicleModule>();
            m.Configure(type, hp, 5f);
            return m;
        }

        [Test]
        public void PowerDropsWithAntennas_AndStopsWithoutGenerator()
        {
            _root = new GameObject("EW");
            var model = _root.AddComponent<VehicleDamageModel>();
            var jammer = _root.AddComponent<SignalJammer>();
            var station = _root.AddComponent<JammerStation>();
            var a = Add(ModuleType.Antenna, 50f);
            var b = Add(ModuleType.Antenna, 50f);
            var gen = Add(ModuleType.Generator, 80f);
            model.Rebuild();

            station.Refresh();
            Assert.IsTrue(jammer.Emitting);
            Assert.AreEqual(1f, jammer.PowerScale, 1e-4f);

            a.ApplyDamage(1000f);
            station.Refresh();
            Assert.IsTrue(jammer.Emitting);
            Assert.AreEqual(0.5f, jammer.PowerScale, 1e-4f);

            b.ApplyDamage(1000f);
            station.Refresh();
            Assert.IsFalse(jammer.Emitting, "all antennas down = sensors kill");

            b.Restore(); a.Restore(); model.Rebuild();
            gen.ApplyDamage(1000f);
            station.Refresh();
            Assert.IsFalse(jammer.Emitting, "no power");
        }
    }
}
