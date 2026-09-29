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
    /// and a recon window onto the slowly orbiting backdrop.
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

        enum Nav { Campaign, Hangar, ProvingGround, Settings, Quit }
        static readonly (Nav nav, string label)[] NavItems =
        {
            (Nav.Campaign, "КАМПАНИЯ"), (Nav.Hangar, "АНГАР"), (Nav.ProvingGround, "ПОЛИГОН"),
            (Nav.Settings, "НАСТРОЙКИ"), (Nav.Quit, "ВЫХОД"),
        };
        const int GridColumns = 4;

        readonly MenuGui _gui = new MenuGui();
        SettingsMenu _settings;
        bool _inSettings, _inHangar, _inMissions;
        Hangar _hangar;
        int _nav, _mission = -1;
        float _angle = 200f;
        Rect _recon; // screen rect of the recon window (the backdrop camera renders into it)
        GUIStyle _kicker, _brand, _navLabel, _navNote, _caption, _captionAccent, _cardTitle, _cardNote, _title, _body, _button, _prompt, _key;

        void Awake()
        {
            GamePause.Set(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _settings = new SettingsMenu(_gui);
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

        void Activate(Nav nav)
        {
            switch (nav)
            {
                case Nav.Campaign: _inMissions = true; break;
                case Nav.Hangar: OpenHangar(); break;
                case Nav.ProvingGround: GameScenes.Load(GameScenes.ProvingGround); break;
                case Nav.Settings: _settings.Open(); _inSettings = true; break;
                case Nav.Quit: GameScenes.Quit(); break;
            }
        }

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
            if (nav.up) _nav = (_nav + NavItems.Length - 1) % NavItems.Length;
            if (nav.down) _nav = (_nav + 1) % NavItems.Length;
            if (nav.confirm || nav.right && NavItems[_nav].nav == Nav.Campaign) Activate(NavItems[_nav].nav);
        }

        // ---- drawing

        void OnGUI()
        {
            _gui.EnsureStyles();
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            if (_inHangar)
            {
                _hangar.Draw("АНГАР", "СБОРКА СОХРАНЯЕТСЯ · ИСПОЛЬЗУЕТСЯ ВО ВСЕХ ЗАДАЧАХ",
                    ("Q·E / LB·RB", "дрон"), ("↑↓", "слот"), ("←→", "вариант"), ("ESC / B", "назад"));
                return;
            }
            if (_inSettings)
            {
                UiTheme.Background();
                _settings.Draw(new Rect(w * 0.12f, h * 0.06f, w * 0.76f, h * 0.88f));
                return;
            }
            if (_mission < 0) return;

            float navW = UiTheme.Px(620f), pad = UiTheme.Px(72f);
            var main = new Rect(navW + UiTheme.Px(56f), UiTheme.Px(64f), w - navW - UiTheme.Px(112f), h - UiTheme.Px(112f));

            // Mission cards and briefing sit below; the recon window shows the 3D backdrop through the ground.
            float cardsTop = main.y + UiTheme.Px(44f), cardH = UiTheme.Px(118f), gap = UiTheme.Px(14f);
            int rows = Mathf.CeilToInt(missionScenes.Length / (float)GridColumns);
            float lowerTop = cardsTop + rows * (cardH + gap) + UiTheme.Px(10f);
            float briefW = UiTheme.Px(460f);
            var recon = new Rect(main.x, lowerTop, main.width - briefW - UiTheme.Px(24f), main.yMax - lowerTop);
            _recon = recon;
            UiTheme.Background(recon);
            UiTheme.Frame(recon, UiTheme.Line);
            UiTheme.Fill(new Rect(recon.x + 1, recon.y + 1, UiTheme.Px(250f), UiTheme.Px(34f)), UiTheme.Panel);
            GUI.Label(new Rect(recon.x + UiTheme.Px(14f), recon.y + 1, UiTheme.Px(240f), UiTheme.Px(34f)), "РАЗВЕДКА · ОБЪЕКТ", _caption);

            DrawNav(new Rect(0, 0, navW, h), pad);
            DrawCampaign(main, cardsTop, cardH, gap);
            DrawBriefing(new Rect(main.xMax - briefW, lowerTop, briefW, main.yMax - lowerTop));
        }

        void DrawNav(Rect r, float pad)
        {
            var e = Event.current;
            UiTheme.Fill(r, UiTheme.Panel);
            UiTheme.Fill(new Rect(r.xMax - 1, 0, 1, r.height), UiTheme.Line);
            float x = pad, width = r.width - pad - UiTheme.Px(48f), y = UiTheme.Px(72f);
            GUI.Label(new Rect(x, y, width, UiTheme.Px(28f)), "// FPV · УДАРНЫЙ КОМПЛЕКС", _kicker);
            GUI.Label(new Rect(x, y + UiTheme.Px(30f), width, UiTheme.Px(90f)), "MODERN DRONE", _brand);
            y += UiTheme.Px(150f);
            float rh = UiTheme.Px(72f);
            int done = 0;
            for (int i = 0; i < missionScenes.Length; i++) if (Completed(i)) done++;
            for (int i = 0; i < NavItems.Length; i++)
            {
                var (nav, label) = NavItems[i];
                var row = new Rect(x - UiTheme.Px(20f), y + i * (rh + UiTheme.Px(6f)), width + UiTheme.Px(20f), rh);
                bool sel = i == _nav && !_inMissions || nav == Nav.Campaign && _inMissions;
                if (sel) UiTheme.Box(row, true);
                _navLabel.normal.textColor = nav == Nav.Quit && !sel ? UiTheme.Muted : UiTheme.Text;
                GUI.Label(new Rect(row.x + UiTheme.Px(20f), row.y, row.width, row.height), label, _navLabel);
                string note = nav switch
                {
                    Nav.Campaign => $"{done} / {missionScenes.Length}",
                    Nav.Hangar => SavedDrone(),
                    Nav.ProvingGround => "свободный полёт",
                    Nav.Settings => "управление · видео",
                    _ => ""
                };
                _navNote.normal.textColor = nav == Nav.Campaign ? UiTheme.Accent : UiTheme.Muted;
                GUI.Label(new Rect(row.x, row.y, row.width - UiTheme.Px(20f), row.height), note, _navNote);
                if (e.type == EventType.MouseMove && row.Contains(e.mousePosition) && !_inMissions) _nav = i;
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                {
                    _nav = i;
                    Activate(nav);
                    e.Use();
                }
            }

            float bottom = r.height - UiTheme.Px(56f);
            UiTheme.Fill(new Rect(x, bottom - UiTheme.Px(96f), width, 1f), UiTheme.Line);
            Line(new Rect(x, bottom - UiTheme.Px(84f), width, UiTheme.Px(30f)), "УПРАВЛЕНИЕ", DeviceSummary());
            Line(new Rect(x, bottom - UiTheme.Px(52f), width, UiTheme.Px(30f)), "ДРОН ИЗ АНГАРА", SavedDrone());
            UiTheme.Prompts(new Rect(x, bottom - UiTheme.Px(4f), width, UiTheme.Px(36f)), _prompt, _key,
                _inMissions ? new[] { ("A", "вылет"), ("B", "назад"), ("←→↑↓", "задача") } : new[] { ("A", "выбрать"), ("↑↓", "пункт") });
        }

        void Line(Rect r, string label, string value)
        {
            GUI.Label(r, label, _caption);
            _navNote.normal.textColor = UiTheme.Text;
            GUI.Label(r, value, _navNote);
        }

        void DrawCampaign(Rect main, float cardsTop, float cardH, float gap)
        {
            var e = Event.current;
            GUI.Label(new Rect(main.x, main.y, main.width, UiTheme.Px(32f)), "КАМПАНИЯ · ВЫБОР ЗАДАЧИ", _caption);
            float cw = (main.width - (GridColumns - 1) * gap) / GridColumns;
            for (int i = 0; i < missionScenes.Length; i++)
            {
                var r = new Rect(main.x + (i % GridColumns) * (cw + gap), cardsTop + (i / GridColumns) * (cardH + gap), cw, cardH);
                bool sel = i == _mission;
                bool done = Completed(i);
                var p = Progress(i);
                UiTheme.Box(r, sel && _inMissions, raised: true);
                if (sel && !_inMissions) UiTheme.Frame(r, UiTheme.Accent);
                var inner = new Rect(r.x + UiTheme.Px(16f), r.y + UiTheme.Px(12f), r.width - UiTheme.Px(32f), r.height - UiTheme.Px(24f));
                string mark = done ? UiTheme.Colored($"● {i + 1:00}", UiTheme.Done) : sel ? UiTheme.Colored($"▶ {i + 1:00}", UiTheme.Accent) : $"{i + 1:00}";
                GUI.Label(new Rect(inner.x, inner.y, inner.width, inner.height * 0.28f), mark, _caption);
                GUI.Label(new Rect(inner.x, inner.y + inner.height * 0.28f, inner.width, inner.height * 0.42f), missionNames[i], _cardTitle);
                string note = p == null || p.attempts == 0 ? "не начата" : done ? $"лучший: {p.bestScore}" : $"не выполнена · {p.bestScore}";
                GUI.Label(new Rect(inner.x, inner.y + inner.height * 0.72f, inner.width, inner.height * 0.28f), note, _cardNote);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    if (_inMissions && sel && e.clickCount > 1) Launch();
                    _mission = i;
                    _inMissions = true;
                    e.Use();
                }
            }
        }

        void DrawBriefing(Rect r)
        {
            var e = Event.current;
            UiTheme.Box(r);
            var inner = new Rect(r.x + UiTheme.Px(24f), r.y + UiTheme.Px(22f), r.width - UiTheme.Px(48f), r.height - UiTheme.Px(44f));
            GUI.Label(new Rect(inner.x, inner.y, inner.width, UiTheme.Px(28f)), $"ЗАДАЧА {_mission + 1:00}", _captionAccent);
            GUI.Label(new Rect(inner.x, inner.y + UiTheme.Px(30f), inner.width, UiTheme.Px(60f)), missionNames[_mission], _title);
            var button = new Rect(inner.x, inner.yMax - UiTheme.Px(64f), inner.width, UiTheme.Px(64f));
            float textTop = inner.y + UiTheme.Px(100f);
            string briefing = _mission < missionBriefings.Length ? missionBriefings[_mission] : "";
            var p = Progress(_mission);
            string record = p == null || p.attempts == 0 ? "Не начата" : $"Попыток: {p.attempts} · лучший результат: {p.bestScore}";
            GUI.Label(new Rect(inner.x, textTop, inner.width, button.y - textTop - UiTheme.Px(16f)), briefing + "\n\n" + UiTheme.Colored(record, UiTheme.Muted), _body);
            UiTheme.Fill(button, UiTheme.Accent);
            GUI.Label(button, "К ВЫЛЕТУ ▸", _button);
            if (e.type == EventType.MouseDown && e.button == 0 && button.Contains(e.mousePosition))
            {
                Launch();
                e.Use();
            }
        }

        void EnsureStyles()
        {
            int size = UiTheme.FontPx(18f);
            if (_body != null && _body.fontSize == size) return;
            _kicker = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Accent);
            _brand = UiTheme.Style(UiTheme.Display, 76f, UiTheme.Text);
            _brand.fontStyle = FontStyle.Bold;
            _navLabel = UiTheme.Style(UiTheme.Display, 30f, UiTheme.Text);
            _navNote = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted, TextAnchor.MiddleRight);
            _caption = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted);
            _captionAccent = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Accent);
            _cardTitle = UiTheme.Style(UiTheme.Display, 26f, UiTheme.Text);
            _cardNote = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted);
            _title = UiTheme.Style(UiTheme.Display, 44f, UiTheme.Text);
            _body = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Body, TextAnchor.UpperLeft, true);
            _button = UiTheme.Style(UiTheme.Display, 28f, UiTheme.Ground, TextAnchor.MiddleCenter);
            _prompt = UiTheme.Style(UiTheme.Mono, 19f, UiTheme.Body);
            _key = UiTheme.Style(UiTheme.MonoBold, 17f, UiTheme.Ground, TextAnchor.MiddleCenter);
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
