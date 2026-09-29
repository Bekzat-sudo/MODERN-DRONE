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
    /// Drone customisation screen, shared by the main menu and the pre-sortie loadout: slots on the left, the drone on
    /// a turntable in the middle (selected part pulled out and outlined), options of the selected slot on the right,
    /// resulting drone below. The last build of each drone model is remembered (PlayerPrefs) and used by every mission.
    /// </summary>
    public sealed class Hangar
    {
        enum Row { Drone, Battery, Propellers, Camera, Link, Warhead }

        static readonly (Row row, PartSlot slot)[] PartRows =
        {
            (Row.Battery, PartSlot.Battery),
            (Row.Propellers, PartSlot.Propellers),
            (Row.Camera, PartSlot.Camera),
            (Row.Link, PartSlot.VideoLink),
        };

        static readonly Color Accent = new Color(0.95f, 0.62f, 0.15f);

        readonly DroneConfig[] _models;
        readonly DronePart[] _parts;
        readonly WarheadData[] _warheads;
        readonly HangarPreview _preview;
        readonly bool _mission;
        readonly Func<bool, int> _linkStock;
        readonly List<Row> _rows = new List<Row>();
        readonly List<DronePart> _options = new List<DronePart>();
        readonly DronePart[] _picked = new DronePart[4]; // by PartSlot
        int _model = -1, _warhead, _row;
        GUIStyle _title, _body, _caption, _value, _option, _optionSelected, _tag, _hint;

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
        public bool HasChoices => _rows.Count > 0;

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
            if (_models.Length > 1) _rows.Add(Row.Drone);
            foreach (var (row, slot) in PartRows)
                if (Options(slot).Count > 1) _rows.Add(row);
            if (_warheads.Length > 1) _rows.Add(Row.Warhead);
            _row = 0;
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
            Row.Drone => "Дрон",
            Row.Battery => "Батарея",
            Row.Propellers => "Винты",
            Row.Camera => "Камера",
            Row.Link => "Канал",
            _ => "Боевая часть"
        };

        int OptionCount(Row row) => row switch
        {
            Row.Drone => _models.Length,
            Row.Warhead => _warheads.Length,
            _ => Options(SlotOf(row)).Count
        };

        int OptionIndex(Row row) => row switch
        {
            Row.Drone => _model,
            Row.Warhead => _warhead,
            _ => Options(SlotOf(row)).IndexOf(_picked[(int)SlotOf(row)])
        };

        void SelectOption(Row row, int i)
        {
            int n = OptionCount(row);
            if (n == 0) return;
            i = (i % n + n) % n;
            switch (row)
            {
                case Row.Drone: _model = i; PickSaved(); break;
                case Row.Warhead: _warhead = i; break;
                default: _picked[(int)SlotOf(row)] = Options(SlotOf(row))[i]; break;
            }
        }

        string OptionName(Row row, int i) => row switch
        {
            Row.Drone => _models[i].displayName,
            Row.Warhead => _warheads[i].displayName,
            _ => Options(SlotOf(row))[i].displayName
        };

        /// <summary>Short figure shown next to an option in the list.</summary>
        string OptionTag(Row row, int i)
        {
            if (row == Row.Drone) return $"{_models[i].frameMass:0.0} кг";
            if (row == Row.Warhead) return $"{_warheads[i].mass:0.0} кг";
            var p = Options(SlotOf(row))[i];
            return p.slot == PartSlot.Propellers ? $"тяга ×{p.thrustScale:0.00}" : $"{p.mass * 1000f:0} г";
        }

        string OptionDetails(Row row)
        {
            if (row == Row.Drone) return Model.summary;
            if (row == Row.Warhead) return $"{Warhead.mass:0.0} кг · {WarheadHint(Warhead)}";
            var p = _picked[(int)SlotOf(row)];
            if (p == null) return "";
            string specs = p.slot switch
            {
                PartSlot.Battery => $"{p.cells}S · {p.capacityMah:0} мА·ч · {p.internalResistance * 1000f:0} мОм · {p.mass:0.00} кг",
                PartSlot.Camera => p.thermal ? $"{p.thermalResolution.x}×{p.thermalResolution.y} · поле {p.horizontalFov:0}° · {p.mass * 1000f:0} г" : $"{p.mass * 1000f:0} г",
                PartSlot.Propellers => $"тяга ×{p.thrustScale:0.00} · ток ×{p.currentScale:0.00} · шаг ×{p.pitchSpeedScale:0.00}",
                _ => p.fiber ? $"кабель {p.spoolLength / 1000f:0} км · {p.mass:0.00} кг"
                             : $"видео до {RadioParams.Default.videoRangeEnd * p.videoRangeScale / 1000f:0.0} км · листва ×{p.foliageBlock:0.00} · {p.mass * 1000f:0} г"
            };
            string note = p.slot == PartSlot.Camera && p.thermal && !_mission ? "\nставится в ночных задачах" : "";
            string stock = p.slot == PartSlot.VideoLink && _linkStock != null ? $"\nосталось дронов: {_linkStock(p.fiber)}" : "";
            return specs + "\n" + p.summary + note + stock;
        }

        public static string WarheadHint(WarheadData w) => w.kind == WarheadKind.Fragmentation
            ? "против открытых целей: РЛС, орудия, грузовики"
            : w.tandem ? "пробивает динамическую защиту" : "против брони: борт, корма, крыша";

        // ---- input

        static Rect PreviewRect => new Rect(Screen.width * 0.27f, Screen.height * 0.1f, Screen.width * 0.44f, Screen.height * 0.66f);

        /// <summary>Keyboard / gamepad navigation and the turntable (call from Update).</summary>
        public void Update()
        {
            if (_rows.Count == 0) return;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            int n = _rows.Count;
            if (up) _row = (_row + n - 1) % n;
            if (down) _row = (_row + 1) % n;
            if (left || right) SelectOption(_rows[_row], OptionIndex(_rows[_row]) + (right ? 1 : -1));

            if (_preview != null)
            {
                var r = PreviewRect;
                var sel = _rows[_row];
                var highlight = sel == Row.Drone || sel == Row.Warhead ? null : _picked[(int)SlotOf(sel)];
                _preview.Show(Model, _picked[(int)PartSlot.Propellers], highlight, new Vector2Int((int)r.width, (int)r.height));
            }
        }

        // ---- drawing

        /// <summary>Full-screen hangar (call from OnGUI).</summary>
        public void Draw(string title, string hint)
        {
            EnsureStyles();
            float w = Screen.width, h = Screen.height;
            Fill(new Rect(0, 0, w, h), new Color(0.03f, 0.035f, 0.04f, 1f));
            GUI.Label(new Rect(0, h * 0.02f, w, h * 0.06f), title, _title);
            if (_rows.Count == 0) return;

            var e = Event.current;
            var preview = PreviewRect;
            if (_preview != null && _preview.Texture != null)
            {
                GUI.DrawTexture(preview, _preview.Texture, ScaleMode.StretchToFill, false);
                if (e.type == EventType.MouseDrag && e.button == 0 && preview.Contains(e.mousePosition))
                {
                    _preview.Orbit(e.delta);
                    e.Use();
                }
            }

            // Slots
            float lh = h * 0.075f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = new Rect(w * 0.03f, h * 0.1f + i * lh, w * 0.22f, lh * 0.92f);
                bool sel = i == _row;
                Fill(r, sel ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.03f));
                if (sel) Fill(new Rect(r.x, r.y, 4f, r.height), Accent);
                GUI.Label(new Rect(r.x + 14f, r.y + r.height * 0.08f, r.width - 20f, r.height * 0.4f), RowName(row).ToUpperInvariant(), _caption);
                GUI.Label(new Rect(r.x + 14f, r.y + r.height * 0.42f, r.width - 20f, r.height * 0.5f), OptionName(row, OptionIndex(row)), _value);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    _row = i;
                    e.Use();
                }
            }

            // Options of the selected slot
            var selRow = _rows[_row];
            var list = new Rect(w * 0.73f, h * 0.1f, w * 0.24f, h * 0.66f);
            Fill(list, new Color(1f, 1f, 1f, 0.03f));
            GUI.Label(new Rect(list.x + 12f, list.y + 6f, list.width, lh * 0.5f), RowName(selRow).ToUpperInvariant(), _caption);
            float oh = h * 0.045f;
            int count = OptionCount(selRow), current = OptionIndex(selRow);
            for (int i = 0; i < count; i++)
            {
                var r = new Rect(list.x + 6f, list.y + lh * 0.55f + i * oh, list.width - 12f, oh * 0.92f);
                bool sel = i == current;
                if (sel) Fill(r, new Color(Accent.r, Accent.g, Accent.b, 0.22f));
                GUI.Label(new Rect(r.x + 8f, r.y, r.width * 0.72f, r.height), OptionName(selRow, i), sel ? _optionSelected : _option);
                GUI.Label(new Rect(r.x, r.y, r.width - 8f, r.height), OptionTag(selRow, i), _tag);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    SelectOption(selRow, i);
                    e.Use();
                }
            }
            float detailsTop = list.y + lh * 0.55f + count * oh + h * 0.02f;
            GUI.Label(new Rect(list.x + 12f, detailsTop, list.width - 24f, list.yMax - detailsTop), OptionDetails(selRow), _body);

            // Resulting drone
            var bar = new Rect(w * 0.03f, h * 0.8f, w * 0.94f, h * 0.1f);
            Fill(bar, new Color(1f, 1f, 1f, 0.04f));
            var s = Summary();
            float q = bar.width / 4f;
            Stat(new Rect(bar.x, bar.y, q, bar.height), "ВЗЛЁТНАЯ МАССА", $"{s.mass:0.00} кг", 0f);
            Stat(new Rect(bar.x + q, bar.y, q, bar.height), "ТЯГА / ВЕС", $"{s.twr:0.0}", Mathf.InverseLerp(1.5f, 7f, s.twr));
            Stat(new Rect(bar.x + 2 * q, bar.y, q, bar.height), "ВИСЕНИЕ", $"~{s.minutes:0} мин", Mathf.InverseLerp(0f, 40f, s.minutes));
            Stat(new Rect(bar.x + 3 * q, bar.y, q, bar.height), "ДАЛЬНОСТЬ ВИДЕО", s.range, 0f);

            GUI.Label(new Rect(0, h * 0.92f, w, h * 0.05f), hint, _hint);
        }

        void Stat(Rect r, string label, string value, float fill)
        {
            GUI.Label(new Rect(r.x + 16f, r.y + r.height * 0.1f, r.width, r.height * 0.35f), label, _caption);
            GUI.Label(new Rect(r.x + 16f, r.y + r.height * 0.42f, r.width, r.height * 0.45f), value, _value);
            if (fill <= 0f) return;
            var b = new Rect(r.x + r.width * 0.45f, r.y + r.height * 0.62f, r.width * 0.45f, 4f);
            Fill(b, new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(b.x, b.y, b.width * Mathf.Clamp01(fill), b.height), Accent);
        }

        struct BuildSummary
        {
            public float mass, twr, minutes;
            public string range;
        }

        /// <summary>The drone this build makes: take-off mass, thrust-to-weight, hover endurance, video reach.</summary>
        BuildSummary Summary()
        {
            var model = Model;
            if (model == null) return new BuildSummary { range = "—" };
            var cfg = Compose();
            var link = _picked[(int)PartSlot.VideoLink];
            float payload = (Warhead != null ? Warhead.mass : 0f) + (link != null && link.fiber ? link.mass : 0f);
            var s = new BuildSummary
            {
                mass = cfg.frameMass + payload,
                minutes = DroneBuild.HoverMinutes(cfg, payload),
                range = link == null ? "—" : link.fiber ? $"{link.spoolLength / 1000f:0} км кабель" : $"{RadioParams.Default.videoRangeEnd * link.videoRangeScale / 1000f:0.0} км"
            };
            s.twr = cfg.frameMass * cfg.thrustToWeight / s.mass;
            if (cfg != model) UnityEngine.Object.Destroy(cfg);
            return s;
        }

        /// <summary>Runtime config of the current build (models without stock parts fly as defined).</summary>
        public DroneConfig Compose() => Model.stockParts.Length > 0 ? DroneBuild.Compose(Model, _picked) : Model;

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void EnsureStyles()
        {
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.024f));
            if (_body != null && _body.fontSize == size) return;
            _body = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true };
            _body.normal.textColor = Color.white;
            _title = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 1.6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _hint = new GUIStyle(_body) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _caption = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 0.7f), fontStyle = FontStyle.Bold, wordWrap = false };
            _caption.normal.textColor = new Color(0.6f, 0.65f, 0.62f);
            _value = new GUIStyle(_body) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            _option = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 0.85f), wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            _option.normal.textColor = new Color(0.82f, 0.84f, 0.82f);
            _optionSelected = new GUIStyle(_option) { fontStyle = FontStyle.Bold };
            _optionSelected.normal.textColor = Color.white;
            _tag = new GUIStyle(_option) { alignment = TextAnchor.MiddleRight };
            _tag.normal.textColor = Accent;
        }
    }
}
