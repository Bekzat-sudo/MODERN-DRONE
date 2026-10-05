using ModernDrone.Drone;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// The pilot's aiming mark, drawn from <see cref="OsdSettings"/>: shape, size, line weight, centre gap, colour,
    /// outline, opacity and a vertical offset (to put the mark where the charge goes on a tilted camera). Used by the
    /// flight OSD and by the settings preview, so both show the same thing.
    /// </summary>
    public static class Crosshair
    {
        public static readonly string[] StyleNames = { "Классический", "Крест", "Точка", "Кольцо", "Кольцо с точкой", "Галочка", "Уголки" };
        public static readonly string[] ColorNames = { "Белый", "Зелёный", "Жёлтый", "Голубой", "Красный", "Чёрный" };
        static readonly Color[] Colors =
        {
            Color.white, new Color(0.3f, 1f, 0.35f), new Color(1f, 0.92f, 0.2f), new Color(0.3f, 0.9f, 1f), new Color(1f, 0.25f, 0.2f), Color.black,
        };

        /// <summary>Draws the mark centred on <paramref name="centre"/>; <paramref name="unit"/> is the base size in pixels.</summary>
        public static void Draw(Vector2 centre, float unit, OsdSettings osd)
        {
            float s = unit * Mathf.Clamp(osd.crosshairSize, 0.3f, 3f);
            float w = Mathf.Max(1.5f, s * 0.12f * Mathf.Clamp(osd.crosshairThickness, 0.3f, 4f));
            float g = s * 0.6f * Mathf.Clamp(osd.crosshairGap, 0f, 3f);
            var c = Colors[Mathf.Clamp(osd.crosshairColor, 0, Colors.Length - 1)];
            c.a = Mathf.Clamp(osd.crosshairOpacity, 0.1f, 1f);
            // A black mark gets a light outline; everything else a dark one.
            var outline = osd.crosshairOutline ? (osd.crosshairColor == 5 ? new Color(1f, 1f, 1f, 0.7f * c.a) : new Color(0f, 0f, 0f, 0.8f * c.a)) : Color.clear;
            // Outline under everything first, then the colour: joints and the ring's segments stay clean.
            if (outline.a > 0f) Shape(centre, s, w, g, (CrosshairStyle)osd.crosshairStyle, outline, 1f);
            Shape(centre, s, w, g, (CrosshairStyle)osd.crosshairStyle, c, 0f);
        }

        /// <summary>One pass of the shape; <paramref name="grow"/> widens every stroke by that many pixels a side.</summary>
        static void Shape(Vector2 centre, float s, float w, float g, CrosshairStyle style, Color c, float grow)
        {
            float x = centre.x, y = centre.y;
            _grow = grow;
            switch (style)
            {
                case CrosshairStyle.Cross:
                    Line(new Vector2(x - s * 2f, y), new Vector2(x - g, y), w, c);
                    Line(new Vector2(x + g, y), new Vector2(x + s * 2f, y), w, c);
                    Line(new Vector2(x, y - s * 2f), new Vector2(x, y - g), w, c);
                    Line(new Vector2(x, y + g), new Vector2(x, y + s * 2f), w, c);
                    break;
                case CrosshairStyle.Dot:
                    Dot(centre, w * 1.6f, c);
                    break;
                case CrosshairStyle.Circle:
                    Ring(centre, s * 1.2f + g * 0.5f, w, c);
                    break;
                case CrosshairStyle.CircleDot:
                    Ring(centre, s * 1.2f + g * 0.5f, w, c);
                    Dot(centre, w * 1.3f, c);
                    break;
                case CrosshairStyle.Chevron:
                    Line(new Vector2(x, y + g * 0.3f), new Vector2(x - s * 1.3f, y + s * 1.1f + g * 0.3f), w, c);
                    Line(new Vector2(x, y + g * 0.3f), new Vector2(x + s * 1.3f, y + s * 1.1f + g * 0.3f), w, c);
                    break;
                case CrosshairStyle.Brackets:
                {
                    float r = s * 1.4f + g * 0.5f, l = s * 0.7f;
                    foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                    {
                        var corner = new Vector2(x + sx * r, y + sy * r);
                        Line(corner, corner - new Vector2(sx * l, 0f), w, c);
                        Line(corner, corner - new Vector2(0f, sy * l), w, c);
                    }
                    break;
                }
                default:
                    // Betaflight-like: two side arms and a short tick above the centre.
                    Line(new Vector2(x - s * 2f, y), new Vector2(x - g, y), w, c);
                    Line(new Vector2(x + g, y), new Vector2(x + s * 2f, y), w, c);
                    Line(new Vector2(x, y - g), new Vector2(x, y - g * 0.33f), w, c);
                    break;
            }
        }

        static float _grow;

        static void Line(Vector2 a, Vector2 b, float w, Color color)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x - _grow, a.y - w * 0.5f - _grow, len + 2f * _grow, w + 2f * _grow), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.matrix = matrix;
        }

        static void Dot(Vector2 c, float size, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            float h = size * 0.5f + _grow;
            GUI.DrawTexture(new Rect(c.x - h, c.y - h, 2f * h, 2f * h), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static void Ring(Vector2 c, float radius, float w, Color color)
        {
            const int Segments = 28;
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / Segments, a1 = (i + 1.08f) * Mathf.PI * 2f / Segments;
                Line(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, w, color);
            }
        }
    }
}
