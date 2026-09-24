using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Drone;
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

        readonly MenuGui _gui = new MenuGui();
        readonly List<MenuRow> _rows = new List<MenuRow>();
        SettingsMenu _settings;
        bool _inSettings;
        int _selected;
        float _angle = 200f;

        void Awake()
        {
            GamePause.Set(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _settings = new SettingsMenu(_gui);

            _rows.Add(MenuRow.Button("Задача 1: Стоянка у посадки", () => GameScenes.Load(GameScenes.Mission01)));
            _rows.Add(MenuRow.Button("Задача 2: Прикрытие РЭБ", () => GameScenes.Load(GameScenes.Mission02)));
            _rows.Add(MenuRow.Button("Задача 3: Огневые позиции", () => GameScenes.Load(GameScenes.Mission03)));
            _rows.Add(MenuRow.Button("Полигон — свободный полёт", () => GameScenes.Load(GameScenes.ProvingGround)));
            _rows.Add(MenuRow.Button("Настройки управления", () => { _settings.Open(); _inSettings = true; }));
            _rows.Add(MenuRow.Header(""));
            _rows.Add(MenuRow.Button("Выход", GameScenes.Quit));
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
