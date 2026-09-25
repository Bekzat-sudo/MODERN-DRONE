using ModernDrone.Drone;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class FlightMathTests
    {
        [Test]
        public void Rates_MatchBetaflightActualRatesFormula()
        {
            var r = new AxisRates(100f, 650f, 0f);
            Assert.AreEqual(0f, RatesProfile.EvaluateDeg(0f, r), 1e-4f);
            Assert.AreEqual(650f, RatesProfile.EvaluateDeg(1f, r), 0.01f);
            // expo 0: 0.5*100 + 550*0.5*0.5 = 187.5
            Assert.AreEqual(187.5f, RatesProfile.EvaluateDeg(0.5f, r), 0.01f);
            Assert.AreEqual(-RatesProfile.EvaluateDeg(0.5f, r), RatesProfile.EvaluateDeg(-0.5f, r), 1e-4f);
        }

        [Test]
        public void Rates_ExpoSoftensCentreButKeepsEndpoint()
        {
            var linear = new AxisRates(100f, 650f, 0f);
            var expo = new AxisRates(100f, 650f, 0.6f);
            Assert.Less(RatesProfile.EvaluateDeg(0.4f, expo), RatesProfile.EvaluateDeg(0.4f, linear));
            Assert.AreEqual(RatesProfile.EvaluateDeg(1f, linear), RatesProfile.EvaluateDeg(1f, expo), 0.01f);
            // Near centre the slope is the centre sensitivity.
            Assert.AreEqual(1f, RatesProfile.EvaluateDeg(0.01f, expo), 0.05f);
        }
        [Test]
        public void Mixer_RoundTripsTorqueWhenUnsaturated()
        {
            var thrust = new float[4];
            var torque = new Vector3(0.3f, 0.05f, -0.2f);
            QuadMixer.Mix(40f, torque, 0.1f, 0.016f, 30f, thrust);

            var back = QuadMixer.TorqueFromThrust(thrust, 0.1f, 0.016f);
            Assert.AreEqual(torque.x, back.x, 1e-4f);
            Assert.AreEqual(torque.y, back.y, 1e-4f);
            Assert.AreEqual(torque.z, back.z, 1e-4f);
            Assert.AreEqual(40f, thrust[0] + thrust[1] + thrust[2] + thrust[3], 1e-3f);
        }

        [Test]
        public void Mixer_PositiveRollTorqueRaisesRightMotors()
        {
            var thrust = new float[4];
            QuadMixer.Mix(40f, new Vector3(0f, 0f, 0.5f), 0.1f, 0.016f, 30f, thrust);
            Assert.Greater(thrust[0], thrust[1]); // FR > FL
            Assert.Greater(thrust[2], thrust[3]); // RR > RL
        }

        [Test]
        public void Mixer_SaturationStaysInRangeAndKeepsTorqueSign()
        {
            var thrust = new float[4];
            QuadMixer.Mix(0f, new Vector3(0f, 0f, 50f), 0.1f, 0.016f, 20f, thrust);
            foreach (float f in thrust)
                Assert.That(f, Is.InRange(0f, 20f));
            Assert.Greater(QuadMixer.TorqueFromThrust(thrust, 0.1f, 0.016f).z, 0f);
        }

        [Test]
        public void AngleMode_DrivesTiltedAttitudeBackToLevel()
        {
            // Integrate body rates from the angle controller and check the attitude converges.
            Quaternion q = Quaternion.Euler(30f, 0f, -20f);
            const float dt = 0.004f;
            for (int i = 0; i < 1000; i++)
            {
                Vector3 upBody = Quaternion.Inverse(q) * Vector3.up;
                Vector3 w = FlightController.AngleModeRate(0f, 0f, upBody, 55f, 8f, 10f);
                if (w.sqrMagnitude > 0f)
                    q = q * Quaternion.AngleAxis(w.magnitude * dt * Mathf.Rad2Deg, w.normalized);
            }
            Assert.Less(Vector3.Angle(q * Vector3.up, Vector3.up), 0.5f);
        }

        [Test]
        public void AngleMode_ReachesCommandedRollAndPitch()
        {
            Quaternion q = Quaternion.identity;
            const float dt = 0.004f;
            for (int i = 0; i < 1000; i++)
            {
                Vector3 upBody = Quaternion.Inverse(q) * Vector3.up;
                Vector3 w = FlightController.AngleModeRate(0.5f, 0.5f, upBody, 50f, 8f, 10f);
                if (w.sqrMagnitude > 0f)
                    q = q * Quaternion.AngleAxis(w.magnitude * dt * Mathf.Rad2Deg, w.normalized);
            }
            Vector3 right = q * Vector3.right;
            Vector3 fwd = q * Vector3.forward;
            Assert.Less(right.y, -0.3f, "stick right should dip the right side");
            Assert.Less(fwd.y, -0.3f, "stick forward should dip the nose");
        }

        [Test]
        public void Payload_RaisesHoverThrottleAndInertia_ThrustUnchanged()
        {
            var cfg = ScriptableObject.CreateInstance<DroneConfig>();
            float thrust = cfg.MaxThrustPerMotor;
            float bare = cfg.HoverThrottleFor(cfg.frameMass);
            float loaded = cfg.HoverThrottleFor(cfg.frameMass + 0.5f);
            Assert.Greater(loaded, bare);
            // thrust = throttle^2 * max must equal the loaded weight
            Assert.AreEqual((cfg.frameMass + 0.5f) * 9.81f, loaded * loaded * thrust * QuadMixer.MotorCount, 0.05f);
            Assert.AreEqual(thrust, cfg.MaxThrustPerMotor);
            var add = cfg.PayloadInertia(0.5f, 0.3f);
            Assert.Greater(add.x, add.z); // a long payload along the frame axis resists pitch more than roll
            Assert.Greater(add.y, 0f);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Battery_SagsUnderLoadAndDrains()
        {
            var b = new BatteryModel(6, 3000f, 0.05f);
            Assert.AreEqual(25.2f, b.Voltage, 0.01f);
            b.Update(100f, 1f);
            Assert.AreEqual(25.2f - 5f, b.Voltage, 0.2f);
            Assert.AreEqual(100f / 3.6f, b.UsedMah, 0.01f);
            Assert.Less(b.ThrustFactor, 1f);
        }
    }
}
