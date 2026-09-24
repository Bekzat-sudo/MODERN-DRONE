using System.Collections;
using ModernDrone.Drone;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernDrone.Tests
{
    /// <summary>
    /// Closed-loop checks on the real rigid-body simulation, stepped manually for determinism.
    /// These guard the sign conventions between sticks, mixer and Unity physics.
    /// </summary>
    public class DroneFlightTests
    {
        const float Dt = 1f / 250f;

        DroneController _drone;
        DroneConfig _config;
        SimulationMode _prevMode;

        [SetUp]
        public void SetUp()
        {
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            _config = ScriptableObject.CreateInstance<DroneConfig>();
            var go = new GameObject("TestDrone");
            go.transform.position = new Vector3(0f, 200f, 0f);
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>().size = _config.colliderSize;
            _drone = go.AddComponent<DroneController>();
            _drone.Initialize(_config);
            _drone.ManualStepping = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_drone.gameObject);
            Object.Destroy(_config);
            Physics.simulationMode = _prevMode;
        }

        void Arm(FlightMode mode)
        {
            _drone.SetInput(new DroneInputState { mode = mode });
            _drone.SetInput(new DroneInputState { mode = mode, armSwitch = true });
            Assert.IsTrue(_drone.Armed);
        }

        void Fly(DroneInputState input, float seconds)
        {
            input.armSwitch = true;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                _drone.SetInput(input);
                _drone.Step(Dt);
                Physics.Simulate(Dt);
            }
        }

        float Hover => _config.HoverThrottle;

        [UnityTest]
        public IEnumerator ArmingRequiresLowThrottle()
        {
            _drone.SetInput(new DroneInputState { throttle = 0.5f });
            _drone.SetInput(new DroneInputState { throttle = 0.5f, armSwitch = true });
            Assert.IsFalse(_drone.Armed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArmingWithSpringThrottleAtRest_IsAllowed_AndBlockReasonReported()
        {
            _drone.SetInput(new DroneInputState { throttle = 0.5f });
            _drone.SetInput(new DroneInputState { throttle = 0.5f, armSwitch = true });
            Assert.IsFalse(_drone.Armed);
            Assert.AreEqual(ArmBlockReason.Throttle, _drone.ArmBlock);

            _drone.SetInput(new DroneInputState { throttle = 0.5f });
            Assert.AreEqual(ArmBlockReason.None, _drone.ArmBlock, "releasing the switch clears the reason");
            _drone.SetInput(new DroneInputState { throttle = 0.5f, throttleAtRest = true, armSwitch = true });
            Assert.IsTrue(_drone.Armed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LinkLoss_HoldsBriefly_ThenFailsafeDisarms()
        {
            bool fired = false;
            _drone.OnFailsafe += () => fired = true;
            Arm(FlightMode.Angle);
            _drone.SetLinkQuality(0f);
            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = Hover }, 0.3f);
            Assert.IsTrue(_drone.Armed, "short dropouts are ridden through");
            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = Hover }, 0.4f);
            Assert.IsFalse(_drone.Armed);
            Assert.IsTrue(_drone.Failsafe && fired);

            _drone.SetLinkQuality(1f);
            _drone.SetInput(new DroneInputState());
            _drone.SetInput(new DroneInputState { armSwitch = true });
            Assert.IsFalse(_drone.Armed, "cannot re-arm after failsafe without a reset");
            Assert.AreEqual(ArmBlockReason.Failsafe, _drone.ArmBlock);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AngleMode_HoverStaysLevel()
        {
            Arm(FlightMode.Angle);
            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = Hover }, 3f);
            Assert.Less(Vector3.Angle(_drone.transform.up, Vector3.up), 2f);
            Assert.Less(_drone.Body.angularVelocity.magnitude, 0.2f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoverThrottle_RoughlyHoldsAltitude()
        {
            Arm(FlightMode.Angle);
            float y0 = _drone.transform.position.y;
            Fly(new DroneInputState { mode = FlightMode.Angle, throttle = Hover }, 2f);
            Assert.Less(Mathf.Abs(_drone.transform.position.y - y0), 6f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Acro_RollRightStickRollsRight()
        {
            Arm(FlightMode.Acro);
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover, roll = 0.3f }, 0.15f);
            Assert.Less(_drone.transform.right.y, -0.05f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Acro_PitchForwardStickDipsNose()
        {
            Arm(FlightMode.Acro);
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover, pitch = 0.3f }, 0.15f);
            Assert.Less(_drone.transform.forward.y, -0.05f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Acro_YawRightStickTurnsRight()
        {
            Arm(FlightMode.Acro);
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover, yaw = 0.5f }, 0.3f);
            Assert.Greater(_drone.transform.forward.x, 0.05f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Acro_RateTracksSetpoint()
        {
            Arm(FlightMode.Acro);
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover, roll = 0.5f }, 0.4f);
            float target = RatesProfile.EvaluateRad(0.5f, _config.rollRates);
            float actual = -_drone.transform.InverseTransformDirection(_drone.Body.angularVelocity).z;
            Assert.AreEqual(target, actual, target * 0.1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Acro_HoldsAttitudeWithCentredSticks()
        {
            Arm(FlightMode.Acro);
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover, roll = 0.3f }, 0.2f);
            Quaternion before = _drone.transform.rotation;
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover }, 0.5f);
            // After the stick is released the rate decays; attitude must stop changing.
            Quaternion mid = _drone.transform.rotation;
            Fly(new DroneInputState { mode = FlightMode.Acro, throttle = Hover }, 0.5f);
            Assert.Less(Quaternion.Angle(mid, _drone.transform.rotation), 1.5f);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, before), 5f);
            yield return null;
        }
    }
}
