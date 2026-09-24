using System.Collections;
using System.Collections.Generic;
using System.Text;
using ModernDrone.Damage;
using ModernDrone.Drone;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ModernDrone.Missions
{
    public enum MissionPhase
    {
        Briefing,
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
        [SerializeField] int sorties = 4;
        [SerializeField] DroneController drone;
        [SerializeField] Killcam killcam;
        [SerializeField] float droneLostDelay = 2f;

        readonly List<MissionTarget> _targets = new List<MissionTarget>();
        readonly List<(KillState, TargetRequirement)> _states = new List<(KillState, TargetRequirement)>();
        DroneWarhead _warhead;
        DroneInput _input;
        int _sortiesFlown;
        float _missionTime;
        MissionOutcome _outcome;
        GUIStyle _title, _body, _small;

        public MissionPhase Phase { get; private set; }
        public bool FreeFlight => _targets.Count == 0;
        public int SortiesFlown => _sortiesFlown;
        public int Sorties => sorties;
        public MissionOutcome Outcome => _outcome;
        public IReadOnlyList<MissionTarget> Targets => _targets;

        public void Configure(string name, string briefingText, int sortieCount)
        {
            missionName = name;
            briefing = briefingText;
            sorties = sortieCount;
        }

        void Awake()
        {
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
            if (killcam == null) killcam = FindAnyObjectByType<Killcam>(FindObjectsInactive.Include);
            _warhead = drone.GetComponent<DroneWarhead>();
            _input = drone.GetComponent<DroneInput>();
            _targets.AddRange(FindObjectsByType<MissionTarget>(FindObjectsSortMode.InstanceID));
        }

        void OnEnable()
        {
            drone.OnCrash += OnDroneCrash;
            if (_warhead != null) _warhead.StrikeResolved += OnStrike;
        }

        void OnDisable()
        {
            drone.OnCrash -= OnDroneCrash;
            if (_warhead != null) _warhead.StrikeResolved -= OnStrike;
        }

        void Start()
        {
            if (_warhead != null) _warhead.ShowReportOverlay = killcam == null;
            _input.AllowReset = FreeFlight;
            if (FreeFlight)
                BeginFlying();
            else
            {
                Phase = MissionPhase.Briefing;
                _input.enabled = false;
            }
        }

        void Update()
        {
            if (ModernDrone.Core.GamePause.IsPaused) return;
            if (Phase == MissionPhase.Flying || Phase == MissionPhase.Killcam || Phase == MissionPhase.DroneLost)
                _missionTime += Time.deltaTime;

            if (Phase == MissionPhase.Briefing && ConfirmPressed())
                BeginFlying();
            else if (Phase == MissionPhase.Debrief && RestartPressed())
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void BeginFlying()
        {
            Phase = MissionPhase.Flying;
            _input.enabled = true;
        }

        void OnStrike(StrikeReport report)
        {
            if (Phase != MissionPhase.Flying) return;
            Phase = MissionPhase.Killcam;
            _input.enabled = false;
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

        IEnumerator CheckLost()
        {
            // The warhead may detonate from the same collision; its callback can run after ours.
            yield return new WaitForFixedUpdate();
            if (Phase != MissionPhase.Flying || (_warhead != null && _warhead.Detonated)) yield break;
            Phase = MissionPhase.DroneLost;
            _input.enabled = false;
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
                BeginFlying();
                return;
            }

            _sortiesFlown++;
            _outcome = MissionRules.Evaluate(CollectStates(), _sortiesFlown, sorties);
            if (_outcome == MissionOutcome.InProgress)
            {
                drone.ResetToHome();
                BeginFlying();
            }
            else
            {
                Phase = MissionPhase.Debrief;
                _input.enabled = false;
            }
        }

        List<(KillState, TargetRequirement)> CollectStates()
        {
            _states.Clear();
            foreach (var t in _targets) _states.Add((t.Model.State, t.Requirement));
            return _states;
        }

        static bool ConfirmPressed()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        static bool RestartPressed()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame);
        }

        // ---------- HUD ----------

        void OnGUI()
        {
            if (FreeFlight || ModernDrone.Core.GamePause.IsPaused) return;
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
                    foreach (var t in _targets) if (t.Neutralized) done++;
                    GUI.Label(new Rect(0, h * 0.09f, w, _small.fontSize * 1.5f),
                        $"ДРОН {_sortiesFlown + 1}/{sorties}   ЦЕЛИ {done}/{_targets.Count}", _small);
                    break;
                case MissionPhase.DroneLost:
                    GUI.Label(new Rect(0, h * 0.4f, w, _title.fontSize * 1.5f), "ДРОН ПОТЕРЯН", _title);
                    break;
                case MissionPhase.Debrief:
                    string header = _outcome == MissionOutcome.Success ? "ЗАДАЧА ВЫПОЛНЕНА" : "ЗАДАЧА НЕ ВЫПОЛНЕНА";
                    Panel(new Rect(w * 0.2f, h * 0.15f, w * 0.6f, h * 0.7f), header, DebriefText(), "R / START — повторить");
                    break;
            }
        }

        string BriefingText()
        {
            var sb = new StringBuilder(briefing);
            sb.AppendLine().AppendLine();
            sb.AppendLine($"Дронов: {sorties}");
            sb.AppendLine("Цели:");
            foreach (var t in _targets)
                sb.AppendLine($"  • {t.Label} — {(t.Requirement == TargetRequirement.Destroyed ? "уничтожить" : "вывести из строя")}");
            var src = _input != null ? _input.ActiveSource : InputSource.KeyboardMouse;
            sb.AppendLine().Append($"Арм: {ControlInput.HintFor(PilotAction.Arm, src)}   Режим: {ControlInput.HintFor(PilotAction.Mode, src)}   Пауза / настройки: {ControlInput.HintFor(PilotAction.Pause, src)}");
            return sb.ToString();
        }

        string DebriefText()
        {
            var states = CollectStates();
            var sb = new StringBuilder();
            foreach (var t in _targets)
                sb.AppendLine($"{t.Label}: {DamageNames.Ru(t.Model.State)}  (+{MissionRules.Score(t.Model.State)})");
            sb.AppendLine();
            sb.AppendLine($"Израсходовано дронов: {_sortiesFlown}/{sorties}");
            int s = (int)_missionTime;
            sb.AppendLine($"Время: {s / 60:00}:{s % 60:00}");
            sb.AppendLine().Append($"ОЧКИ: {MissionRules.TotalScore(states, _sortiesFlown, sorties, _outcome)}");
            return sb.ToString();
        }

        void Panel(Rect r, string header, string text, string footer)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.03f, 0.93f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            float pad = r.height * 0.05f;
            GUI.Label(new Rect(r.x, r.y + pad, r.width, _title.fontSize * 1.5f), header, _title);
            GUI.Label(new Rect(r.x + pad, r.y + pad * 2.5f, r.width - pad * 2, r.height - pad * 4), text, _body);
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
