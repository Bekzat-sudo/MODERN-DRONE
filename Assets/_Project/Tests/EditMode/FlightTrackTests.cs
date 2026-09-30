using ModernDrone.Missions;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class FlightTrackTests
    {
        [Test]
        public void Add_SkipsSamplesInsideInterval_UnlessForced()
        {
            var track = new FlightTrack(0.1f);
            track.Add(0f, Vector3.zero, Quaternion.identity);
            track.Add(0.05f, Vector3.one, Quaternion.identity);
            Assert.AreEqual(1, track.Count);
            track.Add(0.05f, Vector3.one, Quaternion.identity, force: true);
            Assert.AreEqual(2, track.Count);
            track.Add(0.05f, Vector3.one, Quaternion.identity, force: true); // same time never duplicates
            Assert.AreEqual(2, track.Count);
        }

        [Test]
        public void Add_CollapsesStandingStill_WithoutSmearingTheStart()
        {
            var track = new FlightTrack(0.1f);
            for (int i = 0; i <= 20; i++) track.Add(i * 0.25f, Vector3.zero, Quaternion.identity);
            Assert.AreEqual(2, track.Count);
            track.Add(5.25f, new Vector3(1f, 0f, 0f), Quaternion.identity);
            Assert.AreEqual(3, track.Count);
            // Still parked just before it drove off, not creeping forward since t = 0.
            Assert.AreEqual(0f, track.Sample(4.9f).position.x, 1e-4f);
            Assert.AreEqual(0.5f, track.Sample(5.125f).position.x, 1e-4f);
        }

        [Test]
        public void Motors_AreInterpolated_AndKeepAHoverFromCollapsing()
        {
            var track = new FlightTrack(0.1f);
            track.Add(0f, Vector3.zero, Quaternion.identity, false, new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            track.Add(1f, Vector3.zero, Quaternion.identity, false, new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            // Hovering in place while the throttle changes is not "standing still".
            track.Add(2f, Vector3.zero, Quaternion.identity, false, new Vector4(0.8f, 0.6f, 0.6f, 0.8f));
            Assert.AreEqual(3, track.Count);
            Assert.AreEqual(0.5f, track.Sample(1.5f).motors.x, 1e-4f);
            Assert.AreEqual(0.4f, track.Sample(1.5f).motors.y, 1e-4f);
        }

        [Test]
        public void Sample_InterpolatesAndClamps()
        {
            var track = new FlightTrack(0.1f);
            track.Add(1f, Vector3.zero, Quaternion.identity);
            track.Add(2f, new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            track.Add(3f, new Vector3(10f, 10f, 0f), Quaternion.Euler(0f, 90f, 0f));

            Assert.AreEqual(5f, track.Sample(1.5f).position.x, 1e-4f);
            Assert.AreEqual(45f, track.Sample(1.5f).rotation.eulerAngles.y, 1e-2f);
            Assert.AreEqual(5f, track.Sample(2.5f).position.y, 1e-4f);
            Assert.AreEqual(Vector3.zero, track.Sample(-5f).position);
            Assert.AreEqual(new Vector3(10f, 10f, 0f), track.Sample(99f).position);
        }
    }
}
