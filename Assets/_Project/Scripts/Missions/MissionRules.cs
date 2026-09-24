using System.Collections.Generic;
using ModernDrone.Damage;
using UnityEngine;

namespace ModernDrone.Missions
{
    public enum TargetRequirement
    {
        /// <summary>Mobility or firepower kill is enough.</summary>
        OutOfAction,
        /// <summary>Must be destroyed outright (detonation or burn-out).</summary>
        Destroyed,
        /// <summary>Emitters (EW, radar): sensors / antennas knocked out is enough.</summary>
        Silenced
    }

    public enum MissionOutcome
    {
        InProgress,
        Success,
        Failure
    }

    /// <summary>Pure mission scoring and win/lose rules.</summary>
    public static class MissionRules
    {
        public const int PointsDestroyed = 100;
        public const int PointsMobilityAndFirepower = 70;
        public const int PointsPartial = 40;
        public const int PointsSensors = 20;
        public const int PointsPerSpareDrone = 25;

        public static bool IsNeutralized(KillState state, TargetRequirement requirement) => requirement switch
        {
            TargetRequirement.Destroyed => (state & KillState.Destroyed) != 0,
            TargetRequirement.Silenced => (state & (KillState.Sensors | KillState.Destroyed)) != 0,
            _ => (state & (KillState.Mobility | KillState.Firepower | KillState.Destroyed)) != 0
        };

        public static int Score(KillState state)
        {
            if ((state & KillState.Destroyed) != 0) return PointsDestroyed;
            bool mobility = (state & KillState.Mobility) != 0;
            bool firepower = (state & KillState.Firepower) != 0;
            if (mobility && firepower) return PointsMobilityAndFirepower;
            if (mobility || firepower) return PointsPartial;
            if ((state & KillState.Sensors) != 0) return PointsSensors;
            return 0;
        }

        /// <param name="targets">Targets still in the area.</param>
        /// <param name="sortiesFlown">Sorties already finished (drone expended).</param>
        /// <param name="required">Targets that must be neutralized; 0 = all of them.</param>
        /// <param name="escaped">Targets that left the area and can no longer be engaged.</param>
        public static MissionOutcome Evaluate(IReadOnlyList<(KillState state, TargetRequirement requirement)> targets,
            int sortiesFlown, int sortiesTotal, int required = 0, int escaped = 0)
        {
            int total = targets.Count + escaped;
            int need = required > 0 ? Mathf.Min(required, total) : total;
            int done = 0;
            foreach (var t in targets)
                if (IsNeutralized(t.state, t.requirement)) done++;

            if (total > 0 && done >= need) return MissionOutcome.Success;
            if (done + (targets.Count - done) < need) return MissionOutcome.Failure; // too many escaped
            if (sortiesFlown >= sortiesTotal) return MissionOutcome.Failure;
            return MissionOutcome.InProgress;
        }

        public static int TotalScore(IReadOnlyList<(KillState state, TargetRequirement requirement)> targets,
            int sortiesFlown, int sortiesTotal, MissionOutcome outcome)
        {
            int score = 0;
            foreach (var t in targets) score += Score(t.state);
            if (outcome == MissionOutcome.Success)
                score += (sortiesTotal - sortiesFlown) * PointsPerSpareDrone;
            return score;
        }
    }
}
