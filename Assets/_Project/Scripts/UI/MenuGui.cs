using System;
using System.Collections.Generic;
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

        public MenuRow(string label) => Label = label;

        /// <summary>The row whose text is being typed; menus skip their navigation while it is set.</summary>
        public static MenuRow Editing;
        static int _editingSince;

        /// <summary>
        /// Text field limited to <paramref name="maxLength"/> characters that <paramref name="accept"/> maps to a
        /// stored character (or '\0' to reject).
        /// </summary>
        public static int EditingSince => _editingSince;

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
            Adjust = _ => set(!get()),
            Activate = () => set(!get())
        };

        public static MenuRow Slider(string label, Func<float> get, Action<float> set, float min, float max, float step, string format = "0.00") =>
            new MenuRow(label)
            {
                Value = () => get().ToString(format, System.Globalization.CultureInfo.InvariantCulture),
                Adjust = dir => set(Mathf.Clamp(Mathf.Round((get() + dir * step) / step) * step, min, max))
            };

        public static MenuRow Choice(string label, string[] names, Func<int> get, Action<int> set) => new MenuRow(label)
        {
            Value = () => names[Mathf.Clamp(get(), 0, names.Length - 1)],
            Adjust = dir => set((get() + dir + names.Length) % names.Length),
            Activate = () => set((get() + 1) % names.Length)
        };

        public bool Selectable => !IsHeader && (Adjust != null || Activate != null);
    }

    public struct MenuNav
    {
        public bool up, down, left, right, confirm, back, tabPrev, tabNext;
    }

    /// <summary>
    /// Minimal IMGUI menu toolkit with keyboard, gamepad and mouse navigation.
    /// </summary>
    public sealed class MenuGui
    {
        const float RepeatDelay = 0.35f;
        const float RepeatRate = 0.09f;

        public GUIStyle title, subtitle, row, rowSelected, value, header, hint, tab, tabSelected;
        int _styleHeight;

        // Held-direction auto-repeat for sticks / d-pad / keys.
        Vector2Int _heldDir;
        float _nextRepeat;

        static Texture2D _white;
        static Texture2D White => _white != null ? _white : _white = Texture2D.whiteTexture;

        public static readonly Color PanelColor = new Color(ModernDrone.Core.UiTheme.Panel.r, ModernDrone.Core.UiTheme.Panel.g, ModernDrone.Core.UiTheme.Panel.b, 0.96f);
        public static readonly Color Accent = ModernDrone.Core.UiTheme.Accent;

        public void EnsureStyles()
        {
            if (title != null && _styleHeight == Screen.height) return;
            _styleHeight = Screen.height;
            // "Tactical tablet" theme: data in IBM Plex Mono, headings in Oswald (ModernDrone.Core.UiTheme).
            int size = ModernDrone.Core.UiTheme.FontPx(22f);

            row = new GUIStyle(GUI.skin.label) { font = ModernDrone.Core.UiTheme.Mono, fontSize = size, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            row.normal.textColor = ModernDrone.Core.UiTheme.Body;
            rowSelected = new GUIStyle(row) { font = ModernDrone.Core.UiTheme.MonoBold };
            rowSelected.normal.textColor = ModernDrone.Core.UiTheme.Text;
            value = new GUIStyle(row) { alignment = TextAnchor.MiddleRight };
            value.normal.textColor = Accent;
            header = new GUIStyle(row) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(size * 0.8f) };
            header.normal.textColor = ModernDrone.Core.UiTheme.Muted;
            hint = new GUIStyle(row) { fontSize = Mathf.RoundToInt(size * 0.75f), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            hint.normal.textColor = ModernDrone.Core.UiTheme.Muted;
            title = new GUIStyle(row) { font = ModernDrone.Core.UiTheme.Display, fontSize = Mathf.RoundToInt(size * 2.2f), alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = ModernDrone.Core.UiTheme.Text;
            subtitle = new GUIStyle(row) { fontSize = Mathf.RoundToInt(size * 0.9f), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            subtitle.normal.textColor = ModernDrone.Core.UiTheme.Body;
            tab = new GUIStyle(row) { font = ModernDrone.Core.UiTheme.Display, alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(size * 0.95f) };
            tab.normal.textColor = ModernDrone.Core.UiTheme.Muted;
            tabSelected = new GUIStyle(tab);
            tabSelected.normal.textColor = ModernDrone.Core.UiTheme.Text;
        }

        public float RowHeight => row.fontSize * 1.9f;

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
            if (nav.left) r.Adjust?.Invoke(-1);
            if (nav.right) r.Adjust?.Invoke(1);
            if (nav.confirm) (r.Activate ?? (() => r.Adjust?.Invoke(1)))();
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

        /// <summary>Draws rows (scrolling to keep the selection visible) and handles mouse input. Call from OnGUI.</summary>
        public void DrawRows(Rect area, List<MenuRow> rows, ref int selected)
        {
            float h = RowHeight;
            int visible = Mathf.Max(1, Mathf.FloorToInt(area.height / h));
            int first = Mathf.Clamp(selected - visible / 2, 0, Mathf.Max(0, rows.Count - visible));
            var e = Event.current;
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

            for (int i = first; i < Mathf.Min(rows.Count, first + visible); i++)
            {
                var r = rows[i];
                var rect = new Rect(area.x, area.y + (i - first) * h, area.width, h);
                if (r.IsHeader)
                {
                    GUI.Label(new Rect(rect.x, rect.y + h * 0.25f, rect.width, h * 0.75f), r.Label.ToUpperInvariant(), header);
                    continue;
                }

                bool isSel = i == selected;
                if (isSel) Fill(rect, ModernDrone.Core.UiTheme.AccentFill);
                if (isSel) Fill(new Rect(rect.x, rect.y, 3f, rect.height), Accent);

                float valueWidth = rect.width * 0.45f;
                GUI.Label(new Rect(rect.x + 14f, rect.y, rect.width - valueWidth - 14f, h), r.Label, isSel ? rowSelected : row);

                var valueRect = new Rect(rect.xMax - valueWidth, rect.y, valueWidth - 10f, h);
                string v = r.Value?.Invoke();
                if (v != null)
                    GUI.Label(valueRect, r.Adjust != null ? $"‹  {v}  ›" : v, value);

                // Mouse: hover selects, click on the value side adjusts (left/right half), elsewhere activates.
                if (!r.Selectable) continue;
                if (e.type == EventType.MouseMove && rect.Contains(e.mousePosition)) selected = i;
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    selected = i;
                    if (r.Adjust != null && valueRect.Contains(e.mousePosition))
                        r.Adjust(e.mousePosition.x < valueRect.center.x ? -1 : 1);
                    else
                        (r.Activate ?? (() => r.Adjust?.Invoke(1)))();
                    e.Use();
                }
            }
        }

        /// <summary>Tab strip; returns the new tab index (mouse click).</summary>
        public int DrawTabs(Rect area, string[] names, int current)
        {
            float w = area.width / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(area.x + i * w, area.y, w, area.height);
                if (i == current)
                {
                    Fill(r, new Color(1f, 1f, 1f, 0.07f));
                    Fill(new Rect(r.x, r.yMax - 3f, r.width, 3f), Accent);
                }
                if (GUI.Button(r, names[i], i == current ? tabSelected : tab))
                    current = i;
            }
            return current;
        }

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        /// <summary>Horizontal value bar for axis monitors: value in [-1, 1] (bipolar) or [0, 1].</summary>
        public static void Bar(Rect r, float v, bool bipolar)
        {
            Fill(r, new Color(1f, 1f, 1f, 0.12f));
            if (bipolar)
            {
                float mid = r.x + r.width * 0.5f;
                float w = r.width * 0.5f * Mathf.Clamp(v, -1f, 1f);
                Fill(w >= 0 ? new Rect(mid, r.y, w, r.height) : new Rect(mid + w, r.y, -w, r.height), Accent);
                Fill(new Rect(mid - 1f, r.y, 2f, r.height), Color.white);
            }
            else
            {
                Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), Accent);
            }
        }
    }
}
