using System;
using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Video;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Drone customisation screen ("tactical tablet" look), shared by the main menu and the pre-sortie loadout:
    /// drone models as tabs, numbered slots on the left, the 3D drone on a turntable in the middle (selected part pulled
    /// out and outlined), options of the selected slot on the right with the change each one makes, resulting drone
    /// below. The last build of each drone model is remembered (PlayerPrefs) and used by every mission.
    /// </summary>
    public sealed class Hangar
    {
        enum Row { Battery, Propellers, Camera, Link, Warhead }

        static readonly (Row row, PartSlot slot)[] PartRows =
        {
            (Row.Battery, PartSlot.Battery),
            (Row.Propellers, PartSlot.Propellers),
            (Row.Camera, PartSlot.Camera),
            (Row.Link, PartSlot.VideoLink),
        };

        struct Stats
        {
            public float mass, twr, minutes;
            public string range;
        }

        readonly DroneConfig[] _models;
        readonly DronePart[] _parts;
        readonly WarheadData[] _warheads;
        readonly HangarPreview _preview;
        readonly bool _mission;
        readonly Func<bool, int> _linkStock;
        readonly List<Row> _rows = new List<Row>();
        readonly List<DronePart> _options = new List<DronePart>();
        readonly DronePart[] _picked = new DronePart[4]; // by PartSlot
        readonly List<Stats> _optionStats = new List<Stats>();
        Stats _current, _stock;
        bool _dirty = true;
        int _model = -1, _warhead, _row;
        GUIStyle _title, _tab, _caption, _captionAccent, _value, _option, _optionSel, _tag, _body, _statLabel, _statValue, _prompt, _key, _back;

        /// <summary>Called when the back button is clicked (keyboard / gamepad back is read by the owner).</summary>
        public Action Back;

        /// <param name="mission">
        /// In a mission only the thermal cores are offered at night and only analog cameras by day; in the menu every
        /// camera is offered (the night missions then fit the thermal one).
        /// </param>
        /// <param name="linkStock">Drones left with a fiber (true) / radio (false) link, or null for no limit.</param>
        public Hangar(DroneConfig[] models, DronePart[] parts, WarheadData[] warheads, HangarPreview preview,
            bool mission, Func<bool, int> linkStock = null)
        {
            _models = models ?? new DroneConfig[0];
            _parts = parts ?? new DronePart[0];
            _warheads = warheads ?? new WarheadData[0];
            _preview = preview;
            _mission = mission;
            _linkStock = linkStock;
        }

        public DroneConfig Model => _models.Length > 0 ? _models[Mathf.Clamp(_model, 0, _models.Length - 1)] : null;
        public WarheadData Warhead => _warheads.Length > 0 ? _warheads[Mathf.Clamp(_warhead, 0, _warheads.Length - 1)] : null;
        public DronePart Picked(PartSlot slot) => _picked[(int)slot];
        public IReadOnlyList<DronePart> PickedParts => _picked;
        /// <summary>Anything to choose (a screen worth showing).</summary>
        public bool HasChoices => _rows.Count > 0 || _models.Length > 1;

        /// <summary>Restores the last build and lists the slots with a choice.</summary>
        public void Open()
        {
            if (_model < 0)
            {
                _model = Mathf.Max(0, Array.FindIndex(_models, m => m.name == PlayerPrefs.GetString("loadout/drone", "")));
                _warhead = Mathf.Max(0, Array.FindIndex(_warheads, w => w.name == PlayerPrefs.GetString("loadout/warhead", "")));
            }
            PickSaved();
            _rows.Clear();
            foreach (var (row, slot) in PartRows)
                if (Options(slot).Count > 1) _rows.Add(row);
            if (_warheads.Length > 1) _rows.Add(Row.Warhead);
            _row = 0;
            _dirty = true;
        }

        /// <summary>Saves the build and hides the turntable.</summary>
        public void Close()
        {
            foreach (var (_, slot) in PartRows)
                if (_picked[(int)slot] != null) PlayerPrefs.SetString(PrefKey(slot), _picked[(int)slot].name);
            if (Model != null) PlayerPrefs.SetString("loadout/drone", Model.name);
            if (Warhead != null) PlayerPrefs.SetString("loadout/warhead", Warhead.name);
            PlayerPrefs.Save();
            if (_preview != null) _preview.Hide();
        }

        /// <summary>Runtime config of the current build (models without stock parts fly as defined).</summary>
        public DroneConfig Compose() => Compose(_picked);

        DroneConfig Compose(IEnumerable<DronePart> parts) => Model.stockParts.Length > 0 ? DroneBuild.Compose(Model, parts) : Model;

        public static string WarheadHint(WarheadData w) => w.kind == WarheadKind.Fragmentation
            ? "против открытых целей: РЛС, орудия, грузовики"
            : w.tandem ? "пробивает динамическую защиту" : "против брони: борт, корма, крыша";

        // ---- parts

        List<DronePart> Options(PartSlot slot)
        {
            _options.Clear();
            foreach (var p in _parts)
            {
                if (p == null || p.slot != slot) continue;
                if (_mission && slot == PartSlot.Camera && p.thermal != NightMode.Night) continue;
                if (slot == PartSlot.VideoLink && _linkStock != null && _linkStock(p.fiber) <= 0) continue;
                _options.Add(p);
            }
            return _options;
        }

        DronePart Stock(PartSlot slot)
        {
            var model = Model;
            if (model != null)
                foreach (var p in model.stockParts)
                    if (p != null && p.slot == slot) return p;
            return null;
        }

        string PrefKey(PartSlot slot) => $"loadout/{(Model != null ? Model.name : "")}/{slot}";

        /// <summary>Every slot starts from this model's last build, else its stock part, else the first allowed one.</summary>
        void PickSaved()
        {
            foreach (var (_, slot) in PartRows)
            {
                var options = Options(slot);
                string saved = PlayerPrefs.GetString(PrefKey(slot), "");
                var stock = Stock(slot);
                _picked[(int)slot] = options.Find(p => p.name == saved)
                    ?? (options.Contains(stock) ? stock : options.Count > 0 ? options[0] : stock);
            }
        }

        // ---- rows and their options

        static PartSlot SlotOf(Row row)
        {
            foreach (var (r, slot) in PartRows)
                if (r == row) return slot;
            return PartSlot.Battery;
        }

        static string RowName(Row row) => row switch
        {
            Row.Battery => "Батарея",
            Row.Propellers => "Винты",
            Row.Camera => "Камера",
            Row.Link => "Канал",
            _ => "Боевая часть"
        };

        int OptionCount(Row row) => row == Row.Warhead ? _warheads.Length : Options(SlotOf(row)).Count;

        int OptionIndex(Row row) => row == Row.Warhead ? _warhead : Options(SlotOf(row)).IndexOf(_picked[(int)SlotOf(row)]);

        void SelectOption(Row row, int i)
        {
            int n = OptionCount(row);
            if (n == 0) return;
            i = (i % n + n) % n;
            if (row == Row.Warhead) _warhead = i;
            else _picked[(int)SlotOf(row)] = Options(SlotOf(row))[i];
            _dirty = true;
        }

        void SelectModel(int i)
        {
            if (_models.Length == 0) return;
            _model = (i % _models.Length + _models.Length) % _models.Length;
            PickSaved();
            _dirty = true;
        }

        string OptionName(Row row, int i) => row == Row.Warhead ? _warheads[i].displayName : Options(SlotOf(row))[i].displayName;

        /// <summary>Mass (or thrust for props) of an option, as listed.</summary>
        string OptionTag(Row row, int i)
        {
            if (row == Row.Warhead) return $"{_warheads[i].mass:0.0} кг";
            var p = Options(SlotOf(row))[i];
            return p.slot == PartSlot.Propellers ? $"тяга ×{p.thrustScale:0.00}" : $"{p.mass * 1000f:0} г";
        }

        string Specs(Row row)
        {
            if (row == Row.Warhead) return $"{Warhead.mass:0.0} кг\n{WarheadHint(Warhead)}";
            var p = _picked[(int)SlotOf(row)];
            if (p == null) return "";
            string specs = p.slot switch
            {
                PartSlot.Battery => $"{p.cells}S · {p.capacityMah:0} мА·ч · {p.internalResistance * 1000f:0} мОм",
                PartSlot.Camera => p.thermal ? $"{p.thermalResolution.x}×{p.thermalResolution.y} · поле {p.horizontalFov:0}°" : "аналог · поле по настройке пилота",
                PartSlot.Propellers => $"тяга ×{p.thrustScale:0.00} · ток ×{p.currentScale:0.00} · шаг ×{p.pitchSpeedScale:0.00}",
                _ => p.fiber ? $"кабель {p.spoolLength / 1000f:0} км"
                             : $"видео до {RadioParams.Default.videoRangeEnd * p.videoRangeScale / 1000f:0.0} км · листва ×{p.foliageBlock:0.00}"
            };
            string note = p.slot == PartSlot.Camera && p.thermal && !_mission ? "\nставится в ночных задачах" : "";
            string stock = p.slot == PartSlot.VideoLink && _linkStock != null ? $"\nосталось дронов: {_linkStock(p.fiber)}" : "";
            return specs + "\n" + p.summary + note + stock;
        }

        // ---- stats (computed in Update when the build changes, never per GUI event)

        Stats Measure(IEnumerable<DronePart> parts, WarheadData warhead)
        {
            var cfg = Compose(parts);
            DronePart link = null;
            foreach (var p in parts) if (p != null && p.slot == PartSlot.VideoLink) link = p;
            float payload = (warhead != null ? warhead.mass : 0f) + (link != null && link.fiber ? link.mass : 0f);
            var s = new Stats
            {
                mass = cfg.frameMass + payload,
                minutes = DroneBuild.HoverMinutes(cfg, payload),
                range = link == null ? "—" : link.fiber ? $"{link.spoolLength / 1000f:0} км кабель" : $"{RadioParams.Default.videoRangeEnd * link.videoRangeScale / 1000f:0.0} км"
            };
            s.twr = cfg.frameMass * cfg.thrustToWeight / s.mass;
            if (cfg != Model) UnityEngine.Object.Destroy(cfg);
            return s;
        }

        void Refresh()
        {
            _dirty = false;
            _optionStats.Clear();
            if (Model == null || _rows.Count == 0 && _models.Length == 0) return;
            _current = Measure(_picked, Warhead);
            var stock = new DronePart[_picked.Length];
            foreach (var (_, slot) in PartRows) stock[(int)slot] = Stock(slot) ?? _picked[(int)slot];
            _stock = Measure(stock, Warhead);
            if (_rows.Count == 0) return;
            var row = _rows[_row];
            int n = OptionCount(row);
            var trial = (DronePart[])_picked.Clone();
            for (int i = 0; i < n; i++)
            {
                if (row == Row.Warhead)
                {
                    _optionStats.Add(Measure(_picked, _warheads[i]));
                    continue;
                }
                trial[(int)SlotOf(row)] = Options(SlotOf(row))[i];
                _optionStats.Add(Measure(trial, Warhead));
            }
        }

        static string Delta(float value, float reference, string unit, string format)
        {
            float d = value - reference;
            if (Mathf.Abs(d) < 0.5f) return ""; // below the shown precision
            return d > 0f
                ? UiTheme.Colored($"▲ +{d.ToString(format)}{unit}", UiTheme.Better)
                : UiTheme.Colored($"▼ −{(-d).ToString(format)}{unit}", UiTheme.Worse);
        }

        static string Change(float from, float to, string format, bool higherIsBetter = true)
        {
            string arrow = Mathf.Abs(to - from) < 0.05f ? "" : (to > from) == higherIsBetter ? UiTheme.Colored(" ▲", UiTheme.Better) : UiTheme.Colored(" ▼", UiTheme.Worse);
            return $"{from.ToString(format)} → {to.ToString(format)}{arrow}";
        }

        // ---- input

        /// <summary>Keyboard / gamepad navigation and the turntable (call from Update).</summary>
        public void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            bool prevModel = (kb != null && kb.qKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame);
            bool nextModel = (kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame);
            if (prevModel || nextModel) SelectModel(_model + (nextModel ? 1 : -1));
            int n = _rows.Count;
            if (n > 0)
            {
                if (up) { _row = (_row + n - 1) % n; _dirty = true; }
                if (down) { _row = (_row + 1) % n; _dirty = true; }
                if (left || right) SelectOption(_rows[_row], OptionIndex(_rows[_row]) + (right ? 1 : -1));
            }
            if (_dirty) Refresh();

            if (_preview != null)
            {
                var r = Layout().preview;
                var sel = n > 0 ? _rows[_row] : Row.Warhead;
                var highlight = sel == Row.Warhead ? null : _picked[(int)SlotOf(sel)];
                _preview.Show(Model, _picked[(int)PartSlot.Propellers], highlight, new Vector2Int((int)r.width, (int)r.height));
            }
        }

        // ---- drawing

        struct Areas
        {
            public Rect header, slots, preview, options, footer, prompts;
        }

        static Areas Layout()
        {
            float w = Screen.width, h = Screen.height, p = UiTheme.Px(56f), gap = UiTheme.Px(24f);
            float top = UiTheme.Px(40f), headerH = UiTheme.Px(64f);
            float bodyTop = top + headerH + UiTheme.Px(28f);
            float footerH = UiTheme.Px(76f), promptsH = UiTheme.Px(40f);
            float promptsTop = h - UiTheme.Px(28f) - promptsH;
            float footerTop = promptsTop - UiTheme.Px(16f) - footerH;
            float bodyH = footerTop - UiTheme.Px(20f) - bodyTop;
            float slotsW = UiTheme.Px(440f), optionsW = UiTheme.Px(500f);
            return new Areas
            {
                header = new Rect(p, top, w - 2 * p, headerH),
                slots = new Rect(p, bodyTop, slotsW, bodyH),
                preview = new Rect(p + slotsW + gap, bodyTop, w - 2 * p - slotsW - optionsW - 2 * gap, bodyH),
                options = new Rect(w - p - optionsW, bodyTop, optionsW, bodyH),
                footer = new Rect(p, footerTop, w - 2 * p, footerH),
                prompts = new Rect(p, promptsTop, w - 2 * p, promptsH),
            };
        }

        /// <summary>Full-screen hangar (call from OnGUI).</summary>
        public void Draw(string title, string note, params (string key, string action)[] prompts)
        {
            EnsureStyles();
            UiTheme.Background();
            var a = Layout();
            var e = Event.current;
            bool click = e.type == EventType.MouseDown && e.button == 0;

            // Header: back, title, drone tabs, note
            float x = a.header.x;
            if (Back != null)
            {
                var back = new Rect(x, a.header.y, a.header.height, a.header.height);
                UiTheme.Box(back);
                GUI.Label(back, "‹", _back);
                if (click && back.Contains(e.mousePosition)) { Back(); e.Use(); }
                x += back.width + UiTheme.Px(24f);
            }
            float tw = _title.CalcSize(new GUIContent(title)).x;
            GUI.Label(new Rect(x, a.header.y, tw, a.header.height), title, _title);
            x += tw + UiTheme.Px(32f);
            for (int i = 0; i < _models.Length; i++)
            {
                string label = _models[i].displayName.ToUpperInvariant();
                var r = new Rect(x, a.header.y + UiTheme.Px(6f), _tab.CalcSize(new GUIContent(label)).x + UiTheme.Px(40f), a.header.height - UiTheme.Px(12f));
                bool sel = i == _model;
                UiTheme.Box(r, sel);
                _tab.normal.textColor = sel ? UiTheme.Text : UiTheme.Muted;
                GUI.Label(r, label, _tab);
                if (click && r.Contains(e.mousePosition)) { SelectModel(i); e.Use(); }
                x += r.width + UiTheme.Px(6f);
            }
            GUI.Label(new Rect(x, a.header.y, a.header.xMax - x, a.header.height), note, _captionRight);

            if (Model == null) return;

            // Slots
            GUI.Label(new Rect(a.slots.x, a.slots.y, a.slots.width, UiTheme.Px(30f)), "СЛОТЫ", _caption);
            float sh = UiTheme.Px(92f), sy = a.slots.y + UiTheme.Px(40f);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = new Rect(a.slots.x, sy + i * (sh + UiTheme.Px(8f)), a.slots.width, sh);
                bool sel = i == _row;
                UiTheme.Box(r, sel);
                var inner = new Rect(r.x + UiTheme.Px(18f), r.y + UiTheme.Px(12f), r.width - UiTheme.Px(36f), r.height - UiTheme.Px(24f));
                GUI.Label(new Rect(inner.x, inner.y, inner.width, inner.height * 0.42f), $"{i + 1:00} · {RowName(row).ToUpperInvariant()}", sel ? _captionAccent : _caption);
                GUI.Label(new Rect(inner.x, inner.y + inner.height * 0.45f, inner.width, inner.height * 0.55f), OptionName(row, OptionIndex(row)), _value);
                if (click && r.Contains(e.mousePosition)) { _row = i; _dirty = true; e.Use(); }
            }

            // 3D view
            UiTheme.Box(a.preview, raised: true);
            if (_preview != null && _preview.Texture != null)
            {
                GUI.DrawTexture(new Rect(a.preview.x + 1, a.preview.y + 1, a.preview.width - 2, a.preview.height - 2), _preview.Texture, ScaleMode.StretchToFill, false);
                if (e.type == EventType.MouseDrag && e.button == 0 && a.preview.Contains(e.mousePosition))
                {
                    _preview.Orbit(e.delta);
                    e.Use();
                }
            }
            GUI.Label(new Rect(a.preview.x + UiTheme.Px(18f), a.preview.y + UiTheme.Px(12f), a.preview.width, UiTheme.Px(28f)), "ВИД 3D · ТЯНИТЕ ДЛЯ ПОВОРОТА", _caption);
            GUI.Label(new Rect(a.preview.x + UiTheme.Px(18f), a.preview.yMax - UiTheme.Px(40f), a.preview.width - UiTheme.Px(36f), UiTheme.Px(28f)),
                $"{Model.displayName.ToUpperInvariant()} · ДИАГОНАЛЬ {Model.motorDiagonal * 1000f:0} мм · РАМА {Model.airframeMass:0.00} кг", _caption);

            // Options of the selected slot
            UiTheme.Box(a.options);
            if (_rows.Count > 0)
            {
                var selRow = _rows[_row];
                int count = OptionCount(selRow), current = OptionIndex(selRow);
                var o = new Rect(a.options.x + UiTheme.Px(18f), a.options.y + UiTheme.Px(16f), a.options.width - UiTheme.Px(36f), a.options.height - UiTheme.Px(32f));
                GUI.Label(new Rect(o.x, o.y, o.width, UiTheme.Px(28f)), $"{RowName(selRow).ToUpperInvariant()} · {count} {Variants(count)}", _caption);
                float oh = UiTheme.Px(66f), oy = o.y + UiTheme.Px(40f);
                for (int i = 0; i < count; i++)
                {
                    var r = new Rect(o.x - UiTheme.Px(6f), oy + i * (oh + UiTheme.Px(4f)), o.width + UiTheme.Px(12f), oh);
                    bool sel = i == current;
                    if (sel) UiTheme.Box(r, true);
                    string delta = i < _optionStats.Count && !sel ? Delta(_optionStats[i].minutes, _current.minutes, " мин", "0") : "";
                    // Name on top, mass and the change it makes below (before/after comparison per option)
                    GUI.Label(new Rect(r.x + UiTheme.Px(12f), r.y + UiTheme.Px(6f), r.width - UiTheme.Px(24f), r.height * 0.5f), OptionName(selRow, i), sel ? _optionSel : _option);
                    GUI.Label(new Rect(r.x + UiTheme.Px(12f), r.y + r.height * 0.5f, r.width - UiTheme.Px(24f), r.height * 0.42f),
                        $"{OptionTag(selRow, i)}{(delta.Length > 0 ? "  " + delta : sel ? "  " + UiTheme.Colored("установлено", UiTheme.Accent) : "")}", _tag);
                    if (click && r.Contains(e.mousePosition)) { SelectOption(selRow, i); e.Use(); }
                }
                float dy = oy + count * (oh + UiTheme.Px(4f)) + UiTheme.Px(16f);
                UiTheme.Fill(new Rect(o.x, dy, o.width, 1f), UiTheme.Line);
                dy += UiTheme.Px(14f);
                string compare = $"Висение к стандарту: {Change(_stock.minutes, _current.minutes, "0")} мин\nТяга / вес к стандарту: {Change(_stock.twr, _current.twr, "0.0")}";
                GUI.Label(new Rect(o.x, dy, o.width, o.yMax - dy), Specs(selRow) + "\n\n" + compare, _body);
            }

            // Resulting drone
            float cw = (a.footer.width - 3 * UiTheme.Px(12f)) / 4f;
            Stat(new Rect(a.footer.x, a.footer.y, cw, a.footer.height), "ВЗЛЁТНАЯ МАССА", $"{_current.mass:0.00} кг");
            Stat(new Rect(a.footer.x + (cw + UiTheme.Px(12f)), a.footer.y, cw, a.footer.height), "ТЯГА / ВЕС", $"{_current.twr:0.0}");
            Stat(new Rect(a.footer.x + 2 * (cw + UiTheme.Px(12f)), a.footer.y, cw, a.footer.height), "ВИСЕНИЕ", $"~{_current.minutes:0} мин");
            Stat(new Rect(a.footer.x + 3 * (cw + UiTheme.Px(12f)), a.footer.y, cw, a.footer.height), "ДАЛЬНОСТЬ ВИДЕО", _current.range);

            UiTheme.Prompts(a.prompts, _prompt, _key, prompts);
        }

        static string Variants(int n) => n % 10 == 1 && n % 100 != 11 ? "ВАРИАНТ" : n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 10 || n % 100 >= 20) ? "ВАРИАНТА" : "ВАРИАНТОВ";

        void Stat(Rect r, string label, string value)
        {
            UiTheme.Box(r);
            GUI.Label(new Rect(r.x + UiTheme.Px(18f), r.y, r.width * 0.55f, r.height), label, _statLabel);
            GUI.Label(new Rect(r.x, r.y, r.width - UiTheme.Px(18f), r.height), value, _statValue);
        }

        GUIStyle _captionRight;

        void EnsureStyles()
        {
            int size = UiTheme.FontPx(20f);
            if (_value != null && _value.fontSize == size) return;
            _title = UiTheme.Style(UiTheme.Display, 46f, UiTheme.Text);
            _title.fontStyle = FontStyle.Bold;
            _tab = UiTheme.Style(UiTheme.Display, 22f, UiTheme.Text, TextAnchor.MiddleCenter);
            _back = UiTheme.Style(UiTheme.Display, 36f, UiTheme.Text, TextAnchor.MiddleCenter);
            _caption = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted);
            _captionAccent = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Accent);
            _captionRight = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted, TextAnchor.MiddleRight);
            _value = UiTheme.Style(UiTheme.Mono, 20f, UiTheme.Text);
            _option = UiTheme.Style(UiTheme.Mono, 20f, UiTheme.Body);
            _optionSel = UiTheme.Style(UiTheme.MonoBold, 20f, UiTheme.Text);
            _tag = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted);
            _body = UiTheme.Style(UiTheme.Mono, 19f, UiTheme.Body, TextAnchor.UpperLeft, true);
            _statLabel = UiTheme.Style(UiTheme.Mono, 18f, UiTheme.Muted);
            _statValue = UiTheme.Style(UiTheme.Display, 34f, UiTheme.Text, TextAnchor.MiddleRight);
            _prompt = UiTheme.Style(UiTheme.Mono, 19f, UiTheme.Body);
            _key = UiTheme.Style(UiTheme.MonoBold, 17f, UiTheme.Ground, TextAnchor.MiddleCenter);
        }
    }
}
