using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Drone;
using ModernDrone.Missions;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// In-game pause (Pause binding: Esc / View): a side panel over the frozen feed with the task's state (drone,
    /// targets, time) and the actions resume, settings, restart, main menu.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        readonly MenuGui _gui = new MenuGui();
        readonly List<MenuRow> _rows = new List<MenuRow>();
        SettingsMenu _settings;
        MissionController _mission;
        bool _inSettings;
        int _selected;
        int _openedFrame = -1;

        void Awake()
        {
            _settings = new SettingsMenu(_gui);
            _rows.Add(MenuRow.Button("Продолжить", Resume));
            _rows.Add(MenuRow.Button("Настройки", () => { _settings.Open(); _inSettings = true; }, () => "управление · видео"));
            _rows.Add(MenuRow.Button("Перезапустить", GameScenes.Reload, () => "задача с начала"));
            _rows.Add(MenuRow.Header(""));
            _rows.Add(new MenuRow("Главное меню") { Activate = () => GameScenes.Load(GameScenes.MainMenu), Value = () => "без сохранения", Danger = true });
        }

        void Start() => _mission = FindAnyObjectByType<MissionController>();

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
            var t = UiTheme.T;
            float w = Screen.width, h = Screen.height;
            var screen = new Rect(0, 0, w, h);

            if (_inSettings)
            {
                UiTheme.Fill(screen, UiTheme.Scrim);
                _settings.Draw(screen);
                return;
            }

            UiTheme.Fill(screen, new Color(0f, 0f, 0f, 0.4f));
            float pw = Mathf.Clamp(w * 0.34f, UiTheme.Px(520f), UiTheme.Px(640f));
            var panel = new Rect(0, 0, pw, h);
            UiTheme.Fill(panel, new Color(UiTheme.Surface.r, UiTheme.Surface.g, UiTheme.Surface.b, 0.97f));
            UiTheme.Fill(new Rect(panel.xMax - 1f, 0, 1f, h), UiTheme.Border);

            float m = UiTheme.Px(UiTheme.Margin), x = m, cw = pw - m - UiTheme.Px(UiTheme.S7);
            float y = UiTheme.Px(72f);
            UiTheme.Text(new Rect(x, y, cw, UiTheme.Px(26f)), "ПАУЗА", t.caption, UiTheme.Accent);
            y += UiTheme.Px(32f);
            string title = _mission != null ? _mission.MissionName : "";
            float th = t.titleWrap.CalcHeight(new GUIContent(title), cw);
            UiTheme.Text(new Rect(x, y, cw, th), title, t.titleWrap, UiTheme.TextPrimary);
            y += th + UiTheme.Px(UiTheme.S5);

            if (_mission != null && !_mission.FreeFlight)
            {
                float lh = UiTheme.Px(32f);
                int flown = Mathf.Min(_mission.SortiesFlown + 1, _mission.Sorties);
                UiTheme.KeyValue(new Rect(x, y, cw, lh), "ДРОН", $"{flown} из {_mission.Sorties}", UiTheme.TextPrimary);
                y += lh;
                UiTheme.Segments(new Rect(x, y + UiTheme.Px(2f), cw, UiTheme.Px(6f)), _mission.Sorties, _mission.Sorties - _mission.SortiesFlown, UiTheme.TextBody, flown - 1);
                y += UiTheme.Px(UiTheme.S5);
                UiTheme.KeyValue(new Rect(x, y, cw, lh), "ЦЕЛИ", $"{_mission.TargetsDone} из {_mission.RequiredTargets}",
                    _mission.TargetsDone >= _mission.RequiredTargets ? UiTheme.Success : UiTheme.TextPrimary);
                y += lh;
                int s = (int)_mission.MissionTime;
                UiTheme.KeyValue(new Rect(x, y, cw, lh), "ВРЕМЯ", $"{s / 60:00}:{s % 60:00}", UiTheme.TextPrimary);
                y += lh;
            }

            y += UiTheme.Px(UiTheme.S6);
            float rowPad = UiTheme.Px(UiTheme.S5);
            float listH = MenuGui.RowHeight(MenuGui.RowSize.Large) * _rows.Count;
            _gui.DrawRows(new Rect(x - rowPad, y, cw + 2f * rowPad, listH), _rows, ref _selected, MenuGui.RowSize.Large);

            if (UiTheme.Prompts(new Rect(x, h - UiTheme.Px(UiTheme.S8) - UiTheme.Px(36f), cw, UiTheme.Px(36f)), false,
                    (ControlInput.Key("Enter", "A"), "выбрать"), (ControlInput.Key("Esc", "B"), "продолжить")) == 1)
                Resume();
        }
    }
}
