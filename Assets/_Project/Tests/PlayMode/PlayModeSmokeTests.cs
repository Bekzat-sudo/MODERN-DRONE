using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ModernDrone.Tests
{
    public class PlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator Physics_TicksAtSimRate()
        {
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(Core.SimConstants.FixedDeltaTime, Time.fixedDeltaTime, 1e-6f);
        }
    }
}
