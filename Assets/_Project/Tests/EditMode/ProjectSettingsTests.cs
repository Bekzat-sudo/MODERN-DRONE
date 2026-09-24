using ModernDrone.Core;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class ProjectSettingsTests
    {
        [Test]
        public void LayerNames_MatchGameLayersConstants()
        {
            for (int i = 0; i < GameLayers.Names.Length; i++)
                Assert.AreEqual(GameLayers.Names[i], LayerMask.LayerToName(GameLayers.Drone + i));
        }

        [Test]
        public void ModuleLayer_HasNoPhysicalCollisions()
        {
            for (int i = 0; i < 32; i++)
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(GameLayers.Module, i), $"Module collides with layer {i}");
        }

        [Test]
        public void FixedTimestep_MatchesSimRate()
        {
            Assert.AreEqual(SimConstants.FixedDeltaTime, Time.fixedDeltaTime, 1e-6f);
        }
    }
}
