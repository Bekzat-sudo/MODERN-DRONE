using System.Collections.Generic;
using ModernDrone.Damage;
using ModernDrone.Missions;
using NUnit.Framework;
using UnityEngine;

namespace ModernDrone.Tests
{
    public class InterceptorTests
    {
        [Test]
        public void Steer_TurnIsLimitedByTurnRate()
        {
            var v = InterceptorGuidance.Steer(new Vector3(0, 0, 40f), Vector3.zero, new Vector3(100f, 0, 0), 40f, 20f, 90f, 0.1f);
            Assert.AreEqual(9f, Vector3.Angle(Vector3.forward, v), 0.1f, "90 deg/s for 0.1 s");
            Assert.AreEqual(40f, v.magnitude, 1e-3f);
        }

        [Test]
        public void Steer_AcceleratesTowardMaxSpeed()
        {
            var v = InterceptorGuidance.Steer(new Vector3(0, 10f, 0), Vector3.zero, new Vector3(0, 100f, 0), 40f, 20f, 180f, 0.5f);
            Assert.AreEqual(20f, v.magnitude, 1e-3f);
        }

        [Test]
        public void LeadPoint_AimsAheadOfCrossingTarget_WithCap()
        {
            var lead = InterceptorGuidance.LeadPoint(Vector3.zero, 40f, new Vector3(0, 0, 80f), new Vector3(20f, 0, 0));
            Assert.AreEqual(40f, lead.x, 1e-3f, "2 s to go * 20 m/s");
            var far = InterceptorGuidance.LeadPoint(Vector3.zero, 10f, new Vector3(0, 0, 500f), new Vector3(20f, 0, 0));
            Assert.AreEqual(60f, far.x, 1e-3f, "prediction capped at 3 s");
        }

        [Test]
        public void PursuitCatchesSlowerStraightFlyer()
        {
            Vector3 p = Vector3.zero, v = new Vector3(0, 10f, 0);
            Vector3 target = new Vector3(0, 30f, 200f), tv = new Vector3(15f, 0, 0);
            float dt = 0.02f, closest = float.MaxValue;
            for (int i = 0; i < 1000; i++)
            {
                var aim = InterceptorGuidance.LeadPoint(p, v.magnitude, target, tv);
                v = InterceptorGuidance.Steer(v, p, aim, 40f, 22f, 140f, dt);
                p += v * dt;
                target += tv * dt;
                closest = Mathf.Min(closest, Vector3.Distance(p, target));
            }
            Assert.Less(closest, 2.5f);
        }
    }

    public class MissionRulesQuotaTests
    {
        [Test]
        public void RequiredCount_SucceedsBeforeAllTargetsAreDown()
        {
            var t = new List<(KillState, TargetRequirement)>
            {
                (KillState.Mobility, TargetRequirement.OutOfAction), (KillState.Mobility, TargetRequirement.OutOfAction),
                (KillState.Destroyed, TargetRequirement.OutOfAction), (KillState.None, TargetRequirement.OutOfAction)
            };
            Assert.AreEqual(MissionOutcome.Success, MissionRules.Evaluate(t, 2, 6, required: 3));
            Assert.AreEqual(MissionOutcome.InProgress, MissionRules.Evaluate(t, 2, 6));
        }

        [Test]
        public void Escapes_MakeTheTaskImpossible()
        {
            var t = new List<(KillState, TargetRequirement)>
            {
                (KillState.Mobility, TargetRequirement.OutOfAction), (KillState.None, TargetRequirement.OutOfAction)
            };
            Assert.AreEqual(MissionOutcome.InProgress, MissionRules.Evaluate(t, 1, 6, required: 2, escaped: 2));
            t.RemoveAt(1);
            Assert.AreEqual(MissionOutcome.Failure, MissionRules.Evaluate(t, 1, 6, required: 2, escaped: 3));
        }
    }
}
