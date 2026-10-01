using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Missions;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// Start screen, "tactical tablet" look: navigation on the left (campaign, hangar, proving ground, settings, quit),
    /// the campaign on the right — mission cards with progress, the selected mission's briefing and a launch button,
    /// and a recon window onto the slowly orbiting backdrop. Focus is either in the navigation or in the mission grid;
    /// the other side shows its choice as "current" (amber edge, no fill).
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] Transform orbitTarget;
        [SerializeField] float orbitRadius = 12f;
        [SerializeField] float orbitHeight = 3.5f;
        [SerializeField] float orbitSpeed = 6f;
        [Header("Hangar")]
        [SerializeField] DroneConfig[] droneModels = new DroneConfig[0];
        [SerializeField] DronePart[] parts = new DronePart[0];
        [SerializeField] WarheadData[] warheads = new WarheadData[0];
        [SerializeField] HangarPreview preview;
        [Header("Campaign")]
        [SerializeField] string[] missionNames =
        {
            "Стоянка у посадки", "Прикрытие РЭБ", "Огневые позиции", "Колонна",
            "Позиция ЗРК", "Ложные цели", "Ночная охота", "Охота на вертолёт"
        };
        [SerializeField] string[] missionScenes =
        {
            GameScenes.Mission01, GameScenes.Mission02, GameScenes.Mission03, GameScenes.Mission04,
            GameScenes.Mission05, GameScenes.Mission06, GameScenes.Mission07, GameScenes.Mission08
        };
        [SerializeField] string[] missionBriefings = new string[0];

        const int GridColumns = 4;

        readonly MenuGui _gui = new MenuGui();
        readonly List<MenuRow> _navRows = new List<MenuRow>();
        SettingsMenu _settings;
        bool _inSettings, _inHangar, _inMissions;
        Hangar _hangar;
        int _nav, _mission = -1;
        float _angle = 200f;
        Rect _recon; // screen rect of the recon window (the backdrop camera renders into it)

        void Awake()
        {
            GamePause.Set(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _settings = new SettingsMenu(_gui);
            _navRows.Add(MenuRow.Button("КАМПАНИЯ", () => _inMissions = true, () => $"{DoneCount()} / {missionScenes.Length}"));
            _navRows.Add(MenuRow.Button("АНГАР", OpenHangar, SavedDrone));
            _navRows.Add(MenuRow.Button("ПОЛИГОН", () => GameScenes.Load(GameScenes.ProvingGround), () => "свободный полёт"));
            _navRows.Add(MenuRow.Button("НАСТРОЙКИ", () => { _settings.Open(); _inSettings = true; }, () => "управление · видео"));
            _navRows.Add(MenuRow.Header(""));
            _navRows.Add(new MenuRow("ВЫХОД") { Activate = GameScenes.Quit, Danger = true });
        }

        public void SetHangar(DroneConfig[] models, DronePart[] options, WarheadData[] warheadOptions, HangarPreview turntable)
        {
            droneModels = models;
            parts = options;
            warheads = warheadOptions;
            preview = turntable;
        }

        public void SetMissions(string[] names, string[] scenes, string[] briefings)
        {
            missionNames = names;
            missionScenes = scenes;
            missionBriefings = briefings;
        }

        // ---- actions

        void OpenHangar()
        {
            _hangar ??= new Hangar(droneModels, parts, warheads, preview, false) { Back = CloseHangar };
            _hangar.Open();
            _inHangar = true;
        }

        void CloseHangar()
        {
            _hangar.Close();
            _inHangar = false;
        }

        void Launch() => GameScenes.Load(missionScenes[_mission]);

        MissionProgress.Entry Progress(int i) => MissionProgress.Current.entries.Find(x => x.mission == missionScenes[i]);

        bool Completed(int i) => Progress(i)?.completed ?? false;

        int DoneCount()
        {
            int done = 0;
            for (int i = 0; i < missionScenes.Length; i++) if (Completed(i)) done++;
            return done;
        }

        /// <summary>First mission not yet completed (where the campaign continues).</summary>
        int NextMission()
        {
            for (int i = 0; i < missionScenes.Length; i++)
                if (!Completed(i)) return i;
            return 0;
        }

        string SavedDrone()
        {
            string saved = PlayerPrefs.GetString("loadout/drone", "");
            var model = System.Array.Find(droneModels, m => m.name == saved) ?? (droneModels.Length > 0 ? droneModels[0] : null);
            return model != null ? model.displayName : "—";
        }

        // ---- input

        void Update()
        {
            if (orbitTarget != null)
            {
                _angle += orbitSpeed * Time.unscaledDeltaTime;
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.transform.position = orbitTarget.position + Quaternion.Euler(0f, _angle, 0f) * Vector3.forward * orbitRadius + Vector3.up * orbitHeight;
                    cam.transform.LookAt(orbitTarget.position + Vector3.up * 1.2f);
                    // Frame the backdrop in the recon window (full screen behind the hangar / settings).
                    bool framed = _recon.width > 0f && !_inHangar && !_inSettings;
                    cam.rect = framed
                        ? new Rect(_recon.x / Screen.width, 1f - _recon.yMax / Screen.height, _recon.width / Screen.width, _recon.height / Screen.height)
                        : new Rect(0f, 0f, 1f, 1f);
                }
            }
            if (_mission < 0) _mission = NextMission();

            if (_inHangar)
            {
                _hangar.Update();
                if (_gui.ReadNav().back) CloseHangar();
                return;
            }
            if (_inSettings)
            {
                _settings.Update();
                if (_settings.Closed) _inSettings = false;
                return;
            }

            var nav = _gui.ReadNav();
            if (_inMissions)
            {
                int n = missionScenes.Length;
                if (nav.left) _mission = (_mission + n - 1) % n;
                if (nav.right) _mission = (_mission + 1) % n;
                if (nav.up) _mission = (_mission - GridColumns + n) % n;
                if (nav.down) _mission = (_mission + GridColumns) % n;
                if (nav.confirm) Launch();
                if (nav.back) _inMissions = false;
                return;
            }
            // → from the campaign item steps into the mission grid, like Enter.
            if (nav.right && _nav == 0) _inMissions = true;
            else MenuGui.Navigate(_navRows, ref _nav, nav);
        }

        // ---- drawing

        void OnGUI()
        {
            var t = UiTheme.T;
            float w = Screen.width, h = Screen.height;

            if (_inHangar)
            {
                if (_hangar.Draw("АНГАР", "сборка сохраняется и используется во всех задачах",
                        ("↑↓", "слот"), ("←→", "вариант"), (ControlInput.Key("Esc", "B"), "назад")) == 2)
                    CloseHangar();
                return;
            }
            if (_inSettings)
            {
                UiTheme.Background();
                _settings.Draw(new Rect(0, 0, w, h));
                return;
            }
            if (_mission < 0) return;

            float m = UiTheme.Px(UiTheme.Margin);
            float navW = Mathf.Clamp(w * 0.3f, UiTheme.Px(500f), UiTheme.Px(600f));
            var main = new Rect(navW + UiTheme.Px(UiTheme.S7), m, w - navW - UiTheme.Px(UiTheme.S7) - m, h - 2f * m);

            float headerH = UiTheme.Px(40f), cardH = UiTheme.Px(124f), gap = UiTheme.Px(UiTheme.S3);
            float cardsTop = main.y + headerH + UiTheme.Px(UiTheme.S4);
            int rows = Mathf.CeilToInt(missionScenes.Length / (float)GridColumns);
            float lowerTop = cardsTop + rows * (cardH + gap) + UiTheme.Px(UiTheme.S4);
            float briefW = Mathf.Clamp(main.width * 0.4f, UiTheme.Px(460f), UiTheme.Px(600f));
            // The recon window shows the 3D backdrop through the ground.
            var recon = new Rect(main.x, lowerTop, main.width - briefW - UiTheme.Px(UiTheme.S5), main.yMax - lowerTop);
            _recon = recon;
            UiTheme.Background(recon);
            UiTheme.Frame(recon, UiTheme.Border);
            var tag = new Rect(recon.x + 1f, recon.y + 1f, UiTheme.Width("РАЗВЕДКА · ОБЪЕКТ", t.caption) + UiTheme.Px(UiTheme.S6), UiTheme.Px(34f));
            UiTheme.Fill(tag, UiTheme.Surface);
            UiTheme.Text(UiTheme.Inset(tag, UiTheme.Px(UiTheme.S4), 0f), "РАЗВЕДКА · ОБЪЕКТ", t.caption, UiTheme.TextSecondary);

            DrawNav(new Rect(0, 0, navW, h));
            DrawCampaign(main, headerH, cardsTop, cardH, gap);
            DrawBriefing(new Rect(main.xMax - briefW, lowerTop, briefW, main.yMax - lowerTop));
        }

        void DrawNav(Rect r)
        {
            var t = UiTheme.T;
            UiTheme.Fill(r, UiTheme.Surface);
            UiTheme.Fill(new Rect(r.xMax - 1, 0, 1, r.height), UiTheme.Border);
            float m = UiTheme.Px(UiTheme.Margin), x = m, width = r.width - m - UiTheme.Px(UiTheme.S7), y = UiTheme.Px(72f);
            UiTheme.Text(new Rect(x, y, width, UiTheme.Px(26f)), "FPV · УДАРНЫЙ КОМПЛЕКС", t.caption, UiTheme.Accent);
            UiTheme.TextFit(new Rect(x, y + UiTheme.Px(26f), width, UiTheme.Px(96f)), "MODERN DRONE", t.display, UiTheme.TextPrimary);
            y += UiTheme.Px(168f);

            float rowPad = UiTheme.Px(UiTheme.S5);
            float listH = MenuGui.RowHeight(MenuGui.RowSize.Large) * _navRows.Count;
            int nav = _inMissions ? 0 : _nav;
            _gui.DrawRows(new Rect(x - rowPad, y, width + 2f * rowPad, listH), _navRows, ref nav, MenuGui.RowSize.Large, !_inMissions);
            if (!_inMissions) _nav = nav;
            else if (nav != 0) { _nav = nav; _inMissions = false; } // the mouse moved back into the navigation

            float bottom = r.height - UiTheme.Px(UiTheme.S8);
            float lh = UiTheme.Px(30f);
            UiTheme.Fill(new Rect(x, bottom - UiTheme.Px(120f), width, 1f), UiTheme.Border);
            UiTheme.KeyValue(new Rect(x, bottom - UiTheme.Px(104f), width, lh), "УПРАВЛЕНИЕ", DeviceSummary(), UiTheme.TextBody);
            UiTheme.KeyValue(new Rect(x, bottom - UiTheme.Px(104f) + lh, width, lh), "ДРОН ИЗ АНГАРА", SavedDrone(), UiTheme.TextBody);
            string ok = ControlInput.Key("Enter", "A"), back = ControlInput.Key("Esc", "B");
            var prompts = new Rect(x, bottom - UiTheme.Px(36f), width, UiTheme.Px(36f));
            if (_inMissions)
            {
                if (UiTheme.Prompts(prompts, false, ("←→↑↓", "задача"), (ok, "вылет"), (back, "назад")) == 2) _inMissions = false;
            }
            else UiTheme.Prompts(prompts, false, ("↑↓", "пункт"), (ok, "выбрать"));
        }

        void DrawCampaign(Rect main, float headerH, float cardsTop, float cardH, float gap)
        {
            var t = UiTheme.T;
            var header = new Rect(main.x, main.y, main.width, headerH);
            UiTheme.Text(header, "КАМПАНИЯ", t.heading, UiTheme.TextPrimary);
            int done = DoneCount(), n = missionScenes.Length;
            float segW = UiTheme.Px(220f);
            UiTheme.Segments(new Rect(header.xMax - segW, header.center.y - UiTheme.Px(4f), segW, UiTheme.Px(8f)), n, done, UiTheme.Success);
            UiTheme.Text(new Rect(header.x, header.y, header.width - segW - UiTheme.Px(UiTheme.S4), header.height),
                $"выполнено {done} из {n}", t.captionRight, UiTheme.TextSecondary);

            float cw = (main.width - (GridColumns - 1) * gap) / GridColumns;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(main.x + (i % GridColumns) * (cw + gap), cardsTop + (i / GridColumns) * (cardH + gap), cw, cardH);
                bool sel = i == _mission;
                var state = UiTheme.StateOf(r, sel, _inMissions);
                UiTheme.Item(r, state, raised: true, slideId: 0x6d1);
                var p = Progress(i);
                var inner = UiTheme.Inset(r, UiTheme.Px(20f), UiTheme.Px(14f));
                var top = new Rect(inner.x, inner.y, inner.width, UiTheme.Px(24f));
                UiTheme.Text(top, $"{i + 1:00}", t.caption, sel ? UiTheme.Accent : UiTheme.TextSecondary);
                // Status: a word with a square marker (filled = done).
                bool attempted = p != null && p.attempts > 0;
                if (attempted)
                {
                    string status = Completed(i) ? "выполнена" : "не выполнена";
                    Color sc = Completed(i) ? UiTheme.Success : UiTheme.Warning;
                    float sw = UiTheme.Width(status, t.caption);
                    UiTheme.Text(top, status, t.captionRight, sc);
                    UiTheme.Marker(new Rect(top.xMax - sw - UiTheme.Px(18f), top.y, UiTheme.Px(10f), top.height), sc, Completed(i));
                }
                UiTheme.TextFit(new Rect(inner.x, inner.y + UiTheme.Px(28f), inner.width, UiTheme.Px(38f)), missionNames[i], t.heading,
                    state == UiState.Normal ? UiTheme.TextBody : UiTheme.TextPrimary);
                string note = attempted ? $"лучший {p.bestScore} · попыток {p.attempts}" : "не начата";
                UiTheme.Text(new Rect(inner.x, inner.yMax - UiTheme.Px(24f), inner.width, UiTheme.Px(24f)), note, t.caption, UiTheme.TextSecondary);

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
                {
                    if (_inMissions && sel && Event.current.clickCount > 1) Launch();
                    _mission = i;
                    _inMissions = true;
                    Event.current.Use();
                }
            }
        }

        void DrawBriefing(Rect r)
        {
            var t = UiTheme.T;
            UiTheme.Panel(r);
            var inner = UiTheme.Inset(r, UiTheme.Px(28f), UiTheme.Px(UiTheme.S5));
            float y = inner.y;
            UiTheme.Text(new Rect(inner.x, y, inner.width, UiTheme.Px(26f)), $"ЗАДАЧА {_mission + 1:00}", t.caption, UiTheme.Accent);
            y += UiTheme.Px(30f);
            float th = t.titleWrap.CalcHeight(new GUIContent(missionNames[_mission]), inner.width);
            UiTheme.Text(new Rect(inner.x, y, inner.width, th), missionNames[_mission], t.titleWrap, UiTheme.TextPrimary);
            y += th + UiTheme.Px(UiTheme.S3);

            var button = new Rect(inner.x, inner.yMax - UiTheme.Px(60f), inner.width, UiTheme.Px(60f));
            // The record (attempts, best score) is on the mission card; the column is the briefing text.
            string briefing = _mission < missionBriefings.Length ? missionBriefings[_mission] : "";
            UiTheme.TextBlockFit(new Rect(inner.x, y, inner.width, button.y - y - UiTheme.Px(UiTheme.S5)), briefing, t.body, UiTheme.TextBody);

            if (UiTheme.Button(button, "К ВЫЛЕТУ", true, _inMissions, _inMissions ? ControlInput.Key("Enter", "A") : null)) Launch();
        }

        static string DeviceSummary()
        {
            var js = ControlInput.PreferredJoystick;
            var gp = UnityEngine.InputSystem.Gamepad.current;
            return ControlSettings.Current.device switch
            {
                ControlDevice.Joystick => "аппаратура",
                ControlDevice.Gamepad => "геймпад",
                ControlDevice.KeyboardMouse => "клавиатура + мышь",
                _ => js != null ? $"авто · {js.displayName}" : gp != null ? $"авто · {gp.displayName}" : "авто · клавиатура"
            };
        }
    }
}
