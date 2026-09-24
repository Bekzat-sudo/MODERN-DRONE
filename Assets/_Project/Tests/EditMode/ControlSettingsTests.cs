using ModernDrone.Drone;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class ControlSettingsTests
    {
        [Test]
        public void Bipolar_UsesSeparateScaleEachSideOfCentre()
        {
            // Typical RC axis reported 0..1 with an off-centre neutral.
            var a = new AxisCalibration("stick/x") { min = 0f, center = 0.4f, max = 1f };
            Assert.AreEqual(0f, a.NormalizeBipolar(0.4f), 1e-5f);
            Assert.AreEqual(1f, a.NormalizeBipolar(1f), 1e-5f);
            Assert.AreEqual(-1f, a.NormalizeBipolar(0f), 1e-5f);
            Assert.AreEqual(0.5f, a.NormalizeBipolar(0.7f), 1e-5f);
            Assert.AreEqual(-0.5f, a.NormalizeBipolar(0.2f), 1e-5f);
        }

        [Test]
        public void Bipolar_InvertFlipsSign_AndClampsOutOfRange()
        {
            var a = new AxisCalibration("y") { min = -1f, center = 0f, max = 1f, invert = true };
            Assert.AreEqual(-1f, a.NormalizeBipolar(2f), 1e-5f);
            Assert.AreEqual(0.5f, a.NormalizeBipolar(-0.5f), 1e-5f);
        }

        [Test]
        public void Unipolar_MapsRangeToZeroOne_WithInvert()
        {
            var t = new AxisCalibration("z") { min = -0.9f, max = 0.9f };
            Assert.AreEqual(0f, t.NormalizeUnipolar(-0.9f), 1e-5f);
            Assert.AreEqual(1f, t.NormalizeUnipolar(0.9f), 1e-5f);
            t.invert = true;
            Assert.AreEqual(1f, t.NormalizeUnipolar(-0.9f), 1e-5f);
        }

        [Test]
        public void Json_RoundTripKeepsCalibrationAndBindings()
        {
            var s = new ControlSettings { device = ControlDevice.Gamepad, gpThrottleMode = GamepadThrottleMode.Trigger };
            s.jsRoll.center = 0.37f;
            s.Binding(PilotAction.Arm).gamepad = "<Gamepad>/buttonNorth";

            var back = JsonUtility.FromJson<ControlSettings>(JsonUtility.ToJson(s));
            Assert.AreEqual(ControlDevice.Gamepad, back.device);
            Assert.AreEqual(GamepadThrottleMode.Trigger, back.gpThrottleMode);
            Assert.AreEqual(0.37f, back.jsRoll.center, 1e-5f);
            Assert.AreEqual("<Gamepad>/buttonNorth", back.Binding(PilotAction.Arm).gamepad);
        }

        [Test]
        public void MissingBinding_FallsBackToDefault()
        {
            var s = new ControlSettings { bindings = new ActionBinding[0] };
            Assert.AreEqual("<Keyboard>/space", s.Binding(PilotAction.Arm).keyboard);
            Assert.AreEqual(1, s.bindings.Length, "fallback is stored so edits persist");
        }

        [Test]
        public void DefaultBindings_CoverEveryAction()
        {
            var defaults = ControlSettings.DefaultBindings();
            foreach (PilotAction a in System.Enum.GetValues(typeof(PilotAction)))
                Assert.IsTrue(System.Array.Exists(defaults, b => b.action == a), $"no default for {a}");
        }
    }
}
