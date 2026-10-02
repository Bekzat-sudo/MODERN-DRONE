using System.Collections;
using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Video;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ModernDrone.Missions
{
    public enum MissionPhase
    {
        Briefing,
        Loadout,
        Flying,
        Killcam,
        DroneLost,
        Debrief
    }

    /// <summary>
    /// Runs one mission: briefing, a limited number of sorties, killcam after each strike, debrief.
    /// With no targets in the scene it runs as free flight (unlimited sorties, no briefing).
    /// </summary>
    public sealed partial class MissionController : MonoBehaviour
    {
        [SerializeField] string missionName = "Свободный полёт";
        [TextArea(3, 8)] [SerializeField] string briefing = "";
        [Tooltip("Radio (analog) drones available.")]
        [SerializeField] int sorties = 4;
        [Tooltip("Fiber-optic drones available.")]
        [SerializeField] int fiberSorties;
        [Tooltip("Warheads the pilot can choose from before each sortie (first = default).")]
        [SerializeField] WarheadData[] warheads = new WarheadData[0];
        [Tooltip("Drone classes the pilot can choose from (index 1 = default when present).")]
        [UnityEngine.Serialization.FormerlySerializedAs("droneClasses")]
        [SerializeField] DroneConfig[] droneModels = new DroneConfig[0];
        [Tooltip("Swappable parts offered in the loadout (battery, props, camera at night, video link).")]
        [SerializeField] DronePart[] parts = new DronePart[0];
        [Tooltip("Targets that must be neutralized; 0 = all.")]
        [SerializeField] int requiredTargets;
        [SerializeField] DroneController drone;
        [SerializeField] Killcam killcam;
        [SerializeField] float droneLostDelay = 2f;

        readonly List<MissionTarget> _targets = new List<MissionTarget>();
        readonly List<(KillState, TargetRequirement)> _states = new List<(KillState, TargetRequirement)>();
        DroneWarhead _warhead;
        DroneInput _input;
        int _sortiesFlown;
        int _radioLeft, _fiberLeft;
        int _decoyStrikes;
        readonly List<ModernDrone.Vehicles.Convoy> _convoys = new List<ModernDrone.Vehicles.Convoy>();
        bool _convoysStarted;
        bool _recorded;
        DroneLink _link;
        string _lostMessage = "ДРОН ПОТЕРЯН";
        float _missionTime;
        MissionOutcome _outcome;
        float _debriefAt;

        public MissionPhase Phase { get; private set; }
        public bool FreeFlight => _targets.Count == 0;
        public int SortiesFlown => _sortiesFlown;
        public int Sorties => sorties + fiberSorties;
        public MissionOutcome Outcome => _outcome;
        public IReadOnlyList<MissionTarget> Targets => _targets;
        public string MissionName => missionName;
        /// <summary>Flight time so far (briefing and loadout excluded).</summary>
        public float MissionTime => _missionTime;
        /// <summary>Targets that must be neutralized to complete the task.</summary>
        public int RequiredTargets => Required;

        /// <summary>Targets neutralized so far (an escaped convoy vehicle does not count).</summary>
        public int TargetsDone
        {
            get
            {
                int done = 0;
                foreach (var t in _targets) if (t.Neutralized && !IsEscaped(t)) done++;
                return done;
            }
        }

        public void SetWarheads(WarheadData[] options) => warheads = options ?? new WarheadData[0];

        public void SetRequiredTargets(int count) => requiredTargets = count;

        public void SetDroneModels(DroneConfig[] models) => droneModels = models ?? new DroneConfig[0];

        public void SetParts(DronePart[] options) => parts = options ?? new DronePart[0];

        int Required => requiredTargets > 0 ? Mathf.Min(requiredTargets, _targets.Count) : _targets.Count;

        public void Configure(string name, string briefingText, int radioDrones, int fiberDrones = 0)
        {
            missionName = name;
            briefing = briefingText;
            sorties = radioDrones;
            fiberSorties = fiberDrones;
        }

        void Awake()
        {
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
            if (killcam == null) killcam = FindAnyObjectByType<Killcam>(FindObjectsInactive.Include);
            _warhead = drone.GetComponent<DroneWarhead>();
            _input = drone.GetComponent<DroneInput>();
            _link = drone.GetComponent<DroneLink>();
            _radioLeft = sorties;
            _fiberLeft = fiberSorties;
            _targets.AddRange(FindObjectsByType<MissionTarget>(FindObjectsSortMode.InstanceID));
            _convoys.AddRange(FindObjectsByType<ModernDrone.Vehicles.Convoy>(FindObjectsSortMode.None));
            foreach (var c in _convoys) c.VehicleEscaped += OnVehicleEscaped;
        }

        void OnEnable()
        {
            drone.OnCrash += OnDroneCrash;
            drone.OnFailsafe += OnFailsafe;
            drone.OnShotDown += OnShotDown;
            if (_warhead != null) _warhead.StrikeResolved += OnStrike;
        }

        void OnDisable()
        {
            drone.OnCrash -= OnDroneCrash;
            drone.OnFailsafe -= OnFailsafe;
            drone.OnShotDown -= OnShotDown;
            if (_warhead != null) _warhead.StrikeResolved -= OnStrike;
        }

        void Start()
        {
            if (_warhead != null) _warhead.ShowReportOverlay = killcam == null;
            _input.AllowReset = FreeFlight;
            if (FreeFlight)
                PrepareSortie();
            else
            {
                Phase = MissionPhase.Briefing;
                _input.Suspended = true;
            }
        }

        void Update()
        {
            if (ModernDrone.Core.GamePause.IsPaused) return;
            if (ControlInput.WasPressed(PilotAction.Restart))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }
            if (Phase == MissionPhase.Flying || Phase == MissionPhase.Killcam || Phase == MissionPhase.DroneLost)
                _missionTime += Time.deltaTime;

            if (Phase == MissionPhase.Briefing && ConfirmPressed())
                PrepareSortie();
            else if (Phase == MissionPhase.Loadout)
                UpdateLoadout();
            else if (Phase == MissionPhase.Debrief && RestartPressed())
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            else if (Phase == MissionPhase.Debrief && MenuPressed())
                SceneManager.LoadScene(0); // main menu (build index 0)
        }

        void BeginFlying()
        {
            Phase = MissionPhase.Flying;
            _input.Suspended = false;
            if (killcam != null) killcam.BeginRecording(drone.transform);
        }

        void OnStrike(StrikeReport report)
        {
            if (Phase != MissionPhase.Flying) return;
            if (report.HitDecoy) _decoyStrikes++;
            Phase = MissionPhase.Killcam;
            _input.Suspended = true;
            if (killcam != null)
                StartCoroutine(killcam.Play(report, EndSortie));
            else
                StartCoroutine(DelayThen(droneLostDelay, EndSortie));
        }

        void OnDroneCrash(Collision _)
        {
            if (Phase != MissionPhase.Flying) return;
            StartCoroutine(CheckLost());
        }

        void OnFailsafe()
        {
            if (Phase != MissionPhase.Flying) return;
            _lostMessage = _link != null && _link.Type == LinkType.Fiber ? "ОБРЫВ ОПТОВОЛОКНА" : "СВЯЗЬ ПОТЕРЯНА";
            Phase = MissionPhase.DroneLost;
            _input.Suspended = true;
            StartCoroutine(DelayThen(droneLostDelay, EndSortie));
        }

        void OnShotDown()
        {
            if (Phase != MissionPhase.Flying) return;
            _lostMessage = drone.ShotDownCause ?? "ДРОН СБИТ";
            Phase = MissionPhase.DroneLost;
            _input.Suspended = true;
            StartCoroutine(DelayThen(droneLostDelay, EndSortie));
        }

        void LaunchSortie()
        {
            var hangar = HangarScreen;
            hangar.Close();
            var link = hangar.Picked(PartSlot.VideoLink);
            bool fiber = link != null ? link.fiber : !FreeFlight && _radioLeft <= 0;
            if (!FreeFlight)
            {
                if (fiber) _fiberLeft--; else _radioLeft--;
            }
            if (droneModels.Length > 0)
            {
                // Models without stock parts (older assets, tests) fly as defined.
                var old = drone.Config;
                drone.Initialize(hangar.Compose());
                if (old != null && old != drone.Config && old.name.EndsWith("(Runtime)")) Destroy(old);
                ControlSettings.NotifyChanged(); // re-apply pilot rates / camera to the new runtime config
            }
            var cam = hangar.Picked(PartSlot.Camera);
            if (NightMode.Night && cam != null)
            {
                NightMode.Thermal = cam.thermal;
                NightMode.ThermalHorizontalFov = cam.horizontalFov;
                NightMode.ThermalResolution = cam.thermalResolution;
                ControlSettings.NotifyChanged();
            }
            if (_link != null)
            {
                if (link != null) _link.ApplyPart(link);
                else _link.SetType(fiber ? LinkType.Fiber : LinkType.Radio);
            }
            if (!_convoysStarted)
            {
                _convoysStarted = true;
                foreach (var c in _convoys) c.Begin();
            }
            if (hangar.Warhead != null && _warhead != null) _warhead.SetWarhead(hangar.Warhead);
            _lostMessage = "ДРОН ПОТЕРЯН";
            BeginFlying();
        }

        IEnumerator CheckLost()
        {
            // The warhead may detonate from the same collision; its callback can run after ours.
            yield return new WaitForFixedUpdate();
            if (Phase != MissionPhase.Flying || (_warhead != null && _warhead.Detonated)) yield break;
            Phase = MissionPhase.DroneLost;
            _input.Suspended = true;
            yield return DelayThen(droneLostDelay, EndSortie);
        }

        static IEnumerator DelayThen(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        void EndSortie()
        {
            if (FreeFlight)
            {
                drone.ResetToHome();
                PrepareSortie();
                return;
            }
            _lostMessage = "ДРОН ПОТЕРЯН";

            _sortiesFlown++;
            _outcome = MissionRules.Evaluate(CollectStates(), _sortiesFlown, Sorties, Required, EscapedCount());
            if (_outcome == MissionOutcome.InProgress)
            {
                drone.ResetToHome();
                PrepareSortie();
            }
            else
                EnterDebrief();
        }

        List<(KillState, TargetRequirement)> CollectStates()
        {
            _states.Clear();
            foreach (var t in _targets)
                if (!IsEscaped(t)) _states.Add((t.Model.State, t.Requirement));
            return _states;
        }

        bool IsEscaped(MissionTarget t)
        {
            foreach (var c in _convoys)
                if (c.IsEscaped(t.Model)) return true;
            return false;
        }

        int EscapedCount()
        {
            int n = 0;
            foreach (var t in _targets) if (IsEscaped(t)) n++;
            return n;
        }

        /// <summary>A convoy vehicle left the area: end early if the task can no longer be completed.</summary>
        void OnVehicleEscaped(VehicleDamageModel _)
        {
            if (FreeFlight || Phase == MissionPhase.Debrief || Phase == MissionPhase.Killcam) return;
            var outcome = MissionRules.Evaluate(CollectStates(), 0, int.MaxValue, Required, EscapedCount());
            if (outcome != MissionOutcome.Failure) return;
            _outcome = MissionOutcome.Failure;
            EnterDebrief();
        }

        void EnterDebrief()
        {
            Phase = MissionPhase.Debrief;
            _debriefAt = Time.unscaledTime;
            _input.Suspended = true;
            if (_recorded) return;
            _recorded = true;
            int score = MissionRules.TotalScore(CollectStates(), _sortiesFlown, Sorties, _outcome);
            MissionProgress.Current.Record(SceneManager.GetActiveScene().name, _outcome == MissionOutcome.Success, score);
            MissionProgress.Save();
        }

        static bool ConfirmPressed()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

        /// <summary>Enter / A, ignored for a second so a press meant for the flight cannot skip the debrief.</summary>
        bool MenuPressed()
        {
            if (Time.unscaledTime - _debriefAt < 1f) return false;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

        static bool RestartPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.rKey.wasPressedThisFrame; // gamepad restart goes through the Restart binding
        }


        // ---------- HUD ----------

        void OnGUI()
        {
            if (ModernDrone.Core.GamePause.IsPaused) return;
            if (FreeFlight && Phase != MissionPhase.Loadout) return;
            GUI.depth = -10; // above the OSD
            float w = Screen.width, h = Screen.height;

            switch (Phase)
            {
                case MissionPhase.Briefing:
                    DrawBriefing(w, h);
                    break;
                case MissionPhase.Flying:
                    DrawStatusStrip(w);
                    break;
                case MissionPhase.DroneLost:
                    int left = Sorties - _sortiesFlown - 1;
                    UiTheme.Banner(h * 0.34f, _lostMessage, UiTheme.Danger, left > 0 ? $"дронов осталось: {left}" : "это был последний дрон");
                    break;
                case MissionPhase.Loadout:
                    DrawLoadout();
                    break;
                case MissionPhase.Debrief:
                    DrawDebrief(w, h);
                    break;
            }
        }

        /// <summary>
        /// Task state over the live feed, on the screen's top edge above the OSD rows (Betaflight's top line sits a
        /// safe margin below it), so it never covers the pilot's telemetry.
        /// </summary>
        void DrawStatusStrip(float w)
        {
            var t = UiTheme.T;
            string kind = _link != null && _link.Type == LinkType.Fiber ? "оптоволокно" : "радио";
            int done = TargetsDone;
            string Val(string v, Color c) => UiTheme.Colored(v, c);
            string text = $"ДРОН {Val($"{_sortiesFlown + 1}/{Sorties}", UiTheme.TextPrimary)} {Val(kind, UiTheme.TextBody)}     "
                + $"ЦЕЛИ {Val($"{done}/{Required}", done >= Required ? UiTheme.Success : UiTheme.TextPrimary)}";
            float tw = UiTheme.Width(text, t.caption) + UiTheme.Px(UiTheme.S6);
            var r = new Rect((w - tw) * 0.5f, UiTheme.Px(8f), tw, UiTheme.Px(32f));
            UiTheme.Fill(r, UiTheme.OverVideo);
            UiTheme.Text(r, text, t.captionCenter, UiTheme.TextSecondary);
        }

        /// <summary>Centred content column of the briefing and debrief: readable width on any aspect ratio.</summary>
        static Rect Sheet(float w, float h)
        {
            float m = UiTheme.Px(UiTheme.Margin);
            float width = Mathf.Min(w - 2f * m, UiTheme.Px(1400f));
            return new Rect((w - width) * 0.5f, UiTheme.Px(72f), width, h - UiTheme.Px(72f) - m);
        }

        void DrawBriefing(float w, float h)
        {
            var t = UiTheme.T;
            UiTheme.Background(); // opaque: the OSD under it must not show through
            var r = Sheet(w, h);
            UiTheme.Text(new Rect(r.x, r.y, r.width, UiTheme.Px(26f)), "БРИФИНГ", t.caption, UiTheme.Accent);
            UiTheme.TextFit(new Rect(r.x, r.y + UiTheme.Px(28f), r.width, UiTheme.Px(60f)), missionName, t.title, UiTheme.TextPrimary);

            float top = r.y + UiTheme.Px(112f), gap = UiTheme.Px(UiTheme.S7);
            var button = new Rect(r.x, r.yMax - UiTheme.Px(60f), UiTheme.Px(340f), UiTheme.Px(60f));
            float sideW = Mathf.Clamp(r.width * 0.36f, UiTheme.Px(420f), UiTheme.Px(520f));
            var situation = new Rect(r.x, top, r.width - sideW - gap, button.y - UiTheme.Px(UiTheme.S6) - top);
            var side = new Rect(situation.xMax + gap, top, sideW, r.yMax - top);

            // Situation: running text at a readable line length.
            UiTheme.Text(new Rect(situation.x, situation.y, situation.width, UiTheme.Px(26f)), "ОБСТАНОВКА", t.caption, UiTheme.TextSecondary);
            float textW = Mathf.Min(situation.width, UiTheme.Px(820f));
            UiTheme.Text(new Rect(situation.x, situation.y + UiTheme.Px(36f), textW, situation.height - UiTheme.Px(36f)), briefing, t.body, UiTheme.TextBody);

            // Forces, targets, controls.
            UiTheme.Panel(side);
            var s = UiTheme.Inset(side, UiTheme.Px(28f), UiTheme.Px(UiTheme.S5));
            float y = s.y, lh = UiTheme.Px(30f);
            UiTheme.Text(new Rect(s.x, y, s.width, UiTheme.Px(26f)), "ДРОНЫ", t.caption, UiTheme.TextSecondary);
            y += UiTheme.Px(32f);
            UiTheme.KeyValue(new Rect(s.x, y, s.width, lh), "радио", sorties.ToString(), UiTheme.TextPrimary);
            y += lh;
            if (fiberSorties > 0)
            {
                UiTheme.KeyValue(new Rect(s.x, y, s.width, lh), "оптоволокно", fiberSorties.ToString(), UiTheme.TextPrimary);
                y += lh;
            }
            UiTheme.Segments(new Rect(s.x, y + UiTheme.Px(4f), s.width, UiTheme.Px(6f)), Sorties, Sorties, UiTheme.TextBody);
            y += UiTheme.Px(UiTheme.S6);

            UiTheme.Text(new Rect(s.x, y, s.width, UiTheme.Px(26f)), Required < _targets.Count ? $"ЦЕЛИ · НУЖНО {Required} ИЗ {_targets.Count}" : "ЦЕЛИ", t.caption, UiTheme.TextSecondary);
            y += UiTheme.Px(32f);
            foreach (var target in _targets)
            {
                if (y > side.yMax - UiTheme.Px(180f)) break;
                var line = new Rect(s.x, y, s.width, UiTheme.Px(28f));
                UiTheme.Marker(line, UiTheme.Danger, false);
                UiTheme.TextFit(new Rect(s.x + UiTheme.Px(22f), y, s.width - UiTheme.Px(22f), line.height), target.Label, t.label, UiTheme.TextPrimary);
                UiTheme.Text(new Rect(s.x + UiTheme.Px(22f), y + UiTheme.Px(26f), s.width - UiTheme.Px(22f), UiTheme.Px(24f)), RequirementText(target.Requirement), t.caption, UiTheme.TextSecondary);
                y += UiTheme.Px(58f);
            }

            var src = _input != null ? _input.ActiveSource : InputSource.KeyboardMouse;
            float cy = s.yMax - 3f * lh;
            UiTheme.Text(new Rect(s.x, cy - UiTheme.Px(32f), s.width, UiTheme.Px(26f)), "УПРАВЛЕНИЕ", t.caption, UiTheme.TextSecondary);
            UiTheme.KeyValue(new Rect(s.x, cy, s.width, lh), "арм", ControlInput.HintFor(PilotAction.Arm, src), UiTheme.TextPrimary);
            UiTheme.KeyValue(new Rect(s.x, cy + lh, s.width, lh), "режим acro / angle", ControlInput.HintFor(PilotAction.Mode, src), UiTheme.TextPrimary);
            UiTheme.KeyValue(new Rect(s.x, cy + 2f * lh, s.width, lh), "пауза и настройки", ControlInput.HintFor(PilotAction.Pause, src), UiTheme.TextPrimary);

            if (UiTheme.Button(button, "НАЧАТЬ", true, true, ControlInput.Key("Enter", "A"))) PrepareSortie();
        }

        static string RequirementText(TargetRequirement r) => r switch
        {
            TargetRequirement.Destroyed => "уничтожить",
            TargetRequirement.Silenced => "подавить: антенны или генератор",
            _ => "вывести из строя"
        };

        void DrawDebrief(float w, float h)
        {
            var t = UiTheme.T;
            UiTheme.Background(); // opaque: the OSD under it must not show through
            var r = Sheet(w, h);
            bool success = _outcome == MissionOutcome.Success;
            var states = CollectStates();
            UiTheme.Text(new Rect(r.x, r.y, r.width, UiTheme.Px(26f)), $"ИТОГ · {missionName.ToUpperInvariant()}", t.caption, UiTheme.TextSecondary);
            var titleRect = new Rect(r.x, r.y + UiTheme.Px(28f), r.width, UiTheme.Px(60f));
            UiTheme.Text(titleRect,
                success ? "ЗАДАЧА ВЫПОЛНЕНА" : "ЗАДАЧА НЕ ВЫПОЛНЕНА", t.title, success ? UiTheme.Success : UiTheme.Danger);

            // Key numbers.
            float top = r.y + UiTheme.Px(116f), g = UiTheme.Px(UiTheme.S3), th = UiTheme.Px(104f);
            int tiles = _decoyStrikes > 0 ? 4 : 3;
            float tw = (r.width - (tiles - 1) * g) / tiles;
            int sec = (int)_missionTime;
            UiTheme.Stat(new Rect(r.x, top, tw, th), "ОЧКИ", MissionRules.TotalScore(states, _sortiesFlown, Sorties, _outcome).ToString(), UiTheme.TextPrimary);
            UiTheme.Stat(new Rect(r.x + (tw + g), top, tw, th), "ДРОНОВ ИЗРАСХОДОВАНО", $"{_sortiesFlown} из {Sorties}", UiTheme.TextPrimary);
            UiTheme.Stat(new Rect(r.x + 2f * (tw + g), top, tw, th), "ВРЕМЯ", $"{sec / 60:00}:{sec % 60:00}", UiTheme.TextPrimary);
            if (_decoyStrikes > 0)
                UiTheme.Stat(new Rect(r.x + 3f * (tw + g), top, tw, th), "УДАРОВ ПО МАКЕТАМ", _decoyStrikes.ToString(), UiTheme.Warning);

            // Targets: name, state, points.
            float y = top + th + UiTheme.Px(UiTheme.S6);
            UiTheme.Text(new Rect(r.x, y, r.width, UiTheme.Px(26f)), "ЦЕЛИ", t.caption, UiTheme.TextSecondary);
            y += UiTheme.Px(34f);
            float rh = UiTheme.Px(44f), listBottom = r.yMax - UiTheme.Px(60f) - UiTheme.Px(UiTheme.S6);
            foreach (var target in _targets)
            {
                if (y + rh > listBottom) break;
                var row = new Rect(r.x, y, r.width, rh);
                UiTheme.Fill(new Rect(row.x, row.yMax - 1f, row.width, 1f), UiTheme.Border);
                bool escaped = IsEscaped(target), hit = !escaped && target.Neutralized;
                Color c = escaped ? UiTheme.Danger : hit ? UiTheme.Success : UiTheme.TextSecondary;
                UiTheme.Marker(row, c, hit);
                UiTheme.TextFit(new Rect(row.x + UiTheme.Px(22f), row.y, row.width * 0.5f, rh), target.Label, t.label, UiTheme.TextPrimary);
                string state = escaped ? "ушла из района" : DamageNames.Ru(target.Model.State);
                float pw = UiTheme.Px(90f);
                UiTheme.Text(new Rect(row.x, row.y, row.width - pw, rh), state, t.labelRight, c);
                UiTheme.Text(new Rect(row.xMax - pw, row.y, pw, rh), escaped ? "—" : $"+{MissionRules.Score(target.Model.State)}", t.labelRight, UiTheme.TextSecondary);
                y += rh;
            }

            float bw = UiTheme.Px(320f), bh = UiTheme.Px(60f), by = r.yMax - bh;
            var src = _input != null ? _input.ActiveSource : InputSource.KeyboardMouse;
            // The likely next step is the primary one: retry after a failure, the menu after a success.
            if (UiTheme.Button(new Rect(r.x, by, bw, bh), "ПОВТОРИТЬ", !success, !success, ControlInput.HintFor(PilotAction.Restart, src)))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            if (UiTheme.Button(new Rect(r.x + bw + UiTheme.Px(UiTheme.S3), by, bw, bh), "В МЕНЮ", success, success, ControlInput.Key("Enter", "A")))
                SceneManager.LoadScene(0);
        }
    }
}
