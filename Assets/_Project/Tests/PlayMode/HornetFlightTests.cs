using System.Collections;
using ModernDrone.Drone;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernDrone.Tests
{
    /// <summary>The Hornet on the real rigid-body simulation: it cruises, dives and turns like an aeroplane.</summary>
    public class HornetFlightTests
    {
        const float Dt = 1f / 250f;

        DroneController _drone;
        SimulationMode _prevMode;

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            var hornet = UnityEditor.AssetDatabase.LoadAssetAtPath<DroneConfig>("Assets/_Project/Data/Drones/Hornet.asset");
#else
            DroneConfig hornet = null;
#endif
            if (hornet == null) Assert.Ignore("Hornet.asset missing (run Modern Drone/Setup/Rebuild Everything)");
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var go = new GameObject("TestHornet");
            go.transform.position = new Vector3(0f, 300f, 0f);
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>().size = hornet.colliderSize;
            _drone = go.AddComponent<DroneController>();
            _drone.Initialize(hornet);
            _drone.SetPayload(5f, 0.4f);
            _drone.ManualStepping = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_drone == null) return;
            Object.Destroy(_drone.gameObject);
            Physics.simulationMode = _prevMode;
        }

        void Fly(DroneInputState input, float seconds)
        {
            for (int i = 0, n = Mathf.RoundToInt(seconds / Dt); i < n; i++)
            {
                _drone.SetInput(input);
                _drone.Step(Dt);
                Physics.Simulate(Dt);
            }
        }

        [UnityTest]
        public IEnumerator WaitsAtTheLaunchPoint_ThenCruisesLevel()
        {
            Vector3 start = _drone.transform.position;
            Fly(new DroneInputState { mode = FlightMode.Angle }, 1f);
            Assert.AreEqual(start, _drone.transform.position, "held in the air until launched");

            _drone.Launch();
            Assert.IsTrue(_drone.Armed);
            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = _drone.HoverThrottle }, 10f);
            var v = _drone.Body.linearVelocity;
            Assert.That(v.magnitude, Is.InRange(25f, 40f), "holds about cruise speed");
            Assert.Less(Mathf.Abs(_drone.transform.position.y - start.y), 40f, "roughly level");
            Assert.Greater(_drone.transform.position.z - start.z, 250f, "flies ahead");
            Assert.Less(Vector3.Angle(_drone.transform.up, Vector3.up), 20f, "upright");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DivesFast_AndTurnsWhenBanked()
        {
            _drone.Launch();
            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = _drone.HoverThrottle, roll = 0.6f }, 6f);
            float heading = Mathf.Atan2(_drone.transform.forward.x, _drone.transform.forward.z) * Mathf.Rad2Deg;
            Assert.Greater(heading, 20f, "banking right turns right");

            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = 0f, pitch = 0.8f }, 8f);
            Assert.Less(_drone.Body.linearVelocity.y, -10f, "diving");
            Assert.Greater(_drone.Body.linearVelocity.magnitude * 3.6f, 180f, "gains speed in the dive");
            yield return null;
        }
    }
}
