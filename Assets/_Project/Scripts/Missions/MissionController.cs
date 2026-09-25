using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    public sealed class MissionController : MonoBehaviour
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
        [SerializeField] DroneConfig[] droneClasses = new DroneConfig[0];
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
        LinkType _choice;
        int _warheadChoice;
        int _loadoutRow;
        int _decoyStrikes;
        readonly List<ModernDrone.Vehicles.Convoy> _convoys = new List<ModernDrone.Vehicles.Convoy>();
        bool _convoysStarted;
        bool _recorded;
        DroneLink _link;
        string _lostMessage = "ДРОН ПОТЕРЯН";
        float _missionTime;
        MissionOutcome _outcome;
        GUIStyle _title, _body, _small;

        public MissionPhase Phase { get; private set; }
        public bool FreeFlight => _targets.Count == 0;
        public int SortiesFlown => _sortiesFlown;
        public int Sorties => sorties + fiberSorties;
        public MissionOutcome Outcome => _outcome;
        public IReadOnlyList<MissionTarget> Targets => _targets;

        public void SetWarheads(WarheadData[] options) => warheads = options ?? new WarheadData[0];

        public void SetRequiredTargets(int count) => requiredTargets = count;

        public void SetDroneClasses(DroneConfig[] classes) => droneClasses = classes ?? new DroneConfig[0];

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
        }

        void BeginFlying()
        {
            Phase = MissionPhase.Flying;
            _input.Suspended = false;
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

        enum LoadoutRow { Drone, Link, Warhead }

        readonly List<LoadoutRow> _loadoutRows = new List<LoadoutRow>();
        int _droneChoice = -1;

        bool ChooseLink => !FreeFlight && _radioLeft > 0 && _fiberLeft > 0;

        /// <summary>Picks the next drone: asks the pilot when there is anything to choose.</summary>
        void PrepareSortie()
        {
            _choice = FreeFlight || _radioLeft > 0 ? LinkType.Radio : LinkType.Fiber;
            if (_droneChoice < 0) _droneChoice = Mathf.Clamp(1, 0, Mathf.Max(0, droneClasses.Length - 1));
            _loadoutRows.Clear();
            if (droneClasses.Length > 1) _loadoutRows.Add(LoadoutRow.Drone);
            if (ChooseLink) _loadoutRows.Add(LoadoutRow.Link);
            if (warheads.Length > 1) _loadoutRows.Add(LoadoutRow.Warhead);
            if (_loadoutRows.Count == 0)
            {
                LaunchSortie(_choice);
                return;
            }
            Phase = MissionPhase.Loadout;
            _input.Suspended = true;
            _loadoutRow = 0;
        }

        void UpdateLoadout()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            int n = _loadoutRows.Count;
            if (up) _loadoutRow = (_loadoutRow + n - 1) % n;
            if (down) _loadoutRow = (_loadoutRow + 1) % n;
            if (left || right)
            {
                int step = right ? 1 : -1;
                switch (_loadoutRows[_loadoutRow])
                {
                    case LoadoutRow.Drone: _droneChoice = (_droneChoice + step + droneClasses.Length) % droneClasses.Length; break;
                    case LoadoutRow.Link: _choice = _choice == LinkType.Radio ? LinkType.Fiber : LinkType.Radio; break;
                    case LoadoutRow.Warhead: _warheadChoice = (_warheadChoice + step + warheads.Length) % warheads.Length; break;
                }
            }
            if (ConfirmPressed()) LaunchSortie(_choice);
        }
        void OnShotDown()
        {
            if (Phase != MissionPhase.Flying) return;
            _lostMessage = drone.ShotDownCause ?? "ДРОН СБИТ";
            Phase = MissionPhase.DroneLost;
            _input.Suspended = true;
            StartCoroutine(DelayThen(droneLostDelay, EndSortie));
        }

        void LaunchSortie(LinkType type)
        {
            if (!FreeFlight)
            {
                if (type == LinkType.Fiber) _fiberLeft--; else _radioLeft--;
            }
            if (droneClasses.Length > 0)
            {
                var cfg = droneClasses[Mathf.Clamp(_droneChoice, 0, droneClasses.Length - 1)];
                if (drone.Config == null || !drone.Config.name.StartsWith(cfg.name))
                {
                    drone.Initialize(cfg);
                    ControlSettings.NotifyChanged(); // re-apply pilot rates / camera to the new runtime config
                }
            }
            _link?.SetType(type);
            if (!_convoysStarted)
            {
                _convoysStarted = true;
                foreach (var c in _convoys) c.Begin();
            }
            if (warheads.Length > 0 && _warhead != null) _warhead.SetWarhead(warheads[Mathf.Clamp(_warheadChoice, 0, warheads.Length - 1)]);
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
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            switch (Phase)
            {
                case MissionPhase.Briefing:
                    Panel(new Rect(w * 0.2f, h * 0.15f, w * 0.6f, h * 0.7f), missionName, BriefingText(), "SPACE / A — начать");
                    break;
                case MissionPhase.Flying:
                    int done = 0;
                    foreach (var t in _targets) if (t.Neutralized && !IsEscaped(t)) done++;
                    string kind = _link != null && _link.Type == LinkType.Fiber ? "ОПТОВОЛОКНО" : "РАДИО";
                    GUI.Label(new Rect(0, h * 0.09f, w, _small.fontSize * 1.5f),
                        $"ДРОН {_sortiesFlown + 1}/{Sorties} · {kind}   ЦЕЛИ {done}/{Required}", _small);
                    break;
                case MissionPhase.DroneLost:
                    GUI.Label(new Rect(0, h * 0.4f, w, _title.fontSize * 1.5f), _lostMessage, _title);
                    break;
                case MissionPhase.Loadout:
                    Panel(new Rect(w * 0.28f, h * 0.28f, w * 0.44f, h * 0.4f), "СНАРЯЖЕНИЕ", LoadoutText(),
                        _loadoutRows.Count > 1 ? "↑↓ — строка,  ← → — выбор,  SPACE / A — взлёт" : "← → — выбор,  SPACE / A — взлёт");
                    break;
                case MissionPhase.Debrief:
                    string header = _outcome == MissionOutcome.Success ? "ЗАДАЧА ВЫПОЛНЕНА" : "ЗАДАЧА НЕ ВЫПОЛНЕНА";
                    Panel(new Rect(w * 0.2f, h * 0.15f, w * 0.6f, h * 0.7f), header, DebriefText(),
                        $"{ControlInput.HintFor(PilotAction.Restart, _input != null ? _input.ActiveSource : InputSource.KeyboardMouse)} — повторить");
                    break;
            }
        }

        string BriefingText()
        {
            var sb = new StringBuilder(briefing);
            sb.AppendLine().AppendLine();
            sb.AppendLine(fiberSorties > 0 ? $"Дронов: {sorties} радио + {fiberSorties} оптоволокно" : $"Дронов: {sorties}");
            sb.AppendLine(Required < _targets.Count ? $"Цели (нужно {Required} из {_targets.Count}):" : "Цели:");
            foreach (var t in _targets)
                sb.AppendLine($"  • {t.Label} — {RequirementText(t.Requirement)}");
            var src = _input != null ? _input.ActiveSource : InputSource.KeyboardMouse;
            sb.AppendLine().Append($"Арм: {ControlInput.HintFor(PilotAction.Arm, src)}   Режим: {ControlInput.HintFor(PilotAction.Mode, src)}   Пауза / настройки: {ControlInput.HintFor(PilotAction.Pause, src)}");
            return sb.ToString();
        }

        static string RequirementText(TargetRequirement r) => r switch
        {
            TargetRequirement.Destroyed => "уничтожить",
            TargetRequirement.Silenced => "подавить (антенны / генератор)",
            _ => "вывести из строя"
        };

        string DebriefText()
        {
            var states = CollectStates();
            var sb = new StringBuilder();
            foreach (var t in _targets)
                sb.AppendLine(IsEscaped(t) ? $"{t.Label}: ушла" : $"{t.Label}: {DamageNames.Ru(t.Model.State)}  (+{MissionRules.Score(t.Model.State)})");
            sb.AppendLine();
            sb.AppendLine($"Израсходовано дронов: {_sortiesFlown}/{Sorties}");
            if (_decoyStrikes > 0) sb.AppendLine($"Из них по макетам: {_decoyStrikes}");
            int s = (int)_missionTime;
            sb.AppendLine($"Время: {s / 60:00}:{s % 60:00}");
            sb.AppendLine().Append($"ОЧКИ: {MissionRules.TotalScore(states, _sortiesFlown, Sorties, _outcome)}");
            return sb.ToString();
        }

        string LoadoutText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _loadoutRows.Count; i++)
            {
                string cursor = _loadoutRows.Count > 1 && i == _loadoutRow ? "» " : "   ";
                switch (_loadoutRows[i])
                {
                    case LoadoutRow.Drone:
                        var cfg = droneClasses[Mathf.Clamp(_droneChoice, 0, droneClasses.Length - 1)];
                        sb.AppendLine($"{cursor}Дрон:  ‹ {cfg.displayName} ›");
                        sb.AppendLine("      " + cfg.summary);
                        break;
                    case LoadoutRow.Link:
                        sb.AppendLine($"{cursor}Канал:  ‹ {(_choice == LinkType.Radio ? "РАДИО (аналог)" : "ОПТОВОЛОКНО")} ›");
                        sb.AppendLine(_choice == LinkType.Radio
                            ? $"      осталось {_radioLeft} · уязвим к РЭБ и рельефу"
                            : $"      осталось {_fiberLeft} · не глушится, катушка 5 км, +масса");
                        break;
                    case LoadoutRow.Warhead:
                        var w = warheads[Mathf.Clamp(_warheadChoice, 0, warheads.Length - 1)];
                        sb.AppendLine($"{cursor}Боевая часть:  ‹ {w.displayName} ›");
                        sb.AppendLine("      " + WarheadHint(w));
                        if (droneClasses.Length > 0)
                        {
                            var dc = droneClasses[Mathf.Clamp(_droneChoice, 0, droneClasses.Length - 1)];
                            float twr = dc.frameMass * dc.thrustToWeight / (dc.frameMass + w.mass);
                            sb.AppendLine($"      масса {w.mass:0.0} кг · дрон {dc.frameMass + w.mass:0.0} кг · тяга/вес {twr:0.0}");
                        }
                        break;
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        static string WarheadHint(WarheadData w) => w.kind == WarheadKind.Fragmentation
            ? "против открытых целей: РЛС, орудия, грузовики"
            : w.tandem ? "пробивает динамическую защиту" : "против брони: борт, корма, крыша";

        void Panel(Rect r, string header, string text, string footer)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.03f, 0.93f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            float pad = r.height * 0.05f;
            GUI.Label(new Rect(r.x, r.y + pad, r.width, _title.fontSize * 1.5f), header, _title);
            float bodyTop = r.y + pad + _title.fontSize * 2f;
            GUI.Label(new Rect(r.x + pad, bodyTop, r.width - pad * 2, r.yMax - bodyTop - pad * 2f), text, _body);
            GUI.Label(new Rect(r.x, r.yMax - pad * 1.5f, r.width, _small.fontSize * 1.5f), footer, _small);
        }

        void EnsureStyles()
        {
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.024f));
            if (_body != null && _body.fontSize == size) return;
            _body = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true };
            _body.normal.textColor = Color.white;
            _title = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 1.6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _small = new GUIStyle(_body) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
    }
}
