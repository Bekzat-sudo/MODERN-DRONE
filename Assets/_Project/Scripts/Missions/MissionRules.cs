using System.Collections.Generic;
using ModernDrone.Damage;

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

        /// <param name="sortiesFlown">Sorties already finished (drone expended).</param>
        public static MissionOutcome Evaluate(IReadOnlyList<(KillState state, TargetRequirement requirement)> targets,
            int sortiesFlown, int sortiesTotal)
        {
            bool allDone = targets.Count > 0;
            foreach (var t in targets)
                if (!IsNeutralized(t.state, t.requirement)) { allDone = false; break; }

            if (allDone) return MissionOutcome.Success;
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
