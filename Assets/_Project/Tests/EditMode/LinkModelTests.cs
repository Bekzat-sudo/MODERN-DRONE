using ModernDrone.Damage;
using ModernDrone.Missions;
using ModernDrone.Video;
using NUnit.Framework;

namespace ModernDrone.Tests
{
    public class LinkModelTests
    {
        static readonly RadioParams P = RadioParams.Default;

        [Test]
        public void ClearLineOfSight_CloseRange_IsClean()
        {
            var q = LinkModel.Radio(P, 300f, 0, 0, default);
            Assert.AreEqual(1f, q.video, 1e-4f);
            Assert.AreEqual(1f, q.control, 1e-4f);
        }

        [Test]
        public void Range_DegradesVideoBeforeControl()
        {
            var q = LinkModel.Radio(P, 2000f, 0, 0, default);
            Assert.Less(q.video, 0.3f);
            Assert.AreEqual(1f, q.control, 1e-4f);
            Assert.AreEqual(0f, LinkModel.Radio(P, 7000f, 0, 0, default).control, 1e-4f);
        }

        [Test]
        public void Terrain_BlocksMoreThanFoliage()
        {
            var terrain = LinkModel.Radio(P, 300f, 1, 0, default);
            var trees = LinkModel.Radio(P, 300f, 0, 1, default);
            Assert.Less(terrain.video, trees.video);
            Assert.Greater(terrain.control, terrain.video, "control link tolerates obstruction better");
        }

        [Test]
        public void Jammer_FullInsideCore_ZeroOutsideRadius()
        {
            Assert.AreEqual(0.9f, LinkModel.JamLevel(new JamSample { strength = 0.9f, radius = 300f, distanceToDrone = 50f }), 1e-4f);
            Assert.AreEqual(0f, LinkModel.JamLevel(new JamSample { strength = 0.9f, radius = 300f, distanceToDrone = 300f }), 1e-4f);
        }

        [Test]
        public void Jammer_OnlyAffectsItsBands()
        {
            var jam = new[] { new JamSample { strength = 1f, radius = 300f, distanceToDrone = 10f, bands = JamBands.Video } };
            var q = LinkModel.Radio(P, 600f, 0, 0, jam);
            Assert.Less(q.video, 0.05f);
            Assert.AreEqual(1f, q.control, 1e-4f);
        }

        [Test]
        public void Fiber_IsCleanUntilSpoolRunsOut()
        {
            Assert.AreEqual(1f, LinkModel.Fiber(4999f, 5000f).video);
            Assert.AreEqual(0f, LinkModel.Fiber(5000f, 5000f).control);
        }

        [Test]
        public void Latency_GrowsAsLinkWeakens()
        {
            Assert.Less(LinkModel.ControlLatency(1f), 0.02f);
            Assert.Greater(LinkModel.ControlLatency(0.2f), 0.1f);
        }

        [Test]
        public void Silenced_RequiresSensorsKill()
        {
            Assert.IsFalse(MissionRules.IsNeutralized(KillState.Mobility, TargetRequirement.Silenced));
            Assert.IsTrue(MissionRules.IsNeutralized(KillState.Sensors, TargetRequirement.Silenced));
            Assert.IsTrue(MissionRules.IsNeutralized(KillState.Destroyed, TargetRequirement.Silenced));
        }
    }
}
