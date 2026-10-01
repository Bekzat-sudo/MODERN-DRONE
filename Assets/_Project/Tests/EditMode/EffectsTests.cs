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

        /// <summary>Seconds a target 50 m straight behind a fresh cloud stays hidden (0.1 s steps).</summary>
        static float HiddenFor(SmokeVolumes.Kind kind, bool thermal)
        {
            SmokeVolumes.Clear();
            var cloud = new Vector3(0f, 2f, 0f);
            SmokeVolumes.Add(SmokeVolumes.Cloud(kind, cloud, 1f, 0f, Vector3.zero));
            var eye = new Vector3(0f, 2f, -60f);
            float hidden = 0f;
            for (float t = 0f; t < 60f; t += 0.1f)
            {
                // Aim at where the cloud is (it rises): the worst case for the observer.
                var target = SmokeVolumes.Volumes[0].CentreAt(t) + new Vector3(0f, 0f, 50f);
                if (!SmokeVolumes.Visible(eye, target, thermal, t)) hidden = t;
            }
            SmokeVolumes.Clear();
            return hidden;
        }

        [Test]
        public void AirBurstHidesForAboutTwoSeconds()
        {
            float t = HiddenFor(SmokeVolumes.Kind.AirBurst, false);
            Assert.That(t, Is.InRange(1f, 3f));
            Assert.AreEqual(0f, HiddenFor(SmokeVolumes.Kind.AirBurst, true), 1e-4f, "soot barely stops a thermal sight");
        }

        [Test]
        public void GroundBurstHidesForSecondsThenHangsAsHaze()
        {
            float t = HiddenFor(SmokeVolumes.Kind.GroundBurst, false);
            Assert.That(t, Is.InRange(3f, 6f));
            Assert.Less(HiddenFor(SmokeVolumes.Kind.GroundBurst, true), t, "dust hinders a thermal sight less than the eye");
            // Thin haze still dims the view after 15 s, but is gone within a minute.
            var v = SmokeVolumes.Cloud(SmokeVolumes.Kind.GroundBurst, Vector3.zero, 1f, 0f, Vector3.zero);
            Assert.Greater(v.DensityAt(15f), 0f);
            Assert.AreEqual(0f, v.DensityAt(61f));
        }

        [Test]
        public void EraPuffIsGoneInAboutASecond()
        {
            Assert.That(HiddenFor(SmokeVolumes.Kind.Era, false), Is.LessThanOrEqualTo(1.2f));
        }

        [Test]
        public void BiggerChargesMakeBiggerClouds()
        {
            var small = SmokeVolumes.Cloud(SmokeVolumes.Kind.GroundBurst, Vector3.zero, 1f, 0f, Vector3.zero);
            var big = SmokeVolumes.Cloud(SmokeVolumes.Kind.GroundBurst, Vector3.zero, 8f, 0f, Vector3.zero);
            Assert.AreEqual(2f, big.radius / small.radius, 1e-3f); // cube root of the charge
        }

        [Test]
        public void SmokeIsBlownDownwind()
        {
            var v = SmokeVolumes.Cloud(SmokeVolumes.Kind.AirBurst, Vector3.zero, 1f, 0f, new Vector3(4f, 0f, 0f));
            Assert.Greater(v.CentreAt(5f).x, 10f);
        }

        [Test]
        public void FlashDazzlesThermalSightsForOneToThreeSeconds()
        {
            SmokeVolumes.Clear();
            try
            {
                var eye = new Vector3(0f, 2f, -200f);
                var target = new Vector3(3f, 2f, 0f); // just beside the burst
                SmokeVolumes.AddFlash(Vector3.zero, 1f, 0f);
                Assert.IsFalse(SmokeVolumes.Visible(eye, target, true, 0.5f));
                Assert.IsTrue(SmokeVolumes.Visible(eye, target, false, 0.5f), "day optics are not whited out");
                Assert.IsTrue(SmokeVolumes.Visible(eye, target, true, 3.1f));
                Assert.IsTrue(SmokeVolumes.Visible(eye, new Vector3(150f, 2f, 0f), true, 0.5f), "looking away from the flash");
            }
            finally { SmokeVolumes.Clear(); }
        }

        [Test]
        public void BurningColumnHidesWhileRefreshed()
        {
            SmokeVolumes.Clear();
            try
            {
                var a = new Vector3(0f, 6f, -50f);
                var b = new Vector3(0f, 6f, 50f);
                Assert.AreEqual(1f, SmokeVolumes.Transmittance(a, b, false, 0f), 1e-5f);
                SmokeVolumes.Set(1, new Vector3(0f, 6f, 0f), 7f, 0.45f, 10f);
                Assert.Less(SmokeVolumes.Transmittance(a, b, false, 1f), SmokeVolumes.SeeThreshold);
                Assert.Greater(SmokeVolumes.Transmittance(a, b, true, 1f), SmokeVolumes.SeeThreshold);
                SmokeVolumes.Prune(11f);
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
