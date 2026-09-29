using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>
    /// "Tactical tablet" look shared by every IMGUI screen: olive ground with a map grid, Oswald for headings,
    /// IBM Plex Mono for data (both SIL OFL, Resources/Fonts). Sizes scale with screen height against a 1080p
    /// reference and never go below 18 px at 1080p (Xbox Accessibility Guideline 101, PC). Colour carries meaning
    /// the MIL-STD-1472H way: amber = focus / selection, red = hostile / alarm, green = done / normal,
    /// blue = better, orange = worse — always paired with an arrow or a word.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Ground = Hex(0x1b1f17);
        public static readonly Color GridLine = Hex(0x262b20);
        public static readonly Color Panel = Hex(0x171a13);
        public static readonly Color PanelRaised = Hex(0x20251a);
        public static readonly Color Line = Hex(0x3a4130);
        public static readonly Color Text = Hex(0xe4e2d6);
        public static readonly Color Body = Hex(0xc9c7b8);
        public static readonly Color Muted = Hex(0x9aa08a);
        public static readonly Color Accent = Hex(0xe8a33d);
        public static readonly Color AccentFill = Hex(0x2c2716);
        public static readonly Color Done = Hex(0x7fb069);
        public static readonly Color Hostile = Hex(0xff6b5b);
        public static readonly Color Better = Hex(0x5aa9e6);
        public static readonly Color Worse = Hex(0xf29a2e);

        static Font _display, _mono, _monoBold;
        static Texture2D _grid;

        public static Font Display => _display != null ? _display : _display = Resources.Load<Font>("Fonts/Oswald");
        public static Font Mono => _mono != null ? _mono : _mono = Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
        public static Font MonoBold => _monoBold != null ? _monoBold : _monoBold = Resources.Load<Font>("Fonts/IBMPlexMono-SemiBold");

        /// <summary>Pixels for a size given at 1080p.</summary>
        public static float Px(float at1080) => at1080 * Screen.height / 1080f;

        public static int FontPx(float at1080) => Mathf.Max(12, Mathf.RoundToInt(Px(at1080)));

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

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>1 px outline.</summary>
        public static void Frame(Rect r, Color c, float width = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, width), c);
            Fill(new Rect(r.x, r.yMax - width, r.width, width), c);
            Fill(new Rect(r.x, r.y, width, r.height), c);
            Fill(new Rect(r.xMax - width, r.y, width, r.height), c);
        }

        /// <summary>Panel with a border; the selected state is amber on a warm fill.</summary>
        public static void Box(Rect r, bool selected = false, bool raised = false)
        {
            Fill(r, selected ? AccentFill : raised ? PanelRaised : Panel);
            Frame(r, selected ? Accent : Line);
        }

        /// <summary>Full-screen olive ground with a map grid.</summary>
        public static void Background() => Ground_(new Rect(0, 0, Screen.width, Screen.height));

        /// <summary>The ground everywhere except <paramref name="window"/>, which shows the 3D scene behind the UI.</summary>
        public static void Background(Rect window)
        {
            float w = Screen.width, h = Screen.height;
            Ground_(new Rect(0, 0, w, window.y));
            Ground_(new Rect(0, window.yMax, w, h - window.yMax));
            Ground_(new Rect(0, window.y, window.x, window.height));
            Ground_(new Rect(window.xMax, window.y, w - window.xMax, window.height));
        }

        static void Ground_(Rect r)
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
                    px[y * n + x] = x == 0 || y == 0 ? GridLine : Ground;
                _grid.SetPixels(px);
                _grid.Apply();
            }
            // Texture coordinates follow the screen position, so the pieces of a split background line up.
            float cell = Px(64f);
            GUI.DrawTextureWithTexCoords(r, _grid, new Rect(r.x / cell, (Screen.height - r.yMax) / cell, r.width / cell, r.height / cell));
        }

        /// <summary>Row of controller / key prompts: [key] action.</summary>
        public static void Prompts(Rect r, GUIStyle label, GUIStyle key, params (string key, string action)[] items)
        {
            float x = r.x, h = Px(30f);
            float y = r.y + (r.height - h) * 0.5f;
            foreach (var (k, a) in items)
            {
                float kw = Mathf.Max(h, key.CalcSize(new GUIContent(k)).x + Px(14f));
                var kr = new Rect(x, y, kw, h);
                Fill(kr, Text);
                GUI.Label(kr, k, key);
                x += kw + Px(8f);
                float aw = label.CalcSize(new GUIContent(a)).x;
                GUI.Label(new Rect(x, r.y, aw, r.height), a, label);
                x += aw + Px(28f);
            }
        }

        public static string Colored(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
