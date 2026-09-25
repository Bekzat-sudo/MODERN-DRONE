using System;
using System.Collections.Generic;
using ModernDrone.Drone;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModernDrone.UI
{
    /// <summary>
    /// Control settings screen (tabs: device, keyboard+mouse, gamepad, RC/joystick, buttons, flight).
    /// Plain class hosted by the main menu and the pause menu. Call <see cref="Update"/> from Update
    /// and <see cref="Draw"/> from OnGUI.
    /// </summary>
    public sealed class SettingsMenu
    {
        static readonly string[] Tabs = { "Устройство", "Клавиатура+мышь", "Геймпад", "Аппаратура / джойстик", "Кнопки", "Полёт", "Экран и звук" };

        readonly MenuGui _gui;
        readonly List<MenuRow> _rows = new List<MenuRow>();
        int _tab;
        int _selected;
        CalibrationWizard _wizard;

        // Rebinding capture
        Action<string> _capture;
        Func<InputDevice, bool> _captureFilter;
        string _captureLabel;
        int _captureStartFrame;

        public bool Closed { get; private set; }

        /// <summary>True while waiting for a key/button or running calibration: hosts must not react to Esc etc.</summary>
        public bool IsCapturing => _capture != null || _wizard != null;

        public SettingsMenu(MenuGui gui)
        {
            _gui = gui;
            BuildRows();
        }

        static ControlSettings S => ControlSettings.Current;

        public void Open()
        {
            Closed = false;
            _tab = 0;
            _selected = 0;
            BuildRows();
        }

        public void Update()
        {
            if (_wizard != null)
            {
                _wizard.Update();
                var n = _gui.ReadNav();
                if (n.confirm) _wizard.Next();
                if (n.back) _wizard.Cancel();
                if (_wizard.Finished) { _wizard = null; BuildRows(); }
                return;
            }

            if (_capture != null)
            {
                PollCapture();
                return;
            }

            var nav = _gui.ReadNav();
            if (nav.back)
            {
                ControlSettings.Save();
                Closed = true;
                return;
            }
            if (nav.tabPrev) SwitchTab(_tab - 1);
            if (nav.tabNext) SwitchTab(_tab + 1);
            MenuGui.Navigate(_rows, ref _selected, nav);
        }

        void SwitchTab(int tab)
        {
            _tab = (tab + Tabs.Length) % Tabs.Length;
            _selected = 0;
            BuildRows();
        }

        // ---------------------------------------------------------------- rows

        void BuildRows()
        {
            _rows.Clear();
            switch (_tab)
            {
                case 0: DeviceRows(); break;
                case 1: KeyboardMouseRows(); break;
                case 2: GamepadRows(); break;
                case 3: JoystickRows(); break;
                case 4: BindingRows(); break;
                case 5: FlightRows(); break;
                case 6: DisplayAudioRows(); break;
            }
            _rows.Add(MenuRow.Header(""));
            _rows.Add(MenuRow.Button("← Назад (сохранить)", () => { ControlSettings.Save(); Closed = true; }));
        }

        void DeviceRows()
        {
            _rows.Add(MenuRow.Choice("Устройство управления", new[] { "Авто", "Аппаратура / джойстик", "Геймпад", "Клавиатура + мышь" },
                () => (int)S.device, v => S.device = (ControlDevice)v));
            _rows.Add(new MenuRow("Джойстик / аппаратура")
            {
                Value = () => ControlInput.PreferredJoystick?.displayName ?? "не подключено",
                Adjust = CycleJoystick,
                Activate = () => CycleJoystick(1)
            });
            _rows.Add(MenuRow.Info("Геймпад", () => Gamepad.current?.displayName ?? "не подключён"));
            _rows.Add(MenuRow.Choice("Режим при старте", new[] { "Acro", "Angle" }, () => (int)S.startMode, v => S.startMode = (FlightMode)v));
            _rows.Add(MenuRow.Header("Подключённые устройства"));
            foreach (var d in InputSystem.devices)
                if (d is Joystick || d is Gamepad || d is Keyboard || d is Mouse)
                    _rows.Add(MenuRow.Info("  " + d.displayName, () => d is Joystick ? "джойстик" : d is Gamepad ? "геймпад" : d is Keyboard ? "клавиатура" : "мышь"));
            _rows.Add(MenuRow.Header(""));
            _rows.Add(MenuRow.Button("Сбросить все настройки управления", () => { ControlSettings.ResetToDefaults(); BuildRows(); }));
        }

        static void CycleJoystick(int dir)
        {
            var list = new List<Joystick>();
            foreach (var d in InputSystem.devices)
                if (d is Joystick j) list.Add(j);
            if (list.Count == 0) return;
            int idx = list.FindIndex(j => j == ControlInput.PreferredJoystick);
            idx = (idx + dir + list.Count) % list.Count;
            S.joystickName = list[idx].displayName;
        }

        void KeyboardMouseRows()
        {
            _rows.Add(MenuRow.Header("Мышь — тангаж и крен (виртуальный стик)"));
            _rows.Add(MenuRow.Slider("Чувствительность наклонов", () => S.mouseSensitivity * 1000f, v => S.mouseSensitivity = v / 1000f, 0.25f, 10f, 0.25f, "0.00"));
            _rows.Add(MenuRow.Slider("Возврат в центр, /с (0 — держит)", () => S.mouseReturn, v => S.mouseReturn = v, 0f, 10f, 0.5f, "0.0"));
            _rows.Add(MenuRow.Choice("Тангаж: мышь вверх", new[] { "нос вниз (как стик FPV)", "нос вверх" }, () => S.mousePitchUpIsNoseUp ? 1 : 0, v => S.mousePitchUpIsNoseUp = v == 1));
            _rows.Add(MenuRow.Toggle("Инверсия крена", () => S.mouseInvertRoll, v => S.mouseInvertRoll = v));
            _rows.Add(MenuRow.Header("Клавиатура"));
            _rows.Add(MenuRow.Slider("Скорость газа, /с", () => S.kbThrottleSpeed, v => S.kbThrottleSpeed = v, 0.1f, 2f, 0.1f, "0.0"));
            _rows.Add(MenuRow.Slider("Сила рыскания", () => S.kbYaw, v => S.kbYaw = v, 0.1f, 1f, 0.05f));
            _rows.Add(MenuRow.Info("Колесо мыши", () => "газ ±"));
            _rows.Add(MenuRow.Info("Средняя кнопка мыши", () => "центр стика"));
            _rows.Add(MenuRow.Info("Клавиши газа / рыскания", () => "вкладка «Кнопки»"));
        }

        void GamepadRows()
        {
            _rows.Add(MenuRow.Choice("Режим газа", new[] { "Центр = 50%", "Центр = висение", "Газ на удержание", "Правый курок" },
                () => (int)S.gpThrottleMode, v => S.gpThrottleMode = (GamepadThrottleMode)v));
            _rows.Add(MenuRow.Slider("Скорость газа (удержание), /с", () => S.gpIncrementalSpeed, v => S.gpIncrementalSpeed = v, 0.1f, 3f, 0.1f, "0.0"));
            _rows.Add(MenuRow.Slider("Мёртвая зона", () => S.gpDeadzone, v => S.gpDeadzone = v, 0f, 0.25f, 0.01f));
            _rows.Add(MenuRow.Slider("Экспо стиков", () => S.gpExpo, v => S.gpExpo = v, 0f, 0.9f, 0.05f));
            _rows.Add(MenuRow.Slider("Чувствительность наклонов", () => S.gpTiltSensitivity, v => S.gpTiltSensitivity = v, 0.2f, 1.5f, 0.05f));
            _rows.Add(MenuRow.Choice("Тангаж: стик вверх", new[] { "нос вниз (FPV)", "нос вверх" }, () => S.gpPitchUpIsNoseUp ? 1 : 0, v => S.gpPitchUpIsNoseUp = v == 1));
            _rows.Add(MenuRow.Toggle("Инверсия крена", () => S.gpInvertRoll, v => S.gpInvertRoll = v));
            _rows.Add(MenuRow.Header("Схема Mode 2"));
            _rows.Add(MenuRow.Info("Левый стик", () => "газ / рыскание"));
            _rows.Add(MenuRow.Info("Правый стик", () => "тангаж / крен"));
            _rows.Add(MenuRow.Info("Левый стик сейчас", () => Gamepad.current != null ? Vec(Gamepad.current.leftStick.ReadUnprocessedValue()) : "—"));
            _rows.Add(MenuRow.Info("Правый стик сейчас", () => Gamepad.current != null ? Vec(Gamepad.current.rightStick.ReadUnprocessedValue()) : "—"));
        }

        void JoystickRows()
        {
            _rows.Add(MenuRow.Info("Устройство", () => ControlInput.PreferredJoystick?.displayName ?? "не подключено"));
            _rows.Add(MenuRow.Button("▶ Мастер калибровки", () =>
            {
                var d = ControlInput.PreferredJoystick;
                if (d != null) _wizard = new CalibrationWizard(d);
            }));
            _rows.Add(MenuRow.Slider("Мёртвая зона", () => S.jsDeadband, v => S.jsDeadband = v, 0f, 0.1f, 0.005f, "0.000"));
            _rows.Add(MenuRow.Toggle("Арм тумблером (ось)", () => S.jsUseArmAxis, v => S.jsUseArmAxis = v));
            _rows.Add(MenuRow.Toggle("Режим тумблером (ось)", () => S.jsUseModeAxis, v => S.jsUseModeAxis = v));
            _rows.Add(MenuRow.Header("Оси (← → инвертировать)"));
            AxisRow("Газ", () => S.jsThrottle, true);
            AxisRow("Крен", () => S.jsRoll, false);
            AxisRow("Тангаж", () => S.jsPitch, false);
            AxisRow("Рыскание", () => S.jsYaw, false);
            AxisRow("Арм", () => S.jsArm, false);
            AxisRow("Режим", () => S.jsMode, false);
        }

        void AxisRow(string name, Func<AxisCalibration> axis, bool unipolar)
        {
            _rows.Add(new MenuRow(name)
            {
                Value = () =>
                {
                    var a = axis();
                    var js = ControlInput.PreferredJoystick;
                    float? v = unipolar ? a.ReadUnipolar(js) : a.ReadBipolar(js);
                    string val = v.HasValue ? v.Value.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture) : "нет оси";
                    return $"{a.control}{(a.invert ? " (инв.)" : "")}  {val}";
                },
                Adjust = _ => axis().invert = !axis().invert
            });
        }

        void BindingRows()
        {
            _rows.Add(MenuRow.Info("Enter/A — назначить; при ожидании кнопки Backspace — очистить", () => ""));
            foreach (PilotAction action in Enum.GetValues(typeof(PilotAction)))
            {
                var a = action;
                bool keyboardOnly = a >= PilotAction.ThrottleUp && a <= PilotAction.ThrottleCut;
                _rows.Add(MenuRow.Header(ActionName(a)));
                _rows.Add(BindRow("  Клавиатура / мышь", a, b => b.keyboard, (b, v) => b.keyboard = v, d => d is Keyboard || d is Mouse));
                if (keyboardOnly) continue;
                _rows.Add(BindRow("  Геймпад", a, b => b.gamepad, (b, v) => b.gamepad = v, d => d is Gamepad));
                _rows.Add(BindRow("  Джойстик / аппаратура", a, b => b.joystick, (b, v) => b.joystick = v, d => d is Joystick));
            }
            _rows.Add(MenuRow.Header(""));
            _rows.Add(MenuRow.Button("Сбросить кнопки по умолчанию", () => { S.bindings = ControlSettings.DefaultBindings(); BuildRows(); }));
        }

        MenuRow BindRow(string label, PilotAction action, Func<ActionBinding, string> get, Action<ActionBinding, string> set, Func<InputDevice, bool> filter)
        {
            return MenuRow.Button(label, () =>
            {
                _captureLabel = $"{ActionName(action)} — {label.Trim()}";
                _captureFilter = filter;
                _captureStartFrame = Time.frameCount;
                _capture = path => set(S.Binding(action), path);
            }, () => ControlInput.DisplayName(get(S.Binding(action))));
        }

        void PollCapture()
        {
            // Skip the frame that started the capture (Enter / A is still "pressed this frame").
            if (Time.frameCount <= _captureStartFrame) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { _capture = null; return; }
            if (kb != null && kb.backspaceKey.wasPressedThisFrame) { Finish(""); return; }

            foreach (var device in InputSystem.devices)
            {
                if (!_captureFilter(device)) continue;
                foreach (var c in device.allControls)
                {
                    if (!(c is ButtonControl b) || c.synthetic || c.noisy) continue;
                    if (c.parent is StickControl) continue; // stick directions are not buttons
                    if (b.wasPressedThisFrame)
                    {
                        Finish(ControlInput.GenericPath(c));
                        return;
                    }
                }
            }
        }

        void Finish(string path)
        {
            _capture?.Invoke(path);
            _capture = null;
        }

        void DisplayAudioRows()
        {
            _rows.Add(MenuRow.Header("Экран"));
            _rows.Add(MenuRow.Toggle("Полный экран", () => S.fullscreen, v => { S.fullscreen = v; ControlSettings.NotifyChanged(); }));
            _rows.Add(MenuRow.Toggle("Вертикальная синхронизация", () => S.vSync, v => { S.vSync = v; ControlSettings.NotifyChanged(); }));
            _rows.Add(MenuRow.Header("Звук"));
            _rows.Add(MenuRow.Slider("Общая громкость", () => S.masterVolume, v => { S.masterVolume = v; ControlSettings.NotifyChanged(); }, 0f, 1f, 0.05f));
            _rows.Add(MenuRow.Slider("Моторы дрона", () => S.motorVolume, v => S.motorVolume = v, 0f, 1f, 0.05f));
        }

        void FlightRows()
        {
            _rows.Add(MenuRow.Header("Плавность"));
            _rows.Add(MenuRow.Slider("Сглаживание стиков, Гц (меньше — плавнее)", () => S.stickSmoothingHz, v => { S.stickSmoothingHz = v; ControlSettings.NotifyChanged(); }, 4f, 60f, 1f, "0"));
            RateRows("Крен", () => S.rollRates, v => S.rollRates = v);
            RateRows("Тангаж", () => S.pitchRates, v => S.pitchRates = v);
            RateRows("Рыскание", () => S.yawRates, v => S.yawRates = v);
            _rows.Add(MenuRow.Header("Режим Angle"));
            _rows.Add(MenuRow.Slider("Макс. угол наклона, °", () => S.angleMaxTilt, v => { S.angleMaxTilt = v; ControlSettings.NotifyChanged(); }, 15f, 80f, 5f, "0"));
            _rows.Add(MenuRow.Header("Камера"));
            _rows.Add(MenuRow.Slider("Наклон камеры к штатному, °", () => S.cameraTiltOffset, v => S.cameraTiltOffset = v, -30f, 40f, 1f, "+0;-0;0"));
            _rows.Add(MenuRow.Slider("Угол обзора (гориз.), °", () => S.cameraFov, v => S.cameraFov = v, 80f, 150f, 1f, "0"));
        }

        void RateRows(string axis, Func<AxisRates> get, Action<AxisRates> set)
        {
            _rows.Add(MenuRow.Header($"{axis}: макс. {get().MaxRateDeg:0} °/с"));
            _rows.Add(MenuRow.Slider("  Центр, °/с", () => get().centerRate, v => { var r = get(); r.centerRate = v; set(r); Changed(); }, 20f, 400f, 10f, "0"));
            _rows.Add(MenuRow.Slider("  Максимум, °/с", () => get().maxRate, v => { var r = get(); r.maxRate = v; set(r); Changed(); }, 100f, 1400f, 25f, "0"));
            _rows.Add(MenuRow.Slider("  Экспо", () => get().expo, v => { var r = get(); r.expo = v; set(r); Changed(); }, 0f, 1f, 0.05f));
        }

        void Changed()
        {
            ControlSettings.NotifyChanged();
            // Header shows the max rate; rebuild keeps it current without losing the selection.
            int sel = _selected;
            BuildRows();
            _selected = sel;
        }

        static string ActionName(PilotAction a) => a switch
        {
            PilotAction.Arm => "Арм / дизарм",
            PilotAction.Mode => "Режим Acro / Angle",
            PilotAction.Reset => "Сброс дрона",
            PilotAction.TiltUp => "Камера выше",
            PilotAction.TiltDown => "Камера ниже",
            PilotAction.Pause => "Пауза / меню",
            PilotAction.ThrottleUp => "Газ +",
            PilotAction.ThrottleDown => "Газ −",
            PilotAction.YawLeft => "Рыскание влево",
            PilotAction.YawRight => "Рыскание вправо",
            PilotAction.ThrottleCut => "Сброс газа",
            PilotAction.Restart => "Перезапуск задачи",
            _ => a.ToString()
        };

        static string Vec(Vector2 v) => $"{v.x:+0.00;-0.00}  {v.y:+0.00;-0.00}";

        // ---------------------------------------------------------------- drawing

        public void Draw(Rect r)
        {
            _gui.EnsureStyles();
            MenuGui.Fill(r, MenuGui.PanelColor);
            float pad = r.height * 0.03f;
            float tabH = _gui.RowHeight * 1.2f;
            GUI.Label(new Rect(r.x, r.y + pad, r.width, _gui.title.fontSize * 1.2f), "УПРАВЛЕНИЕ", _gui.title);

            var tabs = new Rect(r.x + pad, r.y + pad + _gui.title.fontSize * 1.5f, r.width - pad * 2, tabH);
            var body = new Rect(r.x + pad, tabs.yMax + pad, r.width - pad * 2, r.yMax - tabs.yMax - pad * 3 - _gui.hint.fontSize * 2f);

            if (_wizard != null)
            {
                DrawWizard(body);
            }
            else
            {
                int newTab = _gui.DrawTabs(tabs, Tabs, _tab);
                if (newTab != _tab) SwitchTab(newTab);
                _gui.DrawRows(body, _rows, ref _selected);
            }

            string hint = _wizard != null ? "Enter / A — далее,  Esc / B — отмена"
                : "↑↓ — выбор,  ←→ — значение,  Enter / A — действие,  Q/E или LB/RB — вкладки,  Esc / B — назад";
            GUI.Label(new Rect(r.x, r.yMax - pad - _gui.hint.fontSize * 1.6f, r.width, _gui.hint.fontSize * 1.6f), hint, _gui.hint);

            if (_capture != null)
            {
                var box = new Rect(r.center.x - r.width * 0.3f, r.center.y - r.height * 0.1f, r.width * 0.6f, r.height * 0.2f);
                MenuGui.Fill(box, new Color(0f, 0f, 0f, 0.95f));
                GUI.Label(new Rect(box.x, box.y + box.height * 0.15f, box.width, box.height * 0.4f), _captureLabel, _gui.rowSelected);
                GUI.Label(new Rect(box.x, box.y + box.height * 0.55f, box.width, box.height * 0.35f), "Нажмите кнопку…  (Esc — отмена, Backspace — очистить)", _gui.hint);
            }
        }

        void DrawWizard(Rect body)
        {
            float h = _gui.RowHeight;
            GUI.Label(new Rect(body.x, body.y, body.width, h), $"Калибровка: {_wizard.Device.displayName} — {_wizard.StepLabel}", _gui.rowSelected);
            var instr = new GUIStyle(_gui.row) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            GUI.Label(new Rect(body.x, body.y + h, body.width, h * 3f), _wizard.Instruction, instr);
            if (_wizard.Error != null)
            {
                var err = new GUIStyle(_gui.row);
                err.normal.textColor = new Color(1f, 0.4f, 0.3f);
                GUI.Label(new Rect(body.x, body.y + h * 4f, body.width, h), _wizard.Error, err);
            }

            float y = body.y + h * 5f;
            float bw = body.width * 0.18f;
            if (GUI.Button(new Rect(body.x, y, bw, h), _wizard.IsLastStep ? "Сохранить" : "Далее")) _wizard.Next();
            if (_wizard.CanSkip && GUI.Button(new Rect(body.x + bw + 10f, y, bw, h), "Пропустить")) _wizard.Skip();
            if (GUI.Button(new Rect(body.x + (bw + 10f) * 2f, y, bw, h), "Отмена")) _wizard.Cancel();
            if (_wizard != null && _wizard.Finished) { _wizard = null; BuildRows(); return; }

            y += h * 1.5f;
            GUI.Label(new Rect(body.x, y, body.width, h), $"Монитор осей ({_wizard.AxisCount})", _gui.header);
            y += h;
            foreach (var (path, raw, n, assigned) in _wizard.Monitor())
            {
                if (y > body.yMax - h) break;
                GUI.Label(new Rect(body.x, y, body.width * 0.25f, h), (assigned ? "✔ " : "   ") + path, _gui.row);
                MenuGui.Bar(new Rect(body.x + body.width * 0.27f, y + h * 0.3f, body.width * 0.55f, h * 0.4f), n, true);
                GUI.Label(new Rect(body.x + body.width * 0.84f, y, body.width * 0.16f, h),
                    raw.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), _gui.value);
                y += h * 0.8f;
            }
        }
    }
}
