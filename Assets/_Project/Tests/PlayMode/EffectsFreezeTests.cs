using System.Collections;
using ModernDrone.Vehicles;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernDrone.Tests
{
    /// <summary>The killcam freezes the world at impact (timeScale 0): a fresh effect must wait for it, then play.</summary>
    public class EffectsFreezeTests
    {
        [UnityTest]
        public IEnumerator Explosion_WaitsWhileTheWorldIsFrozen_ThenPlays()
        {
            float scale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                SimpleVfx.Explosion(new Vector3(0f, 50f, 0f), 2.5f, ExplosionKind.Ammo);
                for (int i = 0; i < 5; i++) yield return null;

                var fx = Object.FindAnyObjectByType<FxEffect>();
                Assert.IsNotNull(fx);
                Assert.IsFalse(fx.Started);
                Assert.AreEqual(0f, fx.Age);
                foreach (var l in fx.Lights) Assert.AreEqual(0f, l.GetComponent<Light>().intensity, 1e-4f);
                Assert.AreEqual(0, FxDebris.Active);

                Time.timeScale = 1f;
                for (int i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(fx.Started);
                Assert.Greater(fx.Age, 0f);
                Assert.Greater(FxDebris.Active, 0); // an ammunition blast throws fragments once the world runs
            }
            finally
            {
                Time.timeScale = scale;
            }
        }

        [UnityTest]
        public IEnumerator Pool_RecyclesTheOldestPastItsCap()
        {
            int built = 0;
            FxPool.Register("test_cap", () => { built++; return new GameObject("VFX_Test"); }, 2);
            for (int i = 0; i < 5; i++) FxPool.Spawn("test_cap", Vector3.zero, Quaternion.identity);
            yield return null;
            Assert.AreEqual(2, built);
            Assert.AreEqual(2, FxPool.LiveCount("test_cap"));
        }
    }
}
