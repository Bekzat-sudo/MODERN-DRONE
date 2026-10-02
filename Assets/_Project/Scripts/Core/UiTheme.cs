using System.Collections.Generic;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>Interaction state of a list item, card, tab or button.</summary>
    public enum UiState
    {
        Normal,
        Hover,
        /// <summary>Has keyboard / gamepad focus: Enter / A acts on it.</summary>
        Focus,
        /// <summary>The chosen item of a group whose focus is elsewhere (the open tab, the installed part).</summary>
        Current,
        Disabled
    }

    /// <summary>
    /// "Tactical tablet" design system shared by every IMGUI screen (menus, hangar, briefing, debrief, killcam captions).
    /// Olive ground with a map grid, Oswald for headings and big numbers, IBM Plex Mono for data (both SIL OFL,
    /// Resources/Fonts). Sizes are given at 1080p and scale with screen height; no text is below 18 px at 1080p
    /// (Xbox Accessibility Guideline 101, PC). Colour carries meaning the MIL-STD-1472H way and is always paired with
    /// a word, an arrow or a shape: amber = focus / your choice, green = done, red = hostile / lost, blue = better,
    /// orange = worse or attention.
    /// </summary>
    public static class UiTheme
    {
        // ---------------------------------------------------------------- colour tokens

        public static readonly Color Ground = Hex(0x1b1f17);
        public static readonly Color Grid = Hex(0x252a1f);
        public static readonly Color Surface = Hex(0x15180f);
        public static readonly Color SurfaceRaised = Hex(0x21261b);
        public static readonly Color Border = Hex(0x363d2c);
        public static readonly Color TextPrimary = Hex(0xe8e6da);
        public static readonly Color TextBody = Hex(0xc4c2b3);
        public static readonly Color TextSecondary = Hex(0x959b86);
        public static readonly Color TextDisabled = Hex(0x5e6454);
        public static readonly Color Accent = Hex(0xe8a33d);
        public static readonly Color AccentSurface = Hex(0x302915);
        public static readonly Color OnAccent = Hex(0x16130a);
        public static readonly Color Success = Hex(0x86b56f);
        public static readonly Color Warning = Hex(0xf07f3c);
        public static readonly Color Danger = Hex(0xff6455);
        public static readonly Color Info = Hex(0x5aa9e6);
        /// <summary>Dims the 3D scene behind a menu opened over gameplay.</summary>
        public static readonly Color Scrim = new Color(0.05f, 0.06f, 0.04f, 0.78f);
        /// <summary>Plate behind text drawn over live video (readable on sky and on snow).</summary>
        public static readonly Color OverVideo = new Color(0f, 0f, 0f, 0.6f);

        // ---------------------------------------------------------------- spacing (px at 1080p)

        public const float S1 = 4f, S2 = 8f, S3 = 12f, S4 = 16f, S5 = 24f, S6 = 32f, S7 = 48f, S8 = 64f;
        /// <summary>Screen margin of every full-screen layout.</summary>
        public const float Margin = 64f;

        /// <summary>Pixels for a size given at 1080p.</summary>
        public static float Px(float at1080) => at1080 * Screen.height / 1080f;

        /// <summary>A rule or edge width: whole pixels, never thinner than one.</summary>
        public static float Hair(float at1080) => Mathf.Max(1f, Mathf.Round(Px(at1080)));

        public static int FontPx(float at1080) => Mathf.Max(12, Mathf.RoundToInt(Px(at1080)));

        // ---------------------------------------------------------------- type scale

        static Font _display, _mono, _monoBold;

        public static Font Display => _display != null ? _display : _display = Resources.Load<Font>("Fonts/Oswald");
        public static Font Mono => _mono != null ? _mono : _mono = Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
        public static Font MonoBold => _monoBold != null ? _monoBold : _monoBold = Resources.Load<Font>("Fonts/IBMPlexMono-SemiBold");

        /// <summary>The shared styles. Draw them through <see cref="Text"/> with a colour token; they are rebuilt when the screen height changes.</summary>
        public sealed class TypeScale
        {
            /// <summary>Brand (72).</summary>
            public GUIStyle display;
            /// <summary>Screen or mission title (44).</summary>
            public GUIStyle title;
            /// <summary>Wrapping title for narrow columns (40).</summary>
            public GUIStyle titleWrap;
            /// <summary>Section heading, card title, large menu item (28).</summary>
            public GUIStyle heading;
            /// <summary>Tab label (22).</summary>
            public GUIStyle tab;
            /// <summary>Running text, wraps (19).</summary>
            public GUIStyle body;
            /// <summary>Single-line data: list labels and values (20).</summary>
            public GUIStyle label, labelBold, labelRight;
            /// <summary>Labels of values, notes, captions (18).</summary>
            public GUIStyle caption, captionRight, captionCenter;
            /// <summary>Important numbers: stat tiles, score (36).</summary>
            public GUIStyle number, numberRight;
            /// <summary>Text of a key cap and of a button (18 / 26).</summary>
            public GUIStyle key, button;
        }

        static TypeScale _type;
        static int _typeHeight;

        public static TypeScale T
        {
            get
            {
                if (_type != null && _typeHeight == Screen.height) return _type;
                _typeHeight = Screen.height;
                _type = new TypeScale
                {
                    display = Style(Display, 72f, TextPrimary),
                    title = Style(Display, 44f, TextPrimary),
                    titleWrap = Style(Display, 40f, TextPrimary, TextAnchor.UpperLeft, true),
                    heading = Style(Display, 28f, TextPrimary),
                    tab = Style(Display, 22f, TextSecondary, TextAnchor.MiddleCenter),
                    body = Style(Mono, 19f, TextBody, TextAnchor.UpperLeft, true),
                    label = Style(Mono, 20f, TextBody),
                    labelBold = Style(MonoBold, 20f, TextPrimary),
                    labelRight = Style(Mono, 20f, TextPrimary, TextAnchor.MiddleRight),
                    caption = Style(Mono, 18f, TextSecondary),
                    captionRight = Style(Mono, 18f, TextSecondary, TextAnchor.MiddleRight),
                    captionCenter = Style(Mono, 18f, TextSecondary, TextAnchor.MiddleCenter),
                    number = Style(Display, 36f, TextPrimary),
                    numberRight = Style(Display, 36f, TextPrimary, TextAnchor.MiddleRight),
                    key = Style(MonoBold, 18f, OnAccent, TextAnchor.MiddleCenter),
                    button = Style(Display, 26f, OnAccent, TextAnchor.MiddleCenter),
                };
                _type.display.fontStyle = FontStyle.Bold;
                _type.title.fontStyle = FontStyle.Bold;
                return _type;
            }
        }

        public static GUIStyle Style(Font font, float size1080, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = FontPx(size1080),
                alignment = anchor,
                wordWrap = wrap,
                clipping = TextClipping.Clip,
                richText = true,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            s.normal.textColor = color;
            return s;
        }

        /// <summary>Draws text in a colour token (the shared style keeps its own colour).</summary>
        public static void Text(Rect r, string text, GUIStyle style, Color color)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }

        /// <summary>Single-line text that shrinks (down to 70 %) instead of being clipped when it is too long.</summary>
        public static void TextFit(Rect r, string text, GUIStyle style, Color color)
        {
            int size = style.fontSize;
            var content = new GUIContent(text);
            while (style.fontSize > size * 0.7f && style.CalcSize(content).x > r.width) style.fontSize--;
            Text(r, text, style, color);
            style.fontSize = size;
        }

        /// <summary>Wrapping text that steps its size down (to 80 %) until it fits the rect's height.</summary>
        public static void TextBlockFit(Rect r, string text, GUIStyle style, Color color)
        {
            int size = style.fontSize;
            var content = new GUIContent(text);
            while (style.fontSize > size * 0.8f && style.CalcHeight(content, r.width) > r.height) style.fontSize--;
            Text(r, text, style, color);
            style.fontSize = size;
        }

        public static float Width(string text, GUIStyle style) => style.CalcSize(new GUIContent(text)).x;

        public static string Colored(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        // ---------------------------------------------------------------- primitives

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>Outline of the given width.</summary>
        public static void Frame(Rect r, Color c, float width = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, width), c);
            Fill(new Rect(r.x, r.yMax - width, r.width, width), c);
            Fill(new Rect(r.x, r.y, width, r.height), c);
            Fill(new Rect(r.xMax - width, r.y, width, r.height), c);
        }

        public static Rect Inset(Rect r, float x, float y) => new Rect(r.x + x, r.y + y, r.width - 2f * x, r.height - 2f * y);

        static bool Hovered(Rect r) => r.Contains(Event.current.mousePosition);

        public static bool Clicked(Rect r)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        // ---------------------------------------------------------------- motion

        static readonly Dictionary<int, (Rect rect, float time)> _slides = new Dictionary<int, (Rect, float)>();

        /// <summary>
        /// A rect that glides to <paramref name="target"/> in about 0.1 s (the focus highlight following the selection).
        /// Snaps when the element was not drawn for a while, so a screen never opens mid-animation.
        /// </summary>
        public static Rect Slide(int id, Rect target)
        {
            float now = Time.unscaledTime;
            // Only position glides: a new size means a new layout (resolution, other list), so it snaps.
            if (!_slides.TryGetValue(id, out var s) || now - s.time > 0.25f
                || Mathf.Abs(s.rect.width - target.width) > 1f || Mathf.Abs(s.rect.height - target.height) > 1f)
                s = (target, now);
            else if (Event.current.type == EventType.Repaint)
            {
                float k = 1f - Mathf.Exp(-(now - s.time) * 24f);
                var r = s.rect;
                r = new Rect(Mathf.Lerp(r.x, target.x, k), Mathf.Lerp(r.y, target.y, k), Mathf.Lerp(r.width, target.width, k), Mathf.Lerp(r.height, target.height, k));
                if (Mathf.Abs(r.y - target.y) < 0.5f && Mathf.Abs(r.x - target.x) < 0.5f) r = target;
                s = (r, now);
            }
            _slides[id] = s;
            return s.rect;
        }

        // ---------------------------------------------------------------- components

        /// <summary>Panel: the surface under a group of content.</summary>
        public static void Panel(Rect r)
        {
            Fill(r, Surface);
            Frame(r, Border);
        }

        /// <summary>
        /// Background of a list item / card for a state. Focus: warm fill with a 4 px amber edge. Current: the amber edge
        /// alone (chosen, but focus is elsewhere). Hover: a faint lift. Raised items sit on <see cref="SurfaceRaised"/>.
        /// Pass <paramref name="slideId"/> to let the focus glide between items of one list.
        /// </summary>
        public static void Item(Rect r, UiState state, bool raised = false, int slideId = 0)
        {
            if (raised) Fill(r, SurfaceRaised);
            switch (state)
            {
                case UiState.Focus:
                    var f = slideId != 0 ? Slide(slideId, r) : r;
                    Fill(f, AccentSurface);
                    Fill(new Rect(f.x, f.y, Hair(4f), f.height), Accent);
                    break;
                case UiState.Current:
                    Fill(new Rect(r.x, r.y, Hair(4f), r.height), new Color(Accent.r, Accent.g, Accent.b, 0.55f));
                    break;
                case UiState.Hover:
                    Fill(r, new Color(1f, 1f, 1f, 0.045f));
                    break;
            }
        }

        /// <summary>State of an item from its flags (hover only matters while nothing has focus there).</summary>
        public static UiState StateOf(Rect r, bool selected, bool focused, bool enabled = true)
        {
            if (!enabled) return UiState.Disabled;
            if (selected) return focused ? UiState.Focus : UiState.Current;
            return Hovered(r) ? UiState.Hover : UiState.Normal;
        }

        /// <summary>Text colour of an item's main label in a state.</summary>
        public static Color LabelColor(UiState s) => s == UiState.Disabled ? TextDisabled : s == UiState.Normal ? TextBody : TextPrimary;

        /// <summary>
        /// Button. Primary is the screen's main action: outlined in amber, filled once it has focus (Enter / A presses
        /// it). Secondary is outlined in the border colour. A key cap shows the shortcut. Returns true when clicked.
        /// </summary>
        public static bool Button(Rect r, string label, bool primary, bool focused, string key = null)
        {
            var t = T;
            bool hover = Hovered(r);
            Color ink;
            if (primary && focused)
            {
                Fill(r, hover ? Color.Lerp(Accent, Color.white, 0.12f) : Accent);
                ink = OnAccent;
            }
            else if (focused)
            {
                Fill(r, AccentSurface);
                Frame(r, Accent, Hair(2f));
                ink = TextPrimary;
            }
            else
            {
                Fill(r, hover ? SurfaceRaised : Surface);
                Frame(r, primary ? Accent : Border, Hair(primary ? 2f : 1f));
                ink = primary ? Accent : TextPrimary;
            }
            float lw = Width(label, t.button);
            float kw = 0f, gap = Px(S3);
            if (!string.IsNullOrEmpty(key)) kw = KeyWidth(key);
            float x = r.center.x - (lw + (kw > 0f ? kw + gap : 0f)) * 0.5f;
            if (kw > 0f)
            {
                float kh = Px(30f);
                KeyCap(new Rect(x, r.center.y - kh * 0.5f, kw, kh), key, primary && focused ? OnAccent : TextPrimary, primary && focused ? Accent : OnAccent);
                x += kw + gap;
            }
            Text(new Rect(x, r.y, lw + 2f, r.height), label, t.button, ink);
            return Clicked(r);
        }

        public static float KeyWidth(string key) => Mathf.Max(Px(30f), Width(key, T.key) + Px(14f));

        /// <summary>A key or controller button: light cap, dark glyph.</summary>
        public static void KeyCap(Rect r, string key, Color cap, Color glyph)
        {
            Fill(r, cap);
            Text(r, key, T.key, glyph);
        }

        /// <summary>
        /// Row of control prompts "[key] action", left-aligned (or centred). Each prompt is also clickable for mouse
        /// users; returns the index clicked, else -1.
        /// </summary>
        public static int Prompts(Rect r, bool center, params (string key, string action)[] items)
        {
            var t = T;
            float h = Px(30f), gapKey = Px(S2), gapItem = Px(S6);
            float total = 0f;
            foreach (var (k, a) in items) total += KeyWidth(k) + gapKey + Width(a, t.caption) + gapItem;
            total -= gapItem;
            float x = center ? r.center.x - total * 0.5f : r.x;
            float y = r.y + (r.height - h) * 0.5f;
            int clicked = -1;
            for (int i = 0; i < items.Length; i++)
            {
                var (k, a) = items[i];
                float kw = KeyWidth(k), aw = Width(a, t.caption);
                var hit = new Rect(x, r.y, kw + gapKey + aw, r.height);
                bool hover = Hovered(hit);
                KeyCap(new Rect(x, y, kw, h), k, hover ? Accent : TextPrimary, OnAccent);
                Text(new Rect(x + kw + gapKey, r.y, aw + 2f, r.height), a, t.caption, hover ? TextPrimary : TextBody);
                if (Clicked(hit)) clicked = i;
                x += kw + gapKey + aw + gapItem;
            }
            return clicked;
        }

        /// <summary>
        /// Tab strip with the previous / next key caps at its ends and an amber underline under the open tab.
        /// Tabs take their text's width; a strip too long for the space scrolls to keep the open tab in view.
        /// Returns the tab clicked (or <paramref name="current"/>).
        /// </summary>
        public static int Tabs(Rect r, string[] names, int current, string prevKey, string nextKey, int slideId)
        {
            var t = T;
            float pad = Px(S4);
            float pw = KeyWidth(prevKey), nw = KeyWidth(nextKey);
            var strip = new Rect(r.x + pw + Px(S4), r.y, r.width - pw - nw - 2f * Px(S4), r.height);
            if (Prompts(new Rect(r.x, r.y, pw, r.height), false, (prevKey, "")) == 0) current = (current + names.Length - 1) % names.Length;
            if (Prompts(new Rect(r.xMax - nw, r.y, nw, r.height), false, (nextKey, "")) == 0) current = (current + 1) % names.Length;
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), Border);

            var widths = new float[names.Length];
            float total = 0f, before = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                widths[i] = Width(names[i], t.tab) + 2f * pad;
                if (i < current) before += widths[i];
                total += widths[i];
            }
            float scroll = total <= strip.width ? 0f : Mathf.Clamp(before + widths[current] * 0.5f - strip.width * 0.5f, 0f, total - strip.width);

            int clicked = current;
            GUI.BeginClip(strip);
            float x = -scroll;
            for (int i = 0; i < names.Length; i++)
            {
                var tr = new Rect(x, 0f, widths[i], strip.height);
                bool sel = i == current;
                bool hover = tr.Contains(Event.current.mousePosition);
                Text(tr, names[i], t.tab, sel ? TextPrimary : hover ? TextBody : TextSecondary);
                if (sel)
                {
                    var u = Slide(slideId, new Rect(tr.x + pad * 0.5f, tr.yMax - Hair(3f), tr.width - pad, Hair(3f)));
                    Fill(u, Accent);
                }
                if (Clicked(tr)) clicked = i;
                x += widths[i];
            }
            GUI.EndClip();
            return clicked;
        }

        /// <summary>Metric tile: caption on top, the value large below; an optional note under it.</summary>
        public static void Stat(Rect r, string label, string value, Color valueColor, string note = null)
        {
            var t = T;
            Fill(r, SurfaceRaised);
            var inner = Inset(r, Px(S4), Px(S3));
            Text(new Rect(inner.x, inner.y, inner.width, Px(24f)), label, t.caption, TextSecondary);
            float vy = note != null ? inner.y + Px(24f) : inner.y + Px(20f);
            TextFit(new Rect(inner.x, vy, inner.width, inner.yMax - vy - (note != null ? Px(24f) : 0f)), value, t.number, valueColor);
            if (note != null) Text(new Rect(inner.x, inner.yMax - Px(24f), inner.width, Px(24f)), note, t.caption, TextSecondary);
        }

        /// <summary>"label .... value" on one line.</summary>
        public static void KeyValue(Rect r, string label, string value, Color valueColor)
        {
            Text(r, label, T.caption, TextSecondary);
            Text(r, value, T.labelRight, valueColor);
        }

        /// <summary>
        /// Count as a row of blocks (missions done, drones left): <paramref name="filled"/> of <paramref name="total"/>
        /// in <paramref name="color"/>, the block at <paramref name="mark"/> outlined in amber.
        /// </summary>
        public static void Segments(Rect r, int total, int filled, Color color, int mark = -1)
        {
            if (total <= 0) return;
            float gap = Mathf.Max(2f, Px(S1)), w = (r.width - gap * (total - 1)) / total;
            for (int i = 0; i < total; i++)
            {
                var b = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                Fill(b, i < filled ? color : Border);
                if (i == mark) Frame(b, Accent, Hair(2f));
            }
        }

        /// <summary>Square status marker (filled = done) in front of a status word.</summary>
        public static void Marker(Rect lineRect, Color c, bool filled)
        {
            float s = Px(10f);
            var m = new Rect(lineRect.x, lineRect.center.y - s * 0.5f, s, s);
            if (filled) Fill(m, c);
            else Frame(m, c, Hair(2f));
        }

        /// <summary>Centred alert plate over gameplay ("ДРОН ПОТЕРЯН", "ПОПАДАНИЕ").</summary>
        public static void Banner(float y, string text, Color color, string sub = null)
        {
            var t = T;
            float w = Mathf.Max(Width(text, t.title), sub != null ? Width(sub, t.caption) : 0f) + Px(S7) * 2f;
            float h = t.title.fontSize * 1.5f + (sub != null ? Px(30f) : 0f);
            var r = new Rect((Screen.width - w) * 0.5f, y, w, h);
            Fill(r, OverVideo);
            Fill(new Rect(r.x, r.y, r.width, Hair(3f)), color);
            Text(new Rect(r.x, r.y, r.width, t.title.fontSize * 1.5f), text, t.title, color, TextAnchor.MiddleCenter);
            if (sub != null) Text(new Rect(r.x, r.yMax - Px(38f), r.width, Px(30f)), sub, t.captionCenter, TextBody);
        }

        static void Text(Rect r, string text, GUIStyle style, Color color, TextAnchor anchor)
        {
            var a = style.alignment;
            style.alignment = anchor;
            Text(r, text, style, color);
            style.alignment = a;
        }

        /// <summary>Scroll position of a list: a thin track with a thumb, only when not everything fits.</summary>
        public static void ScrollBar(Rect track, int first, int visible, int total)
        {
            if (total <= visible) return;
            Fill(track, Border);
            float h = track.height * visible / total, y = track.y + (track.height - h) * first / Mathf.Max(1, total - visible);
            Fill(new Rect(track.x, y, track.width, h), TextSecondary);
        }

        // ---------------------------------------------------------------- background

        static Texture2D _grid;

        /// <summary>Full-screen olive ground with a map grid.</summary>
        public static void Background() => GroundRect(new Rect(0, 0, Screen.width, Screen.height));

        /// <summary>The ground everywhere except <paramref name="window"/>, which shows the 3D scene behind the UI.</summary>
        public static void Background(Rect window)
        {
            float w = Screen.width, h = Screen.height;
            GroundRect(new Rect(0, 0, w, window.y));
            GroundRect(new Rect(0, window.yMax, w, h - window.yMax));
            GroundRect(new Rect(0, window.y, window.x, window.height));
            GroundRect(new Rect(window.xMax, window.y, w - window.xMax, window.height));
        }

        static void GroundRect(Rect r)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Fill(r, Ground);
            if (_grid == null)
            {
                const int n = 64;
                _grid = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = x == 0 || y == 0 ? Grid : Ground;
                _grid.SetPixels(px);
                _grid.Apply();
            }
            // Texture coordinates follow the screen position, so the pieces of a split background line up.
            float cell = Px(64f);
            GUI.DrawTextureWithTexCoords(r, _grid, new Rect(r.x / cell, (Screen.height - r.yMax) / cell, r.width / cell, r.height / cell));
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
