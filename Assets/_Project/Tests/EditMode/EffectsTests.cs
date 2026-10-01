using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class EffectsTests
    {
        static VentPicker.Vent V(Vector3 p, float area, bool open, VentKind kind = VentKind.Hatch) =>
            new VentPicker.Vent { position = p, normal = Vector3.up, area = area, open = open, kind = kind };

        [Test]
        public void FlamesLeaveThroughTheNearestOpenHatch()
        {
            var vents = new List<VentPicker.Vent>
            {
                V(new Vector3(0.6f, 2f, 0f), 0.3f, true),   // commander's hatch, open
                V(new Vector3(-0.5f, 2f, 0f), 0.3f, false),  // gunner's hatch, shut
                V(new Vector3(0f, 1.4f, -2f), 0.8f, true, VentKind.Grille),
            };
            int i = VentPicker.Nearest(new Vector3(0.4f, 1.2f, 0.2f), vents);
            Assert.AreEqual(0, i);
        }

        [Test]
        public void ShutOpeningsAndSeamsNeverCarryFlames()
        {
            var vents = new List<VentPicker.Vent>
            {
                V(new Vector3(0f, 2f, 0f), 0.3f, false),
                V(new Vector3(0f, 2.1f, 0f), 0.05f, false, VentKind.Seam),
            };
            var shares = new List<(int, float)>();
            VentPicker.Share(Vector3.up, vents, shares);
            Assert.IsEmpty(shares);
            Assert.AreEqual(-1, VentPicker.Nearest(Vector3.up, vents));
            // ... but smoke seeps out of the seam.
            Assert.AreEqual(1, VentPicker.Seep(Vector3.up, vents));
        }

        [Test]
        public void FlamesAreSharedByAreaAndNearness()
        {
            var vents = new List<VentPicker.Vent>
            {
                V(new Vector3(0f, 2f, 0f), 3f, true, VentKind.TurretRing), // big and close
                V(new Vector3(0.6f, 2f, 0f), 0.3f, true),
                V(new Vector3(0f, 1.2f, 2.5f), 0.01f, true, VentKind.Hole), // tiny and far: dropped
            };
            var shares = new List<(int index, float share)>();
            VentPicker.Share(new Vector3(0f, 1.2f, 0f), vents, shares);
            Assert.AreEqual(0, shares[0].index);
            Assert.Greater(shares[0].share, 0.7f);
            float sum = 0f;
            foreach (var s in shares) sum += s.share;
            Assert.AreEqual(1f, sum, 1e-4f);
            Assert.IsFalse(shares.Exists(s => s.index == 2));
        }

        [Test]
        public void PropellantPressureBlowsOnlyNearbyShutHatches()
        {
            var vents = new List<VentPicker.Vent>
            {
                V(new Vector3(0.6f, 2f, 0f), 0.3f, false),
                V(new Vector3(0f, 1.4f, 2.1f), 0.2f, false),
                V(new Vector3(0f, 1.4f, 6f), 0.2f, false),             // out of reach
                V(new Vector3(-0.5f, 2f, 0f), 0.3f, true),             // already open
                V(new Vector3(0f, 1.4f, -2f), 0.8f, false, VentKind.Seam),
            };
            var blown = new List<int>();
            VentPicker.Blown(new Vector3(0f, 1f, 0.5f), vents, blown);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, blown);
        }

        [Test]
        public void SoundArrivesAtTheSpeedOfSoundAndLosesItsHighs()
        {
            Assert.AreEqual(1f, SoundPropagation.Delay(343f), 1e-4f);
            Assert.AreEqual(350f / 343f, SoundPropagation.Delay(350f), 1e-4f);
            Assert.AreEqual(0f, SoundPropagation.Delay(-5f));
            Assert.AreEqual(22000f, SoundPropagation.Cutoff(0f), 1f);
            Assert.Less(SoundPropagation.Cutoff(1000f), 2500f);
            Assert.Greater(SoundPropagation.Cutoff(1000f), 1500f);
            Assert.AreEqual(700f, SoundPropagation.Cutoff(10000f), 1f);
        }

        [Test]
        public void FarBlastsAreMostlyTheBoom()
        {
            Assert.AreEqual(0f, SoundPropagation.FarMix(20f, 1f), 1e-4f);
            Assert.AreEqual(1f, SoundPropagation.FarMix(400f, 1f), 1e-4f);
            // A bigger charge keeps its crack further out.
            Assert.Less(SoundPropagation.FarMix(150f, 8f), SoundPropagation.FarMix(150f, 1f));
        }

        [Test]
        public void EchoComesAfterTheDirectSound()
        {
            float d = 350f, reflector = 200f;
            float extra = SoundPropagation.EchoDelay(d, reflector);
            Assert.Greater(extra, 0f);
            Assert.AreEqual((Mathf.Sqrt(d * d + 4f * reflector * reflector) - d) / 343f, extra, 1e-4f);
        }

        [Test]
        public void BlastFrontReachesTreesInOrderAndFades()
        {
            Assert.Less(Shockwaves.Arrival(10f), Shockwaves.Arrival(40f));
            Assert.AreEqual(36f / 360f, Shockwaves.Arrival(36f), 1e-4f);
            Assert.AreEqual(0f, Shockwaves.Push(1f, 10f, -0.01f));         // not there yet
            Assert.Greater(Shockwaves.Push(1f, 10f, 0.05f), 0f);           // shoved away
            Assert.Greater(Shockwaves.Push(1f, 5f, 0.05f), Shockwaves.Push(1f, 30f, 0.05f));
            Assert.AreEqual(0f, Shockwaves.Push(1f, 10f, Shockwaves.Ring + 0.1f));
        }

        [Test]
        public void SmokeBlocksTheViewButNotTheThermalSight()
        {
            SmokeVolumes.Clear();
            try
            {
                var a = new Vector3(0f, 2f, -50f);
                var b = new Vector3(0f, 2f, 50f);
                Assert.AreEqual(1f, SmokeVolumes.Transmittance(a, b), 1e-5f);
                SmokeVolumes.Add(new Vector3(0f, 2f, 0f), 9f, 1.5f, float.MaxValue);
                float visible = SmokeVolumes.Transmittance(a, b);
                float thermal = SmokeVolumes.Transmittance(a, b, thermal: true);
                Assert.Less(visible, SmokeVolumes.SeeThreshold);
                Assert.Greater(thermal, SmokeVolumes.SeeThreshold);
                // A line that passes beside the cloud is clear.
                Assert.AreEqual(1f, SmokeVolumes.Transmittance(a + Vector3.right * 20f, b + Vector3.right * 20f), 1e-5f);
                SmokeVolumes.Prune(float.MaxValue);
                Assert.AreEqual(0, SmokeVolumes.Volumes.Count);
            }
            finally { SmokeVolumes.Clear(); }
        }

        [Test]
        public void WindIsCalmAtTheGroundAndFullAtTenMetres()
        {
            Assert.AreEqual(0f, Wind.HeightFactor(0.05f));
            Assert.AreEqual(1f, Wind.HeightFactor(10f), 1e-4f);
            Assert.Less(Wind.HeightFactor(2f), 1f);
            Assert.Greater(Wind.HeightFactor(30f), 1f);
        }

        [Test]
        public void FlashPeaksAtOnceAndIsGoneInATenthOfASecond()
        {
            Assert.AreEqual(0f, FxLight.Curve(FxLight.Mode.Flash, -0.01f, 0.12f, 0f));
            Assert.Greater(FxLight.Curve(FxLight.Mode.Flash, 0.005f, 0.12f, 0f), 0.8f);
            Assert.Less(FxLight.Curve(FxLight.Mode.Flash, 0.15f, 0.12f, 0f), 0.05f);
            Assert.AreEqual(0f, FxLight.Curve(FxLight.Mode.Glow, 0.5f, 0.4f, 0f));
        }
    }
}
