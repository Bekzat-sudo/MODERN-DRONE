using System.Collections.Generic;
using System.Text;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Video;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Hangar / loadout screen before each sortie: slots on the left, the drone on a turntable in the middle
    /// (selected part pulled out and outlined), options of the selected slot on the right, resulting drone below.
    /// The last loadout of each drone model is remembered.
    /// </summary>
    public sealed partial class MissionController
    {
        enum LoadoutRow { Drone, Battery, Propellers, Camera, Link, Warhead }

        static readonly (LoadoutRow row, PartSlot slot)[] PartRows =
        {
            (LoadoutRow.Battery, PartSlot.Battery),
            (LoadoutRow.Propellers, PartSlot.Propellers),
            (LoadoutRow.Camera, PartSlot.Camera),
            (LoadoutRow.Link, PartSlot.VideoLink),
        };

        readonly List<LoadoutRow> _loadoutRows = new List<LoadoutRow>();
        readonly List<DronePart> _options = new List<DronePart>();
        readonly DronePart[] _picked = new DronePart[4]; // by PartSlot
        int _droneChoice = -1;
        int _loadoutRow;
        HangarPreview _hangar;
        GUIStyle _rowLabel, _rowValue, _option, _optionSelected, _caption;

        DroneConfig Model => droneModels.Length > 0 ? droneModels[Mathf.Clamp(_droneChoice, 0, droneModels.Length - 1)] : drone.Config;
        WarheadData Warhead => warheads.Length > 0 ? warheads[Mathf.Clamp(_warheadChoice, 0, warheads.Length - 1)] : null;

        /// <summary>
        /// Parts the pilot can fit in a slot now: thermal cores only at night (an analog camera sees nothing there,
        /// and in daylight the stock camera is kept), links only of a kind still in stock.
        /// </summary>
        List<DronePart> Options(PartSlot slot)
        {
            _options.Clear();
            foreach (var p in parts)
            {
                if (p == null || p.slot != slot) continue;
                if (slot == PartSlot.Camera && p.thermal != NightMode.Night) continue;
                if (slot == PartSlot.VideoLink && !FreeFlight && (p.fiber ? _fiberLeft : _radioLeft) <= 0) continue;
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

        /// <summary>Every slot starts from this model's last loadout, else its stock part, else the first allowed one.</summary>
        void PickStockParts()
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

        void SaveLoadout()
        {
            foreach (var (_, slot) in PartRows)
                if (_picked[(int)slot] != null) PlayerPrefs.SetString(PrefKey(slot), _picked[(int)slot].name);
            if (Model != null) PlayerPrefs.SetString("loadout/drone", Model.name);
            if (Warhead != null) PlayerPrefs.SetString("loadout/warhead", Warhead.name);
            PlayerPrefs.Save();
        }

        /// <summary>Picks the next drone: opens the hangar when there is anything to choose.</summary>
        void PrepareSortie()
        {
            if (_droneChoice < 0)
            {
                _droneChoice = Mathf.Max(0, System.Array.FindIndex(droneModels, m => m.name == PlayerPrefs.GetString("loadout/drone", "")));
                _warheadChoice = Mathf.Max(0, System.Array.FindIndex(warheads, x => x.name == PlayerPrefs.GetString("loadout/warhead", "")));
            }
            PickStockParts();
            _loadoutRows.Clear();
            if (droneModels.Length > 1) _loadoutRows.Add(LoadoutRow.Drone);
            foreach (var (row, slot) in PartRows)
                if (Options(slot).Count > 1) _loadoutRows.Add(row);
            if (warheads.Length > 1) _loadoutRows.Add(LoadoutRow.Warhead);
            if (_loadoutRows.Count == 0)
            {
                LaunchSortie();
                return;
            }
            Phase = MissionPhase.Loadout;
            _input.Suspended = true;
            _loadoutRow = 0;
        }

        // ---- rows and their options

        static PartSlot SlotOf(LoadoutRow row)
        {
            foreach (var (r, slot) in PartRows)
                if (r == row) return slot;
            return PartSlot.Battery;
        }

        static string RowName(LoadoutRow row) => row switch
        {
            LoadoutRow.Drone => "Дрон",
            LoadoutRow.Battery => "Батарея",
            LoadoutRow.Propellers => "Винты",
            LoadoutRow.Camera => "Камера",
            LoadoutRow.Link => "Канал",
            _ => "Боевая часть"
        };

        int OptionCount(LoadoutRow row) => row switch
        {
            LoadoutRow.Drone => droneModels.Length,
            LoadoutRow.Warhead => warheads.Length,
            _ => Options(SlotOf(row)).Count
        };

        int OptionIndex(LoadoutRow row) => row switch
        {
            LoadoutRow.Drone => _droneChoice,
            LoadoutRow.Warhead => _warheadChoice,
            _ => Options(SlotOf(row)).IndexOf(_picked[(int)SlotOf(row)])
        };

        void SelectOption(LoadoutRow row, int i)
        {
            int n = OptionCount(row);
            if (n == 0) return;
            i = (i % n + n) % n;
            switch (row)
            {
                case LoadoutRow.Drone: _droneChoice = i; PickStockParts(); break;
                case LoadoutRow.Warhead: _warheadChoice = i; break;
                default: _picked[(int)SlotOf(row)] = Options(SlotOf(row))[i]; break;
            }
        }

        string OptionName(LoadoutRow row, int i) => row switch
        {
            LoadoutRow.Drone => droneModels[i].displayName,
            LoadoutRow.Warhead => warheads[i].displayName,
            _ => Options(SlotOf(row))[i].displayName
        };

        /// <summary>Short figure shown next to an option in the list.</summary>
        string OptionTag(LoadoutRow row, int i)
        {
            if (row == LoadoutRow.Drone) return $"{droneModels[i].frameMass:0.0} кг";
            if (row == LoadoutRow.Warhead) return $"{warheads[i].mass:0.0} кг";
            var p = Options(SlotOf(row))[i];
            return p.slot == PartSlot.Propellers ? $"тяга ×{p.thrustScale:0.00}" : $"{p.mass * 1000f:0} г";
        }

        string OptionDetails(LoadoutRow row)
        {
            if (row == LoadoutRow.Drone) return Model.summary;
            if (row == LoadoutRow.Warhead) return $"{Warhead.mass:0.0} кг · {WarheadHint(Warhead)}";
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
            return specs + "\n" + p.summary + LinkStock(p);
        }

        string LinkStock(DronePart p) =>
            p.slot != PartSlot.VideoLink || FreeFlight ? "" : $"\nосталось дронов: {(p.fiber ? _fiberLeft : _radioLeft)}";

        // ---- input

        Rect PreviewRect => new Rect(Screen.width * 0.27f, Screen.height * 0.1f, Screen.width * 0.44f, Screen.height * 0.66f);

        void UpdateLoadout()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool left = (kb != null && kb.leftArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.left.wasPressedThisFrame);
            bool right = (kb != null && kb.rightArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.right.wasPressedThisFrame);
            bool up = (kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
            bool down = (kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame);
            int n = _loadoutRows.Count;
            if (up) _loadoutRow = (_loadoutRow + n - 1) % n;
            if (down) _loadoutRow = (_loadoutRow + 1) % n;
            if (left || right)
            {
                var row = _loadoutRows[_loadoutRow];
                SelectOption(row, OptionIndex(row) + (right ? 1 : -1));
            }

            if (_hangar == null) _hangar = GetComponent<HangarPreview>();
            if (_hangar != null)
            {
                var r = PreviewRect;
                var sel = _loadoutRows[_loadoutRow];
                var highlight = sel == LoadoutRow.Drone || sel == LoadoutRow.Warhead ? null : _picked[(int)SlotOf(sel)];
                _hangar.Show(Model, _picked[(int)PartSlot.Propellers], highlight, new Vector2Int((int)r.width, (int)r.height));
            }
            if (ConfirmPressed()) LaunchSortie();
        }

        // ---- drawing

        void DrawLoadout(float w, float h)
        {
            EnsureLoadoutStyles();
            Fill(new Rect(0, 0, w, h), new Color(0.03f, 0.035f, 0.04f, 1f));
            GUI.Label(new Rect(0, h * 0.02f, w, h * 0.06f), "АНГАР · СНАРЯЖЕНИЕ", _title);

            var e = Event.current;
            var preview = PreviewRect;
            if (_hangar != null && _hangar.Texture != null)
            {
                GUI.DrawTexture(preview, _hangar.Texture, ScaleMode.StretchToFill, false);
                if (e.type == EventType.MouseDrag && e.button == 0 && preview.Contains(e.mousePosition))
                {
                    _hangar.Orbit(e.delta);
                    e.Use();
                }
            }

            // Slots
            float lh = h * 0.075f;
            var slots = new Rect(w * 0.03f, h * 0.1f, w * 0.22f, lh * _loadoutRows.Count);
            for (int i = 0; i < _loadoutRows.Count; i++)
            {
                var row = _loadoutRows[i];
                var r = new Rect(slots.x, slots.y + i * lh, slots.width, lh * 0.92f);
                bool sel = i == _loadoutRow;
                Fill(r, sel ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.03f));
                if (sel) Fill(new Rect(r.x, r.y, 4f, r.height), Accent);
                GUI.Label(new Rect(r.x + 14f, r.y + r.height * 0.08f, r.width - 20f, r.height * 0.4f), RowName(row).ToUpperInvariant(), _caption);
                GUI.Label(new Rect(r.x + 14f, r.y + r.height * 0.42f, r.width - 20f, r.height * 0.5f), OptionName(row, OptionIndex(row)), _rowValue);
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    _loadoutRow = i;
                    e.Use();
                }
            }

            // Options of the selected slot
            var selRow = _loadoutRows[_loadoutRow];
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
                GUI.Label(new Rect(r.x, r.y, r.width - 8f, r.height), OptionTag(selRow, i), _rowLabel);
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
            Stat(new Rect(bar.x, bar.y, bar.width / 4f, bar.height), "ВЗЛЁТНАЯ МАССА", $"{s.mass:0.00} кг", 0f);
            Stat(new Rect(bar.x + bar.width / 4f, bar.y, bar.width / 4f, bar.height), "ТЯГА / ВЕС", $"{s.twr:0.0}", Mathf.InverseLerp(1.5f, 7f, s.twr));
            Stat(new Rect(bar.x + bar.width / 2f, bar.y, bar.width / 4f, bar.height), "ВИСЕНИЕ", $"~{s.minutes:0} мин", Mathf.InverseLerp(0f, 40f, s.minutes));
            Stat(new Rect(bar.x + bar.width * 0.75f, bar.y, bar.width / 4f, bar.height), "ДАЛЬНОСТЬ ВИДЕО", s.range, 0f);

            GUI.Label(new Rect(0, h * 0.92f, w, h * 0.05f),
                "↑↓ — слот,  ← → — вариант,  мышь: клик по строке, тянуть — повернуть дрон,  SPACE / A — взлёт", _small);
        }

        void Stat(Rect r, string label, string value, float fill)
        {
            GUI.Label(new Rect(r.x + 16f, r.y + r.height * 0.1f, r.width, r.height * 0.35f), label, _caption);
            GUI.Label(new Rect(r.x + 16f, r.y + r.height * 0.42f, r.width, r.height * 0.45f), value, _rowValue);
            if (fill > 0f)
            {
                var b = new Rect(r.x + r.width * 0.45f, r.y + r.height * 0.62f, r.width * 0.45f, 4f);
                Fill(b, new Color(1f, 1f, 1f, 0.12f));
                Fill(new Rect(b.x, b.y, b.width * Mathf.Clamp01(fill), b.height), Accent);
            }
        }

        struct LoadoutSummary
        {
            public float mass, twr, minutes;
            public string range;
        }

        /// <summary>The drone this loadout makes: take-off mass, thrust-to-weight, hover endurance, video reach.</summary>
        LoadoutSummary Summary()
        {
            var model = Model;
            if (model == null) return new LoadoutSummary { range = "—" };
            var cfg = model.stockParts.Length > 0 ? DroneBuild.Compose(model, _picked) : model;
            var link = _picked[(int)PartSlot.VideoLink];
            float payload = (Warhead != null ? Warhead.mass : 0f) + (link != null && link.fiber ? link.mass : 0f);
            var s = new LoadoutSummary
            {
                mass = cfg.frameMass + payload,
                minutes = DroneBuild.HoverMinutes(cfg, payload),
                range = link == null ? "—" : link.fiber ? $"{link.spoolLength / 1000f:0} км кабель" : $"{RadioParams.Default.videoRangeEnd * link.videoRangeScale / 1000f:0.0} км"
            };
            s.twr = cfg.frameMass * cfg.thrustToWeight / s.mass;
            if (cfg != model) Destroy(cfg);
            return s;
        }

        static readonly Color Accent = new Color(0.95f, 0.62f, 0.15f);

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void EnsureLoadoutStyles()
        {
            if (_rowValue != null && _rowValue.fontSize == _body.fontSize) return;
            int size = _body.fontSize;
            _caption = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 0.7f), fontStyle = FontStyle.Bold, wordWrap = false };
            _caption.normal.textColor = new Color(0.6f, 0.65f, 0.62f);
            _rowValue = new GUIStyle(_body) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            _option = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 0.85f), wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            _option.normal.textColor = new Color(0.82f, 0.84f, 0.82f);
            _optionSelected = new GUIStyle(_option) { fontStyle = FontStyle.Bold };
            _optionSelected.normal.textColor = Color.white;
            _rowLabel = new GUIStyle(_option) { alignment = TextAnchor.MiddleRight };
            _rowLabel.normal.textColor = Accent;
        }
    }
}
