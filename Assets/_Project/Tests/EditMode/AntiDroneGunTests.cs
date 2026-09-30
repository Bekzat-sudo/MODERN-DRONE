using ModernDrone.Missions;
using NUnit.Framework;

namespace ModernDrone.Tests
{
    public class AntiDroneGunTests
    {
        [Test]
        public void KillChance_ZeroBeyondRange_FullAtOptimal()
        {
            Assert.AreEqual(0f, AntiDroneGun.RoundKillChance(0.2f, 51f, 20f, 50f, 5f));
            Assert.Greater(AntiDroneGun.RoundKillChance(0.2f, 15f, 20f, 50f, 5f), AntiDroneGun.RoundKillChance(0.2f, 40f, 20f, 50f, 5f));
        }

        /// <summary>
        /// Balance of the ZU-23-2 MOG against an FPV (VehicleCatalog values): 10 s of fire at 200 m. Crossing at
        /// 20 m/s should usually be survived, loitering should be dangerous, a 35 m/s dive nearly safe.
        /// </summary>
        [Test]
        public void ZuMog_TenSecondsOfFire_IsSurvivableWhenMoving()
        {
            float Chance(float speed) => AntiDroneGun.ExposureKillChance(0.003f, 2000f, 0.6f, 1.2f, 200f, 150f, 700f, speed, 10f);
            Assert.That(Chance(20f), Is.InRange(0.1f, 0.3f));
            Assert.That(Chance(1f), Is.InRange(0.3f, 0.6f));
            Assert.Less(Chance(35f), 0.08f);
        }

        [Test]
        public void KillChance_FastDiveIsHarderToHitThanLoitering()
        {
            float loiter = AntiDroneGun.RoundKillChance(0.2f, 25f, 20f, 50f, 2f);
            float dive = AntiDroneGun.RoundKillChance(0.2f, 25f, 20f, 50f, 30f);
            Assert.Greater(loiter, dive * 3f);
        }
    }
}
