using System.Collections.Generic;
using ModernDrone.Damage;
using ModernDrone.Missions;
using NUnit.Framework;

namespace ModernDrone.Tests
{
    public class MissionRulesTests
    {
        static List<(KillState, TargetRequirement)> Targets(params (KillState, TargetRequirement)[] t) =>
            new List<(KillState, TargetRequirement)>(t);

        [Test]
        public void OutOfAction_AcceptsMobilityOrFirepower_ButNotSensors()
        {
            Assert.IsTrue(MissionRules.IsNeutralized(KillState.Mobility, TargetRequirement.OutOfAction));
            Assert.IsTrue(MissionRules.IsNeutralized(KillState.Firepower, TargetRequirement.OutOfAction));
            Assert.IsFalse(MissionRules.IsNeutralized(KillState.Sensors, TargetRequirement.OutOfAction));
            Assert.IsFalse(MissionRules.IsNeutralized(KillState.Mobility, TargetRequirement.Destroyed));
            Assert.IsTrue(MissionRules.IsNeutralized(KillState.Destroyed, TargetRequirement.Destroyed));
        }

        [Test]
        public void Evaluate_SuccessWhenAllNeutralized_EvenOnLastDrone()
        {
            var t = Targets((KillState.Mobility, TargetRequirement.OutOfAction), (KillState.Destroyed, TargetRequirement.OutOfAction));
            Assert.AreEqual(MissionOutcome.Success, MissionRules.Evaluate(t, 4, 4));
        }

        [Test]
        public void Evaluate_FailureWhenOutOfDrones()
        {
            var t = Targets((KillState.Mobility, TargetRequirement.OutOfAction), (KillState.None, TargetRequirement.OutOfAction));
            Assert.AreEqual(MissionOutcome.InProgress, MissionRules.Evaluate(t, 3, 4));
            Assert.AreEqual(MissionOutcome.Failure, MissionRules.Evaluate(t, 4, 4));
        }

        [Test]
        public void Score_RewardsHeavierKillsAndSpareDrones()
        {
            Assert.Greater(MissionRules.Score(KillState.Destroyed), MissionRules.Score(KillState.Mobility | KillState.Firepower));
            Assert.Greater(MissionRules.Score(KillState.Mobility | KillState.Firepower), MissionRules.Score(KillState.Mobility));

            var t = Targets((KillState.Destroyed, TargetRequirement.OutOfAction));
            Assert.AreEqual(MissionRules.PointsDestroyed + 2 * MissionRules.PointsPerSpareDrone,
                MissionRules.TotalScore(t, 2, 4, MissionOutcome.Success));
            Assert.AreEqual(MissionRules.PointsDestroyed, MissionRules.TotalScore(t, 2, 4, MissionOutcome.Failure));
        }
    }
}
