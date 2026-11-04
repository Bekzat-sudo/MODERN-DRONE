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
            s.twr = cfg.LoadedThrustToWeight(s.mass); // under load: what the pilot gets, not the motor rating
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
                ? UiTheme.Colored($"▲ +{d.ToString(format)}{unit}", UiTheme.Info)
                : UiTheme.Colored($"▼ −{(-d).ToString(format)}{unit}", UiTheme.Warning);
        }

        /// <summary>"21 → 18 мин ▼ хуже": the arrow and the word only when the shown values differ.</summary>
        static string Change(float from, float to, string format, string unit, bool higherIsBetter = true)
        {
            string a = from.ToString(format), b = to.ToString(format);
            string verdict = a == b ? "" : (to > from) == higherIsBetter ? UiTheme.Colored("  ▲ лучше", UiTheme.Info) : UiTheme.Colored("  ▼ хуже", UiTheme.Warning);
            return $"{a} → {b}{unit}{verdict}";
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
            float w = Screen.width, h = Screen.height, p = UiTheme.Px(UiTheme.Margin), gap = UiTheme.Px(UiTheme.S5);
            float top = UiTheme.Px(40f), headerH = UiTheme.Px(64f);
            float bodyTop = top + headerH + UiTheme.Px(UiTheme.S5);
            float footerH = UiTheme.Px(92f), promptsH = UiTheme.Px(36f);
            float promptsTop = h - UiTheme.Px(28f) - promptsH;
            float footerTop = promptsTop - UiTheme.Px(UiTheme.S4) - footerH;
            float bodyH = footerTop - UiTheme.Px(UiTheme.S4) - bodyTop;
            float slotsW = UiTheme.Px(420f), optionsW = UiTheme.Px(500f);
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

        /// <summary>Bottom-right slot for the screen's primary button (level with the prompts).</summary>
        public static Rect ActionRect
        {
            get
            {
                var p = Layout().prompts;
                float w = UiTheme.Px(300f), h = UiTheme.Px(52f);
                return new Rect(p.xMax - w, p.yMax - h, w, h);
            }
        }

        /// <summary>Full-screen hangar (call from OnGUI). Returns the prompt clicked, else -1.</summary>
        public int Draw(string title, string note, params (string key, string action)[] prompts)
        {
            var t = UiTheme.T;
            UiTheme.Background();
            var a = Layout();
            var e = Event.current;
            bool click = e.type == EventType.MouseDown && e.button == 0;

            // Header: back, title, drone models as tabs, note
            float x = a.header.x;
            if (Back != null)
            {
                var back = new Rect(x, a.header.y + UiTheme.Px(6f), a.header.height - UiTheme.Px(12f), a.header.height - UiTheme.Px(12f));
                if (UiTheme.Button(back, "‹", false, false)) { Back(); }
                x += back.width + UiTheme.Px(UiTheme.S5);
            }
            float tw = UiTheme.Width(title, t.title);
            UiTheme.Text(new Rect(x, a.header.y, tw + 2f, a.header.height), title, t.title, UiTheme.TextPrimary);
            x += tw + UiTheme.Px(UiTheme.S6);
            if (_models.Length > 1)
            {
                var names = new string[_models.Length];
                string prev = ControlInput.Key("Q", "LB"), next = ControlInput.Key("E", "RB");
                float stripW = UiTheme.KeyWidth(prev) + UiTheme.KeyWidth(next) + 2f * UiTheme.Px(UiTheme.S4);
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = _models[i].displayName.ToUpperInvariant();
                    stripW += UiTheme.Width(names[i], t.tab) + 2f * UiTheme.Px(UiTheme.S4);
                }
                stripW = Mathf.Min(stripW, a.header.xMax - x - UiTheme.Px(200f));
                int picked = UiTheme.Tabs(new Rect(x, a.header.y + UiTheme.Px(6f), stripW, a.header.height - UiTheme.Px(12f)), names, Mathf.Max(0, _model), prev, next, 0x4a6);
                if (picked != _model) SelectModel(picked);
                x += stripW + UiTheme.Px(UiTheme.S6);
            }
            UiTheme.TextFit(new Rect(x, a.header.y, a.header.xMax - x, a.header.height), note, t.captionRight, UiTheme.TextSecondary);

            if (Model == null) return -1;

            // Slots
            UiTheme.Panel(a.slots);
            var sp = UiTheme.Inset(a.slots, 0f, UiTheme.Px(UiTheme.S4));
            UiTheme.Text(new Rect(sp.x + UiTheme.Px(UiTheme.S5), sp.y, sp.width, UiTheme.Px(26f)), "СЛОТЫ", t.caption, UiTheme.TextSecondary);
            float sh = UiTheme.Px(84f), sy = sp.y + UiTheme.Px(36f);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = new Rect(sp.x + 1f, sy + i * sh, sp.width - 2f, sh);
                bool sel = i == _row;
                UiTheme.Item(r, UiTheme.StateOf(r, sel, true), slideId: 0x51a);
                var inner = UiTheme.Inset(r, UiTheme.Px(UiTheme.S5), UiTheme.Px(14f));
                UiTheme.Text(new Rect(inner.x, inner.y, inner.width, UiTheme.Px(24f)), $"{i + 1:00} · {RowName(row).ToUpperInvariant()}", t.caption, sel ? UiTheme.Accent : UiTheme.TextSecondary);
                UiTheme.TextFit(new Rect(inner.x, inner.yMax - UiTheme.Px(30f), inner.width, UiTheme.Px(30f)), OptionName(row, OptionIndex(row)), sel ? t.labelBold : t.label, sel ? UiTheme.TextPrimary : UiTheme.TextBody);
                if (click && r.Contains(e.mousePosition)) { _row = i; _dirty = true; e.Use(); }
            }

            // 3D view
            UiTheme.Panel(a.preview);
            if (_preview != null && _preview.Texture != null)
            {
                GUI.DrawTexture(new Rect(a.preview.x + 1, a.preview.y + 1, a.preview.width - 2, a.preview.height - 2), _preview.Texture, ScaleMode.StretchToFill, false);
                if (e.type == EventType.MouseDrag && e.button == 0 && a.preview.Contains(e.mousePosition))
                {
                    _preview.Orbit(e.delta);
                    e.Use();
                }
            }
            var pv = UiTheme.Inset(a.preview, UiTheme.Px(UiTheme.S5), UiTheme.Px(UiTheme.S4));
            UiTheme.Text(new Rect(pv.x, pv.y, pv.width, UiTheme.Px(26f)), Model.displayName.ToUpperInvariant(), t.caption, UiTheme.TextPrimary);
            UiTheme.Text(new Rect(pv.x, pv.y, pv.width, UiTheme.Px(26f)), "тяните, чтобы повернуть", t.captionRight, UiTheme.TextSecondary);
            var spec = new Rect(a.preview.x + 1f, a.preview.yMax - UiTheme.Px(44f), a.preview.width - 2f, UiTheme.Px(43f));
            UiTheme.Fill(spec, new Color(UiTheme.Surface.r, UiTheme.Surface.g, UiTheme.Surface.b, 0.85f));
            UiTheme.Text(new Rect(pv.x, spec.y, pv.width, spec.height),
                $"диагональ {Model.motorDiagonal * 1000f:0} мм · рама {Model.airframeMass:0.00} кг", t.caption, UiTheme.TextBody);

            // Options of the selected slot: ← → installs the next one, so the installed one is "current".
            UiTheme.Panel(a.options);
            if (_rows.Count > 0)
            {
                var selRow = _rows[_row];
                int count = OptionCount(selRow), current = OptionIndex(selRow);
                var o = UiTheme.Inset(a.options, 0f, UiTheme.Px(UiTheme.S4));
                float ox = o.x + UiTheme.Px(UiTheme.S5), ow = o.width - 2f * UiTheme.Px(UiTheme.S5);
                UiTheme.Text(new Rect(ox, o.y, ow, UiTheme.Px(26f)), $"{RowName(selRow).ToUpperInvariant()} · {count} {Variants(count)}", t.caption, UiTheme.TextSecondary);
                float oh = UiTheme.Px(62f), oy = o.y + UiTheme.Px(36f);
                for (int i = 0; i < count; i++)
                {
                    var r = new Rect(o.x + 1f, oy + i * oh, o.width - 2f, oh);
                    bool sel = i == current;
                    UiTheme.Item(r, UiTheme.StateOf(r, sel, false), slideId: 0x0b7);
                    string delta = i < _optionStats.Count && !sel ? Delta(_optionStats[i].minutes, _current.minutes, " мин", "0") : "";
                    // Name on top, mass and the change it makes below (before/after comparison per option)
                    UiTheme.TextFit(new Rect(ox, r.y + UiTheme.Px(8f), ow, UiTheme.Px(28f)), OptionName(selRow, i), sel ? t.labelBold : t.label, sel ? UiTheme.TextPrimary : UiTheme.TextBody);
                    UiTheme.Text(new Rect(ox, r.y + UiTheme.Px(34f), ow, UiTheme.Px(24f)),
                        $"{OptionTag(selRow, i)}{(delta.Length > 0 ? "   " + delta : sel ? "   " + UiTheme.Colored("установлено", UiTheme.Accent) : "")}", t.caption, UiTheme.TextSecondary);
                    if (click && r.Contains(e.mousePosition)) { SelectOption(selRow, i); e.Use(); }
                }
                float dy = oy + count * oh + UiTheme.Px(UiTheme.S4);
                UiTheme.Fill(new Rect(ox, dy, ow, 1f), UiTheme.Border);
                dy += UiTheme.Px(UiTheme.S4);
                float lh = UiTheme.Px(28f);
                UiTheme.Text(new Rect(ox, dy, ow, lh), "К СТАНДАРТНОЙ СБОРКЕ", t.caption, UiTheme.TextSecondary);
                UiTheme.KeyValue(new Rect(ox, dy + lh, ow, lh), "висение", Change(_stock.minutes, _current.minutes, "0", " мин"), UiTheme.TextBody);
                UiTheme.KeyValue(new Rect(ox, dy + 2f * lh, ow, lh), "тяга / вес", Change(_stock.twr, _current.twr, "0.0", ""), UiTheme.TextBody);
                dy += 3f * lh + UiTheme.Px(UiTheme.S3);
                UiTheme.Text(new Rect(ox, dy, ow, o.yMax - dy), Specs(selRow), t.body, UiTheme.TextBody);
            }

            // Resulting drone
            float g = UiTheme.Px(UiTheme.S3), cw = (a.footer.width - 3f * g) / 4f;
            UiTheme.Stat(new Rect(a.footer.x, a.footer.y, cw, a.footer.height), "ВЗЛЁТНАЯ МАССА", $"{_current.mass:0.00} кг", UiTheme.TextPrimary);
            UiTheme.Stat(new Rect(a.footer.x + (cw + g), a.footer.y, cw, a.footer.height), "ТЯГА / ВЕС", _current.twr < 1f ? "не взлетит" : $"{_current.twr:0.0}",
                _current.twr < 1f ? UiTheme.Danger : _current.twr < 1.3f ? UiTheme.Warning : UiTheme.TextPrimary);
            UiTheme.Stat(new Rect(a.footer.x + 2f * (cw + g), a.footer.y, cw, a.footer.height), "ВИСЕНИЕ", $"~{_current.minutes:0} мин", UiTheme.TextPrimary);
            UiTheme.Stat(new Rect(a.footer.x + 3f * (cw + g), a.footer.y, cw, a.footer.height), "ДАЛЬНОСТЬ ВИДЕО", _current.range, UiTheme.TextPrimary);

                return UiTheme.Prompts(a.prompts, false, prompts);
        }

        static string Variants(int n) => n % 10 == 1 && n % 100 != 11 ? "ВАРИАНТ" : n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 10 || n % 100 >= 20) ? "ВАРИАНТА" : "ВАРИАНТОВ";
    }
}
