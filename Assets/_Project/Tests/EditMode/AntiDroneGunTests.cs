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

        [Test]
        public void KillChance_FastDiveIsHarderToHitThanLoitering()
        {
            float loiter = AntiDroneGun.RoundKillChance(0.2f, 25f, 20f, 50f, 2f);
            float dive = AntiDroneGun.RoundKillChance(0.2f, 25f, 20f, 50f, 30f);
            Assert.Greater(loiter, dive * 3f);
        }
    }
}
