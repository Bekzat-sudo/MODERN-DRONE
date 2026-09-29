namespace ModernDrone.Missions
{
    /// <summary>Pre-sortie loadout: the hangar screen with this mission's limits (night cameras, links in stock).</summary>
    public sealed partial class MissionController
    {
        Hangar _hangar;

        Hangar HangarScreen => _hangar ??= new Hangar(droneModels, parts, warheads, GetComponent<HangarPreview>(), true,
            FreeFlight ? null : fiber => fiber ? _fiberLeft : _radioLeft);

        /// <summary>Picks the next drone: opens the hangar when there is anything to choose.</summary>
        void PrepareSortie()
        {
            HangarScreen.Open();
            if (!HangarScreen.HasChoices)
            {
                LaunchSortie();
                return;
            }
            Phase = MissionPhase.Loadout;
            _input.Suspended = true;
        }

        void UpdateLoadout()
        {
            HangarScreen.Update();
            if (ConfirmPressed()) LaunchSortie();
        }

        void DrawLoadout() => HangarScreen.Draw("АНГАР · СНАРЯЖЕНИЕ",
            "↑↓ — слот,  ← → — вариант,  мышь: клик по строке, тянуть — повернуть дрон,  SPACE / A — взлёт");
    }
}
