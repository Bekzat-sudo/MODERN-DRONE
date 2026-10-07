using ModernDrone.Drone;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ModernDrone.Tests
{
    /// <summary>
    /// The Hornet's aerodynamics against its published figures: ~15 kg take-off weight, cruise 100-120 km/h, dash
    /// ~200 km/h, dive 200-300 km/h (euromaidanpress.com 2026-05-22; root-nation.com).
    /// </summary>
    public class FixedWingTests
    {
        const float Mass = 15f;
        DroneConfig _hornet;

        [SetUp]
        public void SetUp()
        {
            _hornet = AssetDatabase.LoadAssetAtPath<DroneConfig>("Assets/_Project/Data/Drones/Hornet.asset");
            Assert.IsNotNull(_hornet, "Hornet.asset missing (run Modern Drone/Setup/Rebuild Everything)");
        }

        [Test]
        public void Hornet_MatchesPublishedSpeeds()
        {
            Assert.IsTrue(_hornet.fixedWing && _hornet.satelliteLink);
            Assert.AreEqual(Mass, _hornet.frameMass + 5f, 0.01f, "airframe + 5 kg warhead = ~15 kg take-off weight");

            float dash = FixedWingAero.MaxLevelSpeed(_hornet, Mass) * 3.6f;
            Assert.That(dash, Is.InRange(180f, 220f), "dash ~200 km/h");
            float cruise = _hornet.cruiseSpeed * 3.6f;
            Assert.That(cruise, Is.InRange(100f, 120f));
            Assert.Greater(_hornet.cruiseSpeed, 1.3f * FixedWingAero.StallSpeed(_hornet, Mass), "cruise well above the stall");
            Assert.That(FixedWingAero.CruiseThrottle(_hornet, Mass), Is.InRange(0.1f, 0.6f));

            Assert.That(FixedWingAero.DiveSpeed(_hornet, Mass, 30f) * 3.6f, Is.InRange(200f, 300f), "dive 200-300 km/h");
            Assert.That(FixedWingAero.DiveSpeed(_hornet, Mass, 45f) * 3.6f, Is.InRange(200f, 300f));
        }

        [Test]
        public void Aero_SignsFollowTheAirflow()
        {
            // Flow from below (angle of attack): lift up, nose pitched down into the flow.
            var flow = new Vector3(0f, -3f, 30f);
            var climbFlow = FixedWingAero.Aero(_hornet, flow);
            Assert.Greater(climbFlow.force.y, 0f);
            Assert.Greater(climbFlow.torque.x, 0f, "+x = nose down");
            // Drag opposes the airflow (lift is across it, so in body axes it may even lean forward).
            Assert.Less(Vector3.Dot(climbFlow.force, flow), 0f, "drag against the flow");

            // Sliding to the right: side force to the left, nose yawed right into the flow.
            var slip = FixedWingAero.Aero(_hornet, new Vector3(3f, 0f, 30f));
            Assert.Less(slip.force.x, 0f);
            Assert.Greater(slip.torque.y, 0f);

            // Past the stall the wing loses lift instead of gaining it.
            float stall = (_hornet.maxLift - _hornet.liftAtZeroAlpha) / _hornet.liftSlope;
            Assert.Less(FixedWingAero.LiftCoefficient(_hornet, stall + 0.2f), FixedWingAero.LiftCoefficient(_hornet, stall));

            // Thrust: static thrust at low speed, power-limited at speed.
            Assert.AreEqual(_hornet.staticThrust, FixedWingAero.Thrust(_hornet, 5f, 1f), 1e-3f);
            Assert.AreEqual(_hornet.thrustPower / 50f, FixedWingAero.Thrust(_hornet, 50f, 1f), 1e-3f);
        }
    }
}
