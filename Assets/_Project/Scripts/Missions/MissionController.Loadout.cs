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

        void DrawLoadout()
        {
            string note = FreeFlight ? missionName : $"{missionName} · дрон {_sortiesFlown + 1} из {Sorties}";
            string go = ModernDrone.Drone.ControlInput.Key("Space", "A");
            HangarScreen.Draw("СНАРЯЖЕНИЕ", note, ("↑↓", "слот"), ("←→", "вариант"));
            if (ModernDrone.Core.UiTheme.Button(Hangar.ActionRect, "ВЗЛЁТ", true, true, go)) LaunchSortie();
        }
    }
}
