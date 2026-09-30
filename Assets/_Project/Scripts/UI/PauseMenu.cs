using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>In-game pause (Pause binding: Esc / View): resume, control settings, restart, main menu.</summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        readonly MenuGui _gui = new MenuGui();
        readonly List<MenuRow> _rows = new List<MenuRow>();
        SettingsMenu _settings;
        bool _inSettings;
        int _selected;
        int _openedFrame = -1;

        void Awake()
        {
            _settings = new SettingsMenu(_gui);
            _rows.Add(MenuRow.Button("Продолжить", Resume));
            _rows.Add(MenuRow.Button("Настройки", () => { _settings.Open(); _inSettings = true; }));
            _rows.Add(MenuRow.Button("Перезапустить", GameScenes.Reload));
            _rows.Add(MenuRow.Header(""));
            _rows.Add(MenuRow.Button("Главное меню", () => GameScenes.Load(GameScenes.MainMenu)));
        }

        void OnDestroy()
        {
            if (GamePause.IsPaused) GamePause.Set(false);
        }

        void Update()
        {
            if (!GamePause.IsPaused)
            {
                if (ControlInput.WasPressed(PilotAction.Pause))
                {
                    GamePause.Set(true);
                    _selected = 0;
                    _inSettings = false;
                    _openedFrame = Time.frameCount;
                }
                return;
            }
            if (Time.frameCount == _openedFrame) return; // the same Esc must not close it again

            if (_inSettings)
            {
                _settings.Update();
                if (_settings.Closed) _inSettings = false;
                return;
            }

            var nav = _gui.ReadNav();
            if (nav.back || ControlInput.WasPressed(PilotAction.Pause))
            {
                Resume();
                return;
            }
            MenuGui.Navigate(_rows, ref _selected, nav);
        }

        void Resume()
        {
            ControlSettings.Save();
            GamePause.Set(false);
        }

        void OnGUI()
        {
            if (!GamePause.IsPaused) return;
            GUI.depth = -20;
            _gui.EnsureStyles();
            float w = Screen.width, h = Screen.height;
            MenuGui.Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.45f));

            if (_inSettings)
            {
                _settings.Draw(new Rect(w * 0.12f, h * 0.06f, w * 0.76f, h * 0.88f));
                return;
            }

            var panel = new Rect(w * 0.35f, h * 0.25f, w * 0.3f, h * 0.5f);
            MenuGui.Fill(panel, MenuGui.PanelColor);
            GUI.Label(new Rect(panel.x, panel.y + h * 0.03f, panel.width, _gui.title.fontSize * 1.3f), "ПАУЗА", _gui.title);
            float top = panel.y + h * 0.12f;
            _gui.DrawRows(new Rect(panel.x + 20f, top, panel.width - 40f, panel.yMax - top - 20f), _rows, ref _selected);
        }
    }
}
