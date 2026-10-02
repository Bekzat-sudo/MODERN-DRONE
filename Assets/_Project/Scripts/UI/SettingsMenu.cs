using System;
using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Drone;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModernDrone.UI
{
    /// <summary>
    /// Settings screen (tabs: device, keyboard+mouse, gamepad, RC/joystick, buttons, flight, display and audio, OSD, graphics).
    /// Plain class hosted by the main menu and the pause menu. Call <see cref="Update"/> from Update
    /// and <see cref="Draw"/> from OnGUI.
    /// </summary>
    public sealed class SettingsMenu
    {
        static readonly string[] Tabs = { "Устройство", "Клавиатура+мышь", "Геймпад", "Аппаратура / джойстик", "Кнопки", "Полёт", "Экран и звук", "OSD", "Графика" };

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
            // While a text field is being typed into, keys belong to the field (Esc only ends the edit).
            if (MenuRow.Editing != null) return;
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
                case 7: OsdRows(); break;
                case 8: GraphicsRows(); break;
            }
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
            _rows[_rows.Count - 1].Note = "← → — выбрать другую аппаратуру, если подключено несколько.";
            _rows.Add(MenuRow.Info("Геймпад", () => Gamepad.current?.displayName ?? "не подключён"));
            _rows.Add(MenuRow.Choice("Режим при старте", new[] { "Acro", "Angle" }, () => (int)S.startMode, v => S.startMode = (FlightMode)v));
            _rows.Add(MenuRow.Header("Подключённые устройства"));
            foreach (var d in InputSystem.devices)
                if (d is Joystick || d is Gamepad || d is Keyboard || d is Mouse)
                    _rows.Add(MenuRow.Info(d.displayName, () => d is Joystick ? "джойстик" : d is Gamepad ? "геймпад" : d is Keyboard ? "клавиатура" : "мышь"));
            _rows.Add(MenuRow.Header(""));
            _rows.Add(new MenuRow("Сбросить все настройки управления")
            {
                Activate = () => { ControlSettings.ResetToDefaults(); BuildRows(); }, Danger = true,
                Note = "Кнопки, оси, рейты, OSD и графика вернутся к значениям по умолчанию."
            });
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
            _rows.Add(MenuRow.Slider("Возврат в центр, /с", () => S.mouseReturn, v => S.mouseReturn = v, 0f, 10f, 0.5f, "0.0"));
            _rows[_rows.Count - 1].Note = "Как быстро виртуальный стик сам возвращается в центр. 0 — держит положение, как стик без пружины.";
            _rows.Add(MenuRow.Choice("Тангаж: мышь вверх", new[] { "нос вниз (как стик FPV)", "нос вверх" }, () => S.mousePitchUpIsNoseUp ? 1 : 0, v => S.mousePitchUpIsNoseUp = v == 1));
            _rows.Add(MenuRow.Toggle("Инверсия крена", () => S.mouseInvertRoll, v => S.mouseInvertRoll = v));
            _rows.Add(MenuRow.Header("Клавиатура"));
            _rows.Add(MenuRow.Slider("Скорость газа, /с", () => S.kbThrottleSpeed, v => S.kbThrottleSpeed = v, 0.1f, 2f, 0.1f, "0.0"));
            _rows.Add(MenuRow.Slider("Сила рыскания", () => S.kbYaw, v => S.kbYaw = v, 0.1f, 1f, 0.05f));
            _rows.Add(MenuRow.Info("Колесо мыши", () => "газ ±"));
            _rows.Add(MenuRow.Info("Средняя кнопка мыши", () => "центр стика"));
            _rows.Add(MenuRow.Info("Клавиши газа и рыскания", () => "вкладка «Кнопки»"));
        }

        void GamepadRows()
        {
            _rows.Add(MenuRow.Choice("Режим газа", new[] { "Центр = 50%", "Центр = висение", "Газ на удержание", "Правый курок", "Центр = 0%" },
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
            _rows.Add(new MenuRow("Мастер калибровки")
            {
                Activate = () =>
                {
                    var d = ControlInput.PreferredJoystick;
                    if (d != null) _wizard = new CalibrationWizard(d);
                },
                Enabled = () => ControlInput.PreferredJoystick != null,
                Note = "Пошагово: стики в центр, крайние положения, тумблеры. Нужна подключённая аппаратура или джойстик."
            });
            _rows.Add(MenuRow.Slider("Мёртвая зона", () => S.jsDeadband, v => S.jsDeadband = v, 0f, 0.1f, 0.005f, "0.000"));
            _rows.Add(MenuRow.Toggle("Арм тумблером (ось)", () => S.jsUseArmAxis, v => S.jsUseArmAxis = v));
            _rows.Add(MenuRow.Toggle("Режим тумблером (ось)", () => S.jsUseModeAxis, v => S.jsUseModeAxis = v));
            _rows.Add(MenuRow.Header("Оси"));
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
                Adjust = _ => axis().invert = !axis().invert,
                Note = "← → — инвертировать ось. Справа — назначенная ось и её текущее значение."
            });
        }

        void BindingRows()
        {
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
            _rows.Add(new MenuRow("Сбросить кнопки по умолчанию") { Activate = () => { S.bindings = ControlSettings.DefaultBindings(); BuildRows(); }, Danger = true });
        }

        MenuRow BindRow(string label, PilotAction action, Func<ActionBinding, string> get, Action<ActionBinding, string> set, Func<InputDevice, bool> filter)
        {
            var row = MenuRow.Button(label, () =>
            {
                _captureLabel = $"{ActionName(action)} — {label.Trim()}";
                _captureFilter = filter;
                _captureStartFrame = Time.frameCount;
                _capture = path => set(S.Binding(action), path);
            }, () => ControlInput.DisplayName(get(S.Binding(action))));
            row.IsBinding = true;
            row.Note = "Enter / A — назначить: нажмите нужную кнопку. Backspace во время ожидания — очистить.";
            return row;
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

        // Quick presets: anti-aliasing, render scale, shadows, vegetation, grass, combat effects. STP at a reduced render scale keeps a
        // cleaner image than a spatial filter at full scale on weaker GPUs.
        static readonly (int aa, float scale, int shadows, int veg, int grass, int fx)[] Presets =
        {
            (3, 0.67f, 1, 1, 1, 1), (3, 0.8f, 2, 2, 2, 2), (3, 1f, 3, 3, 3, 3),
        };

        void GraphicsRows()
        {
            _rows.Add(MenuRow.Choice("Готовый набор", new[] { "свой", "низкое", "среднее", "высокое" }, () =>
            {
                for (int i = 0; i < Presets.Length; i++)
                {
                    var p = Presets[i];
                    if (S.antiAliasing == p.aa && Mathf.Abs(S.renderScale - p.scale) < 0.01f && S.shadowQuality == p.shadows && S.vegetationDetail == p.veg && S.grassQuality == p.grass
                        && S.effectsQuality == p.fx)
                        return i + 1;
                }
                return 0;
            }, v =>
            {
                if (v == 0) return;
                var p = Presets[v - 1];
                S.antiAliasing = p.aa; S.renderScale = p.scale; S.shadowQuality = p.shadows; S.vegetationDetail = p.veg; S.grassQuality = p.grass; S.effectsQuality = p.fx;
                ControlSettings.NotifyChanged();
            }));
            _rows.Add(MenuRow.Header("Изображение"));
            _rows.Add(MenuRow.Choice("Сглаживание", ModernDrone.Video.GraphicsQuality.AntiAliasingNames, () => S.antiAliasing, v => { S.antiAliasing = v; ControlSettings.NotifyChanged(); }));
            _rows.Add(MenuRow.Slider("Разрешение рендера", () => S.renderScale, v => { S.renderScale = v; ControlSettings.NotifyChanged(); }, 0.5f, 1f, 0.05f, "0%"));
            _rows[_rows.Count - 1].Note = "Доля от разрешения экрана; STP или FSR 1.0 дотягивают картинку до экрана. Меньше — выше FPS.";
            _rows.Add(MenuRow.Header("Детализация"));
            _rows.Add(MenuRow.Choice("Тени", ModernDrone.Video.GraphicsQuality.LevelNames, () => S.shadowQuality, v => { S.shadowQuality = v; ControlSettings.NotifyChanged(); }));
            _rows.Add(MenuRow.Choice("Деревья и кусты", new[] { "низкая", "средняя", "высокая" }, () => S.vegetationDetail - 1, v => { S.vegetationDetail = v + 1; ControlSettings.NotifyChanged(); }));
            _rows.Add(MenuRow.Choice("Трава", ModernDrone.Video.GraphicsQuality.LevelNames, () => S.grassQuality, v => { S.grassQuality = v; ControlSettings.NotifyChanged(); }));
            _rows.Add(MenuRow.Choice("Эффекты боя", new[] { "низкие", "средние", "высокие" }, () => S.effectsQuality - 1, v => { S.effectsQuality = v + 1; ControlSettings.NotifyChanged(); }));
            _rows[_rows.Count - 1].Note = "Ниже — без марева, кольца ударной волны, искр-угольков и повторных волн огня. Дым и вспышки остаются.";
            _rows.Add(MenuRow.Header("Кадры"));
            _rows.Add(MenuRow.Choice("Ограничение FPS", Array.ConvertAll(ModernDrone.Video.GraphicsQuality.FrameLimits, f => f == 0 ? "нет" : f.ToString()),
                () => S.frameRateLimit, v => { S.frameRateLimit = v; ControlSettings.NotifyChanged(); }));
            _rows[_rows.Count - 1].Note = "Работает при выключенной вертикальной синхронизации (вкладка «Экран и звук»).";
            _rows.Add(MenuRow.Toggle("Счётчик FPS", () => S.showFps, v => S.showFps = v));
        }

        void FlightRows()
        {
            _rows.Add(MenuRow.Header("Плавность"));
            _rows.Add(MenuRow.Slider("Сглаживание стиков, Гц", () => S.stickSmoothingHz, v => { S.stickSmoothingHz = v; ControlSettings.NotifyChanged(); }, 4f, 60f, 1f, "0"));
            _rows[_rows.Count - 1].Note = "Частота среза фильтра стиков: меньше — плавнее, но с задержкой.";
            RateRows("Крен", () => S.rollRates, v => S.rollRates = v);
            RateRows("Тангаж", () => S.pitchRates, v => S.pitchRates = v);
            RateRows("Рыскание", () => S.yawRates, v => S.yawRates = v);
            _rows.Add(MenuRow.Header("Режим Angle"));
            _rows.Add(MenuRow.Slider("Макс. угол наклона, °", () => S.angleMaxTilt, v => { S.angleMaxTilt = v; ControlSettings.NotifyChanged(); }, 15f, 80f, 5f, "0"));
            _rows.Add(MenuRow.Header("Камера"));
            _rows.Add(MenuRow.Slider("Наклон камеры к штатному, °", () => S.cameraTiltOffset, v => S.cameraTiltOffset = v, -30f, 40f, 1f, "+0;-0;0"));
            _rows.Add(MenuRow.Slider("Угол обзора (гориз.), °", () => S.cameraFov, v => S.cameraFov = v, 80f, 150f, 1f, "0"));
            _rows.Add(MenuRow.Slider("«Рыбий глаз» объектива", () => S.lensDistortion, v => { S.lensDistortion = v; ControlSettings.NotifyChanged(); }, 0f, 1f, 0.05f));
            _rows.Add(MenuRow.Toggle("Кадр 4:3 (как в очках)", () => S.frame43, v => { S.frame43 = v; ControlSettings.NotifyChanged(); }));
        }

        static readonly string[] SlotNames = { "выкл", "слева сверху", "сверху", "справа сверху", "слева", "справа", "слева снизу", "снизу", "справа снизу" };

        static readonly (OsdItem item, string label)[] OsdItems =
        {
            (OsdItem.FlightMode, "Режим полёта"), (OsdItem.InputSource, "Устройство управления"), (OsdItem.Link, "Связь (LQ / видео / катушка)"),
            (OsdItem.Timer, "Время полёта"), (OsdItem.Heading, "Курс"), (OsdItem.Voltage, "Напряжение"), (OsdItem.Current, "Ток"),
            (OsdItem.UsedMah, "Израсходовано мА·ч"), (OsdItem.Throttle, "Газ"), (OsdItem.Speed, "Скорость"), (OsdItem.Altitude, "Высота"),
            (OsdItem.VerticalSpeed, "Вертикальная скорость"), (OsdItem.HomeDistance, "До точки взлёта"), (OsdItem.CraftName, "Позывной"),
        };

        static char CraftNameChar(char c)
        {
            c = char.ToUpperInvariant(c);
            return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || " -_.!".IndexOf(c) >= 0 ? c : '\0';
        }

        /// <summary>OSD layout: a preset, then a slot for every element and the graphic overlays.</summary>
        void OsdRows()
        {
            var osd = S.osd;
            _rows.Add(MenuRow.Header("Пресет"));
            _rows.Add(new MenuRow("Минимальный") { Activate = () => { osd.entries = OsdSettings.Minimal(); BuildRows(); }, Note = "Как на ударных FPV: только самое нужное для удара." });
            _rows.Add(new MenuRow("Полный") { Activate = () => { osd.entries = OsdSettings.Full(); BuildRows(); }, Note = "Все элементы, раскладка как в Betaflight." });
            _rows.Add(MenuRow.Header("Элементы и их место"));
            foreach (var (item, label) in OsdItems)
            {
                var it = item;
                _rows.Add(MenuRow.Choice(label, SlotNames, () => (int)osd.SlotOf(it), v => osd.SetSlot(it, (OsdSlot)v)));
            }
            // Betaflight craft name: up to 16 characters, shown upper case; analog OSD fonts hold only Latin letters,
            // digits and a few signs (MAX7456 character set).
            _rows.Add(MenuRow.Text("Текст позывного", () => osd.craftName, v => osd.craftName = v, 16, CraftNameChar));
            _rows[_rows.Count - 1].Note = "Enter — редактировать. Латиница, цифры и « -_.!», до 16 символов: шрифт аналогового OSD (MAX7456) другого не умеет.";
            _rows.Add(MenuRow.Header("Графика"));
            _rows.Add(MenuRow.Toggle("Прицельный крест", () => osd.crosshair, v => osd.crosshair = v));
            _rows.Add(MenuRow.Toggle("Искусственный горизонт", () => osd.horizon, v => osd.horizon = v));
            _rows.Add(MenuRow.Toggle("Боковые шкалы горизонта", () => osd.sidebars, v => osd.sidebars = v));
            _rows.Add(MenuRow.Toggle("Ось заряда", () => osd.chargeAxis, v => osd.chargeAxis = v));
            _rows.Add(MenuRow.Toggle("Предупреждения (батарея, связь)", () => osd.warnings, v => osd.warnings = v));
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

        /// <summary>
        /// Draws the settings panel centred in <paramref name="screen"/> (the host draws what is behind it: the menu
        /// ground or the dimmed game). The panel keeps a readable width on ultrawide screens.
        /// </summary>
        public void Draw(Rect screen)
        {
            var t = UiTheme.T;
            float width = Mathf.Min(screen.width - 2f * UiTheme.Px(UiTheme.Margin), UiTheme.Px(1320f));
            var panel = new Rect(screen.center.x - width * 0.5f, screen.y + UiTheme.Px(UiTheme.S7), width, screen.height - 2f * UiTheme.Px(UiTheme.S7));
            UiTheme.Panel(panel);
            var inner = UiTheme.Inset(panel, UiTheme.Px(40f), UiTheme.Px(UiTheme.S6));

            var header = new Rect(inner.x, inner.y, inner.width, UiTheme.Px(56f));
            UiTheme.Text(header, "НАСТРОЙКИ", t.title, UiTheme.TextPrimary);
            UiTheme.Text(header, "сохраняются при выходе", t.captionRight, UiTheme.TextSecondary);

            var tabs = new Rect(inner.x, header.yMax + UiTheme.Px(UiTheme.S4), inner.width, UiTheme.Px(52f));
            var prompts = new Rect(inner.x, inner.yMax - UiTheme.Px(36f), inner.width, UiTheme.Px(36f));
            var note = new Rect(inner.x, prompts.y - UiTheme.Px(UiTheme.S4) - UiTheme.Px(60f), inner.width, UiTheme.Px(60f));
            // Rows reach into the panel padding so their text lines up with the title.
            float rowPad = UiTheme.Px(UiTheme.S5);
            var body = new Rect(inner.x - rowPad, tabs.yMax + UiTheme.Px(UiTheme.S4), inner.width + 2f * rowPad - UiTheme.Px(UiTheme.S4), note.y - tabs.yMax - UiTheme.Px(UiTheme.S5));

            if (_wizard != null)
            {
                DrawWizard(new Rect(inner.x, tabs.y, inner.width, note.yMax - tabs.y));
            }
            else
            {
                int newTab = UiTheme.Tabs(tabs, Tabs, _tab, ControlInput.Key("Q", "LB"), ControlInput.Key("E", "RB"), 0x5e7);
                if (newTab != _tab) SwitchTab(newTab);
                _gui.DrawRows(body, _rows, ref _selected, focused: _capture == null);

                string text = MenuGui.FocusedNote(_rows, _selected);
                if (text != null)
                {
                    UiTheme.Fill(new Rect(note.x, note.y, note.width, 1f), UiTheme.Border);
                    UiTheme.Text(new Rect(note.x, note.y + UiTheme.Px(UiTheme.S3), note.width, note.height - UiTheme.Px(UiTheme.S3)), text, t.body, UiTheme.TextBody);
                }
            }

            string ok = ControlInput.Key("Enter", "A"), back = ControlInput.Key("Esc", "B");
            if (_wizard != null)
                UiTheme.Prompts(prompts, false, (ok, "далее"), (back, "отмена"));
            else if (UiTheme.Prompts(prompts, false, ("↑↓", "пункт"), ("←→", "значение"), (ok, "выбрать"), (back, "назад")) == 3 && _capture == null)
            {
                ControlSettings.Save();
                Closed = true;
            }

            if (_capture != null)
            {
                // Modal: waiting for a key / button.
                UiTheme.Fill(panel, new Color(UiTheme.Surface.r, UiTheme.Surface.g, UiTheme.Surface.b, 0.85f));
                float mw = Mathf.Min(panel.width * 0.7f, UiTheme.Px(760f)), mh = UiTheme.Px(220f);
                var box = new Rect(panel.center.x - mw * 0.5f, panel.center.y - mh * 0.5f, mw, mh);
                UiTheme.Fill(box, UiTheme.SurfaceRaised);
                UiTheme.Fill(new Rect(box.x, box.y, box.width, UiTheme.Hair(3f)), UiTheme.Accent);
                var b = UiTheme.Inset(box, UiTheme.Px(UiTheme.S6), UiTheme.Px(UiTheme.S5));
                UiTheme.Text(new Rect(b.x, b.y, b.width, UiTheme.Px(26f)), "НАЗНАЧЕНИЕ", t.caption, UiTheme.Accent);
                UiTheme.TextFit(new Rect(b.x, b.y + UiTheme.Px(30f), b.width, UiTheme.Px(44f)), _captureLabel, t.heading, UiTheme.TextPrimary);
                UiTheme.Text(new Rect(b.x, b.y + UiTheme.Px(84f), b.width, UiTheme.Px(30f)), "Нажмите кнопку или клавишу…", t.label, UiTheme.TextBody);
                UiTheme.Prompts(new Rect(b.x, b.yMax - UiTheme.Px(34f), b.width, UiTheme.Px(34f)), false, ("Esc", "отмена"), ("Backspace", "очистить"));
            }
        }

        void DrawWizard(Rect body)
        {
            var t = UiTheme.T;
            float y = body.y;
            UiTheme.Text(new Rect(body.x, y, body.width, UiTheme.Px(26f)), $"КАЛИБРОВКА · {_wizard.Device.displayName}", t.caption, UiTheme.Accent);
            y += UiTheme.Px(30f);
            UiTheme.TextFit(new Rect(body.x, y, body.width, UiTheme.Px(44f)), _wizard.StepLabel, t.heading, UiTheme.TextPrimary);
            y += UiTheme.Px(52f);
            float textW = Mathf.Min(body.width, UiTheme.Px(900f));
            float ih = t.body.CalcHeight(new GUIContent(_wizard.Instruction), textW);
            UiTheme.Text(new Rect(body.x, y, textW, ih), _wizard.Instruction, t.body, UiTheme.TextBody);
            y += ih + UiTheme.Px(UiTheme.S3);
            if (_wizard.Error != null)
            {
                UiTheme.Text(new Rect(body.x, y, body.width, UiTheme.Px(30f)), _wizard.Error, t.label, UiTheme.Danger);
                y += UiTheme.Px(30f);
            }
            y += UiTheme.Px(UiTheme.S4);

            float bw = UiTheme.Px(240f), bh = UiTheme.Px(56f), gap = UiTheme.Px(UiTheme.S3), x = body.x;
            if (UiTheme.Button(new Rect(x, y, bw, bh), _wizard.IsLastStep ? "СОХРАНИТЬ" : "ДАЛЕЕ", true, true, ControlInput.Key("Enter", "A"))) _wizard.Next();
            x += bw + gap;
            if (_wizard.CanSkip)
            {
                if (UiTheme.Button(new Rect(x, y, bw * 0.8f, bh), "ПРОПУСТИТЬ", false, false)) _wizard.Skip();
                x += bw * 0.8f + gap;
            }
            if (UiTheme.Button(new Rect(x, y, bw * 0.8f, bh), "ОТМЕНА", false, false)) _wizard.Cancel();
            if (_wizard != null && _wizard.Finished) { _wizard = null; BuildRows(); return; }

            y += bh + UiTheme.Px(UiTheme.S6);
            UiTheme.Text(new Rect(body.x, y, body.width, UiTheme.Px(26f)), $"МОНИТОР ОСЕЙ · {_wizard.AxisCount}", t.caption, UiTheme.TextSecondary);
            y += UiTheme.Px(34f);
            float lh = UiTheme.Px(34f);
            foreach (var (path, raw, n, assigned) in _wizard.Monitor())
            {
                if (y > body.yMax - lh) break;
                var line = new Rect(body.x, y, body.width, lh);
                UiTheme.Marker(line, assigned ? UiTheme.Success : UiTheme.TextSecondary, assigned);
                UiTheme.Text(new Rect(body.x + UiTheme.Px(22f), y, body.width * 0.25f, lh), path, t.label, assigned ? UiTheme.TextPrimary : UiTheme.TextBody);
                MenuGui.Bar(new Rect(body.x + body.width * 0.28f, y + lh * 0.4f, body.width * 0.54f, UiTheme.Hair(6f)), n, true);
                UiTheme.Text(new Rect(body.x + body.width * 0.84f, y, body.width * 0.16f, lh),
                    raw.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), t.labelRight, UiTheme.TextBody);
                y += lh;
            }
        }
    }
}
