using System;
using System.Collections.Generic;
using ModernDrone.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.UI
{
    /// <summary>One line of a menu: label, optional value, left/right adjust, activate.</summary>
    public class MenuRow
    {
        public string Label;
        public Func<string> Value;
        public Action<int> Adjust;
        public Action Activate;
        public bool IsHeader;
        /// <summary>Editable text: typed while the row is in edit mode (Enter / click to start, Enter / Esc to finish).</summary>
        public Action<char> Type;
        public Action Erase;
        /// <summary>Explanation shown under the list while the row has focus.</summary>
        public string Note;
        /// <summary>Slider position in [0, 1], drawn as a track next to the value.</summary>
        public Func<float> Fraction;
        /// <summary>On / off state of a toggle, drawn as a switch.</summary>
        public Func<bool> On;
        /// <summary>The value is a key or button name, drawn as a key cap.</summary>
        public bool IsBinding;
        /// <summary>Resets, discards or leaves something: the label turns red while focused.</summary>
        public bool Danger;
        /// <summary>Null = always available; a disabled row is drawn dimmed and does nothing.</summary>
        public Func<bool> Enabled;

        public MenuRow(string label) => Label = label;

        /// <summary>The row whose text is being typed; menus skip their navigation while it is set.</summary>
        public static MenuRow Editing;
        static int _editingSince;

        public static int EditingSince => _editingSince;

        /// <summary>
        /// Text field limited to <paramref name="maxLength"/> characters that <paramref name="accept"/> maps to a
        /// stored character (or '\0' to reject).
        /// </summary>
        public static MenuRow Text(string label, Func<string> get, Action<string> set, int maxLength, Func<char, char> accept)
        {
            var row = new MenuRow(label);
            row.Value = () => Editing == row ? get() + (Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f ? "_" : " ") : get();
            row.Activate = () => { Editing = Editing == row ? null : row; _editingSince = Time.frameCount; };
            row.Type = ch =>
            {
                char c = accept(ch);
                if (c != '\0' && get().Length < maxLength) set(get() + c);
            };
            row.Erase = () => { var v = get(); if (v.Length > 0) set(v.Substring(0, v.Length - 1)); };
            return row;
        }

        public static MenuRow Button(string label, Action activate, Func<string> value = null) =>
            new MenuRow(label) { Activate = activate, Value = value };

        public static MenuRow Info(string label, Func<string> value) => new MenuRow(label) { Value = value };

        public static MenuRow Header(string label) => new MenuRow(label) { IsHeader = true };

        public static MenuRow Toggle(string label, Func<bool> get, Action<bool> set) => new MenuRow(label)
        {
            Value = () => get() ? "ВКЛ" : "ВЫКЛ",
            On = get,
            Adjust = _ => set(!get()),
            Activate = () => set(!get())
        };

        public static MenuRow Slider(string label, Func<float> get, Action<float> set, float min, float max, float step, string format = "0.00") =>
            new MenuRow(label)
            {
                Value = () => get().ToString(format, System.Globalization.CultureInfo.InvariantCulture),
                Fraction = () => Mathf.InverseLerp(min, max, get()),
                Adjust = dir => set(Mathf.Clamp(Mathf.Round((get() + dir * step) / step) * step, min, max))
            };

        public static MenuRow Choice(string label, string[] names, Func<int> get, Action<int> set) => new MenuRow(label)
        {
            Value = () => names[Mathf.Clamp(get(), 0, names.Length - 1)],
            Adjust = dir => set((get() + dir + names.Length) % names.Length),
            Activate = () => set((get() + 1) % names.Length)
        };

        public bool Selectable => !IsHeader && (Adjust != null || Activate != null);

        public bool IsEnabled => Enabled == null || Enabled();

        /// <summary>Enter / A / click: activate, or step the value forward.</summary>
        public void Press()
        {
            if (!IsEnabled) return;
            (Activate ?? (() => Adjust?.Invoke(1)))();
        }
    }

    public struct MenuNav
    {
        public bool up, down, left, right, confirm, back, tabPrev, tabNext;
    }

    /// <summary>
    /// IMGUI menu toolkit on top of <see cref="UiTheme"/>: keyboard / gamepad / mouse navigation and row lists whose
    /// values are drawn by kind (choice, slider track, switch, key cap, read-only info).
    /// </summary>
    public sealed class MenuGui
    {
        const float RepeatDelay = 0.35f;
        const float RepeatRate = 0.09f;

        public enum RowSize
        {
            /// <summary>Settings: mono label, value on the right.</summary>
            Normal,
            /// <summary>Main / pause menu: Oswald label, a note on the right.</summary>
            Large
        }

        // Held-direction auto-repeat for sticks / d-pad / keys.
        Vector2Int _heldDir;
        float _nextRepeat;

        public static float RowHeight(RowSize size = RowSize.Normal) => UiTheme.Px(size == RowSize.Large ? 64f : 46f);

        /// <summary>Polls keyboard and gamepad for menu navigation (call once per frame, from Update).</summary>
        public MenuNav ReadNav()
        {
            var nav = new MenuNav();
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            Vector2Int dir = Vector2Int.zero;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed) dir.y = 1;
                if (kb.downArrowKey.isPressed) dir.y = -1;
                if (kb.leftArrowKey.isPressed) dir.x = -1;
                if (kb.rightArrowKey.isPressed) dir.x = 1;
                nav.confirm |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                nav.back |= kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
                nav.tabPrev |= kb.qKey.wasPressedThisFrame;
                nav.tabNext |= kb.eKey.wasPressedThisFrame;
            }
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                Vector2 dpad = gp.dpad.ReadValue();
                Vector2 v = dpad.sqrMagnitude > 0.1f ? dpad : stick;
                if (v.y > 0.5f) dir.y = 1;
                if (v.y < -0.5f) dir.y = -1;
                if (v.x < -0.5f) dir.x = -1;
                if (v.x > 0.5f) dir.x = 1;
                nav.confirm |= gp.buttonSouth.wasPressedThisFrame;
                nav.back |= gp.buttonEast.wasPressedThisFrame;
                nav.tabPrev |= gp.leftShoulder.wasPressedThisFrame;
                nav.tabNext |= gp.rightShoulder.wasPressedThisFrame;
            }

            // Edge on change, then auto-repeat while held. Uses unscaled time so it works while paused.
            bool fire = false;
            if (dir != _heldDir)
            {
                _heldDir = dir;
                fire = dir != Vector2Int.zero;
                _nextRepeat = Time.unscaledTime + RepeatDelay;
            }
            else if (dir != Vector2Int.zero && Time.unscaledTime >= _nextRepeat)
            {
                fire = true;
                _nextRepeat = Time.unscaledTime + RepeatRate;
            }
            if (fire)
            {
                nav.up = dir.y > 0;
                nav.down = dir.y < 0;
                nav.left = dir.x < 0;
                nav.right = dir.x > 0;
            }
            return nav;
        }

        /// <summary>Applies navigation to a row list (selection and adjust/activate). Call from Update.</summary>
        public static void Navigate(List<MenuRow> rows, ref int selected, in MenuNav nav)
        {
            if (rows.Count == 0) return;
            selected = ClampToSelectable(rows, selected, 1);
            if (nav.up) selected = Step(rows, selected, -1);
            if (nav.down) selected = Step(rows, selected, 1);
            var r = rows[selected];
            if (r.IsEnabled)
            {
                if (nav.left) r.Adjust?.Invoke(-1);
                if (nav.right) r.Adjust?.Invoke(1);
            }
            if (nav.confirm) r.Press();
        }

        static int Step(List<MenuRow> rows, int from, int dir)
        {
            for (int i = 1; i <= rows.Count; i++)
            {
                int idx = (from + dir * i + rows.Count * 2) % rows.Count;
                if (rows[idx].Selectable) return idx;
            }
            return from;
        }

        static int ClampToSelectable(List<MenuRow> rows, int idx, int dir)
        {
            idx = Mathf.Clamp(idx, 0, rows.Count - 1);
            return rows[idx].Selectable ? idx : Step(rows, idx, dir);
        }

        /// <summary>Note of the focused row, or null.</summary>
        public static string FocusedNote(List<MenuRow> rows, int selected) =>
            selected >= 0 && selected < rows.Count ? rows[selected].Note : null;

        /// <summary>
        /// Draws rows (scrolling to keep the selection visible, with a scroll bar when they do not fit) and handles
        /// mouse input. <paramref name="focused"/> = false draws the selection as "current" (focus is elsewhere on the
        /// screen). Call from OnGUI.
        /// </summary>
        public void DrawRows(Rect area, List<MenuRow> rows, ref int selected, RowSize size = RowSize.Normal, bool focused = true)
        {
            var t = UiTheme.T;
            float h = RowHeight(size);
            int visible = Mathf.Max(1, Mathf.FloorToInt(area.height / h));
            int first = Mathf.Clamp(selected - visible / 2, 0, Mathf.Max(0, rows.Count - visible));
            var e = Event.current;
            int slide = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(rows);
            // Mouse wheel scrolls the selection.
            if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition) && rows.Count > 0)
            {
                selected = Step(rows, ClampToSelectable(rows, selected, 1), e.delta.y > 0f ? 1 : -1);
                e.Use();
            }

            // Typing into a text row: characters, Backspace; Enter / Esc finish.
            if (MenuRow.Editing != null && e.type == EventType.KeyDown)
            {
                var ed = MenuRow.Editing;
                // The Enter that opened the field arrives in the same frame: do not let it close it again.
                bool finish = e.keyCode == KeyCode.Escape || ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Time.frameCount != MenuRow.EditingSince);
                if (finish) MenuRow.Editing = null;
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { }
                else if (e.keyCode == KeyCode.Backspace) ed.Erase?.Invoke();
                else if (e.character != '\0' && !char.IsControl(e.character)) ed.Type?.Invoke(e.character);
                e.Use();
            }

            int last = Mathf.Min(rows.Count, first + visible);
            for (int i = first; i < last; i++)
            {
                var r = rows[i];
                var rect = new Rect(area.x, area.y + (i - first) * h, area.width, h);
                if (r.IsHeader)
                {
                    if (!string.IsNullOrEmpty(r.Label))
                        UiTheme.Text(new Rect(rect.x + UiTheme.Px(UiTheme.S5), rect.y + h * 0.35f, rect.width, h * 0.65f), r.Label.ToUpperInvariant(), t.caption, UiTheme.TextSecondary);
                    continue;
                }

                bool sel = i == selected && r.Selectable;
                var state = r.Selectable ? UiTheme.StateOf(rect, sel, focused, r.IsEnabled) : UiState.Normal;
                if (!r.IsEnabled) state = UiState.Disabled;
                UiTheme.Item(rect, state, slideId: sel ? slide : 0);
                DrawRow(rect, r, state, size);

                // Mouse: hover selects, click on the value side adjusts (left/right half), elsewhere activates.
                if (!r.Selectable) continue;
                if (e.type == EventType.MouseMove && rect.Contains(e.mousePosition)) selected = i;
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    selected = i;
                    var valueSide = new Rect(rect.center.x, rect.y, rect.width * 0.5f, rect.height);
                    if (r.Adjust != null && r.IsEnabled && valueSide.Contains(e.mousePosition))
                        r.Adjust(e.mousePosition.x < valueSide.center.x ? -1 : 1);
                    else
                        r.Press();
                    e.Use();
                }
            }
            UiTheme.ScrollBar(new Rect(area.xMax + UiTheme.Px(UiTheme.S3), area.y, UiTheme.Hair(3f), visible * h), first, visible, rows.Count);
        }

        static void DrawRow(Rect rect, MenuRow r, UiState state, RowSize size)
        {
            var t = UiTheme.T;
            bool focus = state == UiState.Focus;
            float pad = UiTheme.Px(UiTheme.S5);
            // Leading spaces in a label mark a sub-item: indent instead of drawing them.
            string label = r.Label.TrimStart(' ');
            float indent = (r.Label.Length - label.Length) / 2 * UiTheme.Px(UiTheme.S5);
            var inner = new Rect(rect.x + pad + indent, rect.y, rect.width - pad * 2f - indent, rect.height);
            string v = r.Value?.Invoke();

            if (size == RowSize.Large)
            {
                Color lc = r.Danger ? (focus ? UiTheme.Danger : UiTheme.TextSecondary) : UiTheme.LabelColor(state);
                UiTheme.TextFit(new Rect(inner.x, inner.y, inner.width * 0.6f, inner.height), label, t.heading, lc);
                if (!string.IsNullOrEmpty(v))
                    UiTheme.Text(inner, v, t.captionRight, focus ? UiTheme.TextBody : UiTheme.TextSecondary);
                return;
            }

            // Value on the right, drawn by kind; the label takes the rest of the line.
            float valueW = DrawValue(inner, r, state, v);
            var labelRect = new Rect(inner.x, inner.y, Mathf.Max(0f, inner.width - valueW - UiTheme.Px(UiTheme.S5)), inner.height);
            Color labelColor = r.Danger && focus ? UiTheme.Danger : r.Selectable ? UiTheme.LabelColor(state) : UiTheme.TextSecondary;
            UiTheme.TextFit(labelRect, label, focus ? t.labelBold : t.label, labelColor);
        }

        /// <summary>Draws the value right-aligned in <paramref name="r"/> and returns the width it used.</summary>
        static float DrawValue(Rect r, MenuRow row, UiState state, string v)
        {
            var t = UiTheme.T;
            bool focus = state == UiState.Focus, disabled = state == UiState.Disabled;
            float x = r.xMax;

            if (row.On != null)
            {
                // Switch: track with a knob, the word beside it.
                bool on = row.On();
                float sw = UiTheme.Px(44f), sh = UiTheme.Px(22f), b = UiTheme.Hair(2f);
                var track = new Rect(x - sw, r.center.y - sh * 0.5f, sw, sh);
                Color c = disabled ? UiTheme.TextDisabled : on ? UiTheme.Accent : UiTheme.TextSecondary;
                if (on) UiTheme.Fill(track, c); else UiTheme.Frame(track, c, b);
                float k = sh - 4f * b;
                UiTheme.Fill(new Rect(on ? track.xMax - 2f * b - k : track.x + 2f * b, track.y + 2f * b, k, k), on ? UiTheme.OnAccent : c);
                float ww = UiTheme.Px(70f);
                UiTheme.Text(new Rect(track.x - UiTheme.Px(UiTheme.S3) - ww, r.y, ww, r.height), v, t.labelRight, on ? UiTheme.TextPrimary : UiTheme.TextSecondary);
                return sw + UiTheme.Px(UiTheme.S3) + ww;
            }

            if (v == null)
            {
                // Plain action: a chevron says "opens / does something".
                if (row.Activate == null) return 0f;
                UiTheme.Text(r, "›", t.labelRight, focus ? UiTheme.Accent : UiTheme.TextSecondary);
                return UiTheme.Px(20f);
            }

            if (row.IsBinding)
            {
                float kw = UiTheme.KeyWidth(v), kh = UiTheme.Px(30f);
                var cap = new Rect(x - kw, r.center.y - kh * 0.5f, kw, kh);
                if (v == "—") UiTheme.Text(cap, v, t.captionCenter, UiTheme.TextSecondary);
                else UiTheme.KeyCap(cap, v, focus ? UiTheme.Accent : UiTheme.TextPrimary, UiTheme.OnAccent);
                return kw;
            }

            Color vc = disabled ? UiTheme.TextDisabled : !row.Selectable ? UiTheme.TextSecondary : focus ? UiTheme.Accent : UiTheme.TextPrimary;
            float used = 0f;
            if (row.Adjust != null && focus)
            {
                // Chevrons only on the focused row: they say "← → changes this".
                float cw = UiTheme.Px(22f);
                UiTheme.Text(new Rect(x - cw, r.y, cw, r.height), "›", t.labelRight, UiTheme.Accent);
                x -= cw + UiTheme.Px(UiTheme.S2);
                used += cw + UiTheme.Px(UiTheme.S2);
            }
            float vw = Mathf.Min(UiTheme.Width(v, t.labelRight), r.width * 0.5f);
            UiTheme.TextFit(new Rect(x - vw, r.y, vw, r.height), v, t.labelRight, vc);
            x -= vw;
            used += vw;
            if (row.Adjust != null && focus)
            {
                float cw = UiTheme.Px(22f);
                x -= UiTheme.Px(UiTheme.S2);
                UiTheme.Text(new Rect(x - cw, r.y, cw, r.height), "‹", t.labelRight, UiTheme.Accent);
                x -= cw;
                used += cw + UiTheme.Px(UiTheme.S2);
            }
            if (row.Fraction != null)
            {
                // Slider track: where the value sits in its range.
                float tw = UiTheme.Px(140f), th = UiTheme.Hair(4f);
                x -= UiTheme.Px(UiTheme.S4) + tw;
                var track = new Rect(x, r.center.y - th * 0.5f, tw, th);
                UiTheme.Fill(track, UiTheme.Border);
                UiTheme.Fill(new Rect(track.x, track.y, tw * Mathf.Clamp01(row.Fraction()), th), focus ? UiTheme.Accent : UiTheme.TextSecondary);
                used += UiTheme.Px(UiTheme.S4) + tw;
            }
            return used;
        }

        /// <summary>Horizontal value bar for axis monitors: value in [-1, 1] (bipolar) or [0, 1].</summary>
        public static void Bar(Rect r, float v, bool bipolar)
        {
            UiTheme.Fill(r, UiTheme.Border);
            if (bipolar)
            {
                float mid = r.x + r.width * 0.5f;
                float w = r.width * 0.5f * Mathf.Clamp(v, -1f, 1f);
                UiTheme.Fill(w >= 0 ? new Rect(mid, r.y, w, r.height) : new Rect(mid + w, r.y, -w, r.height), UiTheme.Accent);
                UiTheme.Fill(new Rect(mid - 1f, r.y - UiTheme.Px(3f), 2f, r.height + UiTheme.Px(6f)), UiTheme.TextPrimary);
            }
            else
            {
                UiTheme.Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), UiTheme.Accent);
            }
        }
    }
}
