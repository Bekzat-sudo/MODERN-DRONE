using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Missions;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>Start screen: missions, free flight, control settings, quit. Slowly orbits a backdrop.</summary>
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

        readonly MenuGui _gui = new MenuGui();
        readonly List<MenuRow> _rows = new List<MenuRow>();
        SettingsMenu _settings;
        bool _inSettings;
        Hangar _hangar;
        bool _inHangar;
        int _selected;
        float _angle = 200f;

        void Awake()
        {
            GamePause.Set(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _settings = new SettingsMenu(_gui);

            AddMission("Задача 1: Стоянка у посадки", GameScenes.Mission01);
            AddMission("Задача 2: Прикрытие РЭБ", GameScenes.Mission02);
            AddMission("Задача 3: Огневые позиции", GameScenes.Mission03);
            AddMission("Задача 4: Колонна", GameScenes.Mission04);
            AddMission("Задача 5: Позиция ЗРК", GameScenes.Mission05);
            AddMission("Задача 6: Ложные цели", GameScenes.Mission06);
            AddMission("Задача 7: Ночная охота", GameScenes.Mission07);
            AddMission("Задача 8: Охота на вертолёт", GameScenes.Mission08);
            _rows.Add(MenuRow.Button("Полигон — свободный полёт", () => GameScenes.Load(GameScenes.ProvingGround)));
            _rows.Add(MenuRow.Button("Ангар — сборка дрона", OpenHangar, HangarSummary));
            _rows.Add(MenuRow.Button("Настройки управления", () => { _settings.Open(); _inSettings = true; }));
            _rows.Add(MenuRow.Header(""));
            _rows.Add(MenuRow.Button("Выход", GameScenes.Quit));
        }

        public void SetHangar(DroneConfig[] models, DronePart[] options, WarheadData[] warheadOptions, HangarPreview turntable)
        {
            droneModels = models;
            parts = options;
            warheads = warheadOptions;
            preview = turntable;
        }

        void OpenHangar()
        {
            _hangar ??= new Hangar(droneModels, parts, warheads, preview, false);
            _hangar.Open();
            _inHangar = true;
        }

        /// <summary>The saved build's drone, shown next to the menu row.</summary>
        string HangarSummary()
        {
            string saved = PlayerPrefs.GetString("loadout/drone", "");
            var model = System.Array.Find(droneModels, m => m.name == saved) ?? (droneModels.Length > 0 ? droneModels[0] : null);
            return model != null ? model.displayName : "";
        }

        void AddMission(string label, string scene) =>
            _rows.Add(MenuRow.Button(label, () => GameScenes.Load(scene), () => ProgressText(scene)));

        /// <summary>Completion mark and best score from saved progress.</summary>
        static string ProgressText(string scene)
        {
            var e = MissionProgress.Current.entries.Find(x => x.mission == scene);
            if (e == null || e.attempts == 0) return "";
            return e.completed ? $"✔ {e.bestScore}" : $"не выполнена · {e.bestScore}";
        }

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
                }
            }

            if (_inHangar)
            {
                _hangar.Update();
                if (_gui.ReadNav().back)
                {
                    _hangar.Close();
                    _inHangar = false;
                }
                return;
            }
            if (_inSettings)
            {
                _settings.Update();
                if (_settings.Closed) _inSettings = false;
                return;
            }
            MenuGui.Navigate(_rows, ref _selected, _gui.ReadNav());
        }

        void OnGUI()
        {
            _gui.EnsureStyles();
            float w = Screen.width, h = Screen.height;

            if (_inHangar)
            {
                _hangar.Draw("АНГАР", "↑↓ — слот,  ← → — вариант,  тянуть мышью — повернуть дрон,  ESC / B — назад (сборка сохраняется)");
                return;
            }
            if (_inSettings)
            {
                _settings.Draw(new Rect(w * 0.12f, h * 0.06f, w * 0.76f, h * 0.88f));
                return;
            }

            var panel = new Rect(w * 0.06f, h * 0.2f, w * 0.36f, h * 0.62f);
            MenuGui.Fill(panel, MenuGui.PanelColor);
            GUI.Label(new Rect(panel.x, panel.y + h * 0.03f, panel.width, _gui.title.fontSize * 1.3f), "MODERN DRONE", _gui.title);
            GUI.Label(new Rect(panel.x, panel.y + h * 0.03f + _gui.title.fontSize * 1.4f, panel.width, _gui.subtitle.fontSize * 1.5f),
                "FPV-симулятор", _gui.subtitle);

            float top = panel.y + h * 0.18f;
            _gui.DrawRows(new Rect(panel.x + 20f, top, panel.width - 40f, panel.yMax - top - h * 0.08f), _rows, ref _selected);

            string device = DeviceSummary();
            GUI.Label(new Rect(panel.x, panel.yMax - h * 0.07f, panel.width, h * 0.05f), device, _gui.hint);
        }

        static string DeviceSummary()
        {
            var js = ControlInput.PreferredJoystick;
            var gp = UnityEngine.InputSystem.Gamepad.current;
            string d = ControlSettings.Current.device switch
            {
                ControlDevice.Joystick => "аппаратура / джойстик",
                ControlDevice.Gamepad => "геймпад",
                ControlDevice.KeyboardMouse => "клавиатура + мышь",
                _ => js != null ? $"авто: {js.displayName}" : gp != null ? $"авто: {gp.displayName}" : "авто: клавиатура + мышь"
            };
            return $"Управление: {d}";
        }
    }
}
