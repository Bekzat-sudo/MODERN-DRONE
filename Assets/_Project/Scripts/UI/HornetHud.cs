using System.Collections.Generic;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
using ModernDrone.Missions;
using ModernDrone.Video;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// Aircraft-style HUD of the Hornet strike drone, after published operator-screen footage (x.com/RALee85, April 2026):
    /// thin green symbology over the forward camera, an airspeed tape on the left in m/s ("AIRSPD (m/s)", current value
    /// boxed in orange), an altitude tape on the right ("ALT", value boxed with a pointer and a reference line), a compass
    /// tape across the top (N / NE / E / SE) with a marker below it, a horizontal scale with a dot along the bottom, and
    /// the AI target lock: a red-orange box on each recognised vehicle with a tag "number | class" (e.g. "2 | VEH/ARM").
    /// A second frame of the same HUD shows a thin horizon line (here in the HUD green, kept between the tapes) through the
    /// aircraft symbol at the centre (a hollow ring, a tick above it and two short wings, set apart from the line) and a
    /// small "x": the symbol and the line are how the drone flies, the "x" where the operator is steering it (here the
    /// nose a second ahead at the rates the flight controller is commanding). The horizon through the symbol in level
    /// flight means the camera looks along the airframe. Assumptions (not readable in the footage, which blurs the
    /// telemetry fields): the compass marker shows the target area, the bottom scale is the throttle, recognition reaches
    /// 1 km with a clear line of sight (abstract).
    /// </summary>
    public static class HornetHud
    {
        static readonly Color Green = new Color(0.45f, 1f, 0.55f, 0.92f);
        static readonly Color Orange = new Color(1f, 0.62f, 0.15f, 0.95f);
        static readonly Color Blue = new Color(0.35f, 0.62f, 1f, 0.95f);
        static readonly Color Lock = new Color(1f, 0.3f, 0.12f, 0.95f);
        static readonly Color TagBack = new Color(0.1f, 0.06f, 0.02f, 0.78f);
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.45f);
        const float RecognitionRange = 1000f;

        static GUIStyle _small, _value;
        static int _styleHeight;
        static MissionTarget[] _targets;
        static readonly Dictionary<MissionTarget, string> Classes = new Dictionary<MissionTarget, string>();
        static readonly Vector3[] Corners = new Vector3[8];

        public static void Draw(DroneController drone, Camera cam, Rect f, InputSource source = InputSource.KeyboardMouse)
        {
            EnsureStyles();
            if (_targets == null || System.Array.Exists(_targets, x => x == null))
            {
                _targets = Object.FindObjectsByType<MissionTarget>(FindObjectsSortMode.InstanceID);
                Classes.Clear();
            }
            var t = drone.Telemetry;
            float h = f.height, w = f.width;
            float spdX = f.x + w * 0.11f, altX = f.xMax - w * 0.11f, midY = f.y + h * 0.52f, half = h * 0.24f;

            float alt = t.altitudeAgl >= 0f ? t.altitudeAgl : t.heightAboveHome;
            Tape(spdX, midY, half, t.speed, 12f, true, "AIRSPD\n(m/s)", w * 0.07f);
            Tape(altX, midY, half, alt, 12f, false, "ALT", w * 0.07f);
            Compass(f, t.headingDeg, TargetBearing(drone));
            ThrottleScale(new Vector2(spdX, f.yMax - h * 0.11f), f.center.x + w * 0.06f - spdX, t.throttle);
            var symbol = f.center;
            if (cam != null)
            {
                symbol = Horizon(cam, f, spdX, altX, midY - half, midY + half);
                CommandMarker(cam, drone, f.height);
                Targets(cam, f);
            }
            AircraftSymbol(symbol, f.height);

            // Link and battery under the tapes (the real fields are blurred in the footage).
            var link = VideoFeed.HasLink ? VideoFeed.Current : default;
            string linkText = !VideoFeed.HasLink ? "" : link.type == LinkType.Satellite ? $"SAT {link.control * 100f:0}" : $"LQ {link.control * 100f:0}";
            Text(new Rect(spdX - 60f, midY + half + _small.fontSize * 0.6f, 200f, _small.fontSize * 1.4f), linkText, _small, link.control < 0.3f ? Lock : Green, TextAnchor.MiddleLeft);
            Text(new Rect(altX - 140f, midY + half + _small.fontSize * 0.6f, 200f, _small.fontSize * 1.4f),
                $"BAT {drone.Battery.StateOfCharge * 100f:0}%", _small, drone.Battery.StateOfCharge < 0.15f ? Lock : Green, TextAnchor.MiddleRight);
        }

        /// <summary>
        /// The long line: level flight, the true horizon between the two tapes (where level directions meet the camera's
        /// view); it moves with pitch and tilts with bank. Returns where the aircraft symbol sits: on the line at the
        /// picture's centre column (kept between <paramref name="top"/> and <paramref name="bottom"/>), with a gap in the
        /// line for its wings. The symbol's level wings against the tilted line show the bank.
        /// </summary>
        static Vector2 Horizon(Camera cam, Rect f, float leftTape, float rightTape, float top, float bottom)
        {
            Vector3 fwd = cam.transform.forward, right = cam.transform.right;
            var hf = new Vector3(fwd.x, 0f, fwd.z);
            if (hf.sqrMagnitude < 0.01f) return f.center; // looking straight down or up: no horizon in view
            hf.Normalize();
            var hr = new Vector3(right.x, 0f, right.z).normalized;
            Vector3 o = cam.transform.position;
            Vector3 a = cam.WorldToScreenPoint(o + hf * 5000f - hr * 1000f), b = cam.WorldToScreenPoint(o + hf * 5000f + hr * 1000f);
            if (a.z <= 0f || b.z <= 0f) return f.center;
            Vector2 pa = new Vector2(a.x, f.height - a.y), pb = new Vector2(b.x, f.height - b.y);
            Vector2 dir = (pb - pa).normalized;
            if (dir.x < 0f) dir = -dir;
            if (dir.x < 0.05f) return f.center; // banked past vertical: no room between the tapes
            // The symbol's spot: where the line crosses the centre column; the line runs between the tapes from there.
            Vector2 anchor = pa + dir * ((f.center.x - pa.x) / dir.x);
            float lo = leftTape + f.width * 0.01f, hi = rightTape - f.width * 0.01f;
            float back = (anchor.x - lo) / dir.x, ahead = (hi - anchor.x) / dir.x;
            // Banked, the ends also stop at the tapes' height (top to bottom of the AIRSPD / ALT scales).
            if (Mathf.Abs(dir.y) > 1e-3f)
            {
                float down = dir.y > 0f ? (bottom - anchor.y) / dir.y : (top - anchor.y) / dir.y; // along +dir
                float up = dir.y > 0f ? (anchor.y - top) / dir.y : (anchor.y - bottom) / dir.y;   // along -dir
                ahead = Mathf.Min(ahead, down);
                back = Mathf.Min(back, up);
            }
            float clamped = Mathf.Clamp(anchor.y, top, bottom);
            // On the line the wings stand apart from it; off the line (horizon out of the tapes' span) no gap.
            float gap = Mathf.Approximately(clamped, anchor.y) ? f.height * SymbolRadius * 6f : 0f;
            float thin = Mathf.Max(1.5f, f.height * 0.002f);
            if (back > gap) Line(anchor - dir * back, anchor - dir * gap, thin, Green);
            if (ahead > gap) Line(anchor + dir * gap, anchor + dir * ahead, thin, Green);
            return new Vector2(f.center.x, clamped);
        }

        /// <summary>Ring radius of the aircraft symbol, share of the screen height.</summary>
        const float SymbolRadius = 0.01f;

        /// <summary>
        /// Aircraft symbol, the drone's attitude now: a hollow ring, a short tick above it and two short wings either side,
        /// level with the airframe (the camera looks along it), sitting on the long line (see <see cref="Horizon"/>).
        /// </summary>
        static void AircraftSymbol(Vector2 c, float h)
        {
            float r = h * SymbolRadius, thin = Mathf.Max(1.5f, h * 0.002f);
            Crosshair.Ring(c, r, thin, Green, false);
            Line(c + Vector2.down * r, c + Vector2.down * r * 2.2f, thin, Green); // GUI y grows down: the tick rises
            Line(c + Vector2.left * r * 1.8f, c + Vector2.left * r * 4.5f, thin, Green);
            Line(c + Vector2.right * r * 1.8f, c + Vector2.right * r * 4.5f, thin, Green);
        }

        /// <summary>How far ahead the commanded-direction cross looks, s.</summary>
        const float CommandLead = 1f;

        /// <summary>
        /// Small cross where the operator is steering: the nose <see cref="CommandLead"/> ahead at the body rates the flight
        /// controller is commanding (centred on the symbol when the sticks hold the attitude). The symbol and the line
        /// show how the drone flies, the cross where it has been told to go.
        /// </summary>
        static void CommandMarker(Camera cam, DroneController drone, float h)
        {
            if (!drone.Armed || drone.FlightController == null) return;
            var rate = drone.FlightController.TargetRate * (CommandLead * Mathf.Rad2Deg); // body: x pitch nose-down +, y yaw right +
            var dir = drone.transform.rotation * Quaternion.Euler(rate.x, rate.y, 0f) * Vector3.forward;
            var s = cam.WorldToScreenPoint(cam.transform.position + dir * 1000f);
            if (s.z <= 0f) return;
            var p = new Vector2(s.x, h - s.y);
            float k = h * 0.007f, thin = Mathf.Max(1.5f, h * 0.0018f);
            Line(p + new Vector2(-k, -k), p + new Vector2(k, k), thin, Green);
            Line(p + new Vector2(-k, k), p + new Vector2(k, -k), thin, Green);
        }

        /// <summary>Vertical tape: ticks every unit, labels every 5, the current value boxed at the centre.</summary>
        static void Tape(float x, float midY, float half, float value, float window, bool left, string header, float refLine)
        {
            float px = half / window, side = left ? -1f : 1f, thin = Mathf.Max(1f, half * 0.006f);
            Line(new Vector2(x, midY - half), new Vector2(x, midY + half), thin, left ? Orange : Green);
            int lo = Mathf.CeilToInt(value - window), hi = Mathf.FloorToInt(value + window);
            for (int v = lo; v <= hi; v++)
            {
                float y = midY - (v - value) * px;
                bool major = v % 5 == 0;
                Line(new Vector2(x, y), new Vector2(x + side * (major ? half * 0.06f : half * 0.03f), y), thin, Green);
                if (major && Mathf.Abs(y - midY) > _value.fontSize * 0.9f)
                    Text(new Rect(left ? x - half * 0.08f - 80f : x + half * 0.08f, y - _small.fontSize, 80f, _small.fontSize * 2f),
                        v.ToString(), _small, left && v >= 45 ? Orange : Green, left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            }
            Text(new Rect(left ? x - 100f : x - 20f, midY - half - _small.fontSize * 2.8f, 120f, _small.fontSize * 2.6f), header, _small, Green,
                left ? TextAnchor.LowerRight : TextAnchor.LowerLeft);

            // Current value: boxed number on the outer side, pointer and a reference line reaching into the picture.
            string s = Mathf.RoundToInt(value).ToString();
            float bw = _value.CalcSize(new GUIContent(s)).x + 12f, bh = _value.fontSize * 1.35f;
            var box = new Rect(left ? x - half * 0.05f - bw : x + half * 0.05f, midY - bh * 0.5f, bw, bh);
            var c = left ? Orange : Green;
            Text(box, s, _value, c, TextAnchor.MiddleCenter);
            Line(new Vector2(box.xMin, box.yMin), new Vector2(box.xMax, box.yMin), thin, c);
            Line(new Vector2(box.xMin, box.yMax), new Vector2(box.xMax, box.yMax), thin, c);
            float tip = left ? box.xMax + half * 0.04f : box.xMin - half * 0.04f;
            Line(new Vector2(left ? box.xMax : box.xMin, box.yMin), new Vector2(tip, midY), thin, c);
            Line(new Vector2(left ? box.xMax : box.xMin, box.yMax), new Vector2(tip, midY), thin, c);
            Line(new Vector2(x - side * half * 0.02f, midY), new Vector2(x - side * refLine, midY), thin, Green);
        }

        /// <summary>Compass tape across the top: ticks every 5 degrees, cardinal names every 45, a marker on a bearing below.</summary>
        static void Compass(Rect f, float heading, float? markerBearing)
        {
            float y = f.y + f.height * 0.1f, halfW = f.width * 0.2f, span = 50f, px = halfW / span, thin = Mathf.Max(1f, f.height * 0.0015f);
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int d = Mathf.CeilToInt((heading - span) / 5f) * 5; d <= heading + span; d += 5)
            {
                float x = f.center.x + Mathf.DeltaAngle(heading, d) * px;
                int deg = ((d % 360) + 360) % 360;
                bool cardinal = deg % 45 == 0;
                Line(new Vector2(x, y), new Vector2(x, y + (cardinal ? f.height * 0.018f : deg % 15 == 0 ? f.height * 0.011f : f.height * 0.006f)), thin, Green);
                if (cardinal)
                    Text(new Rect(x - 40f, y - _small.fontSize * 2.1f, 80f, _small.fontSize * 2f), names[deg / 45], _small, Green, TextAnchor.LowerCenter);
            }
            Line(new Vector2(f.center.x, y - f.height * 0.004f), new Vector2(f.center.x, y + f.height * 0.026f), thin * 2f, Green);
            if (markerBearing is float b)
            {
                float dx = Mathf.Clamp(Mathf.DeltaAngle(heading, b) * px, -halfW, halfW);
                var p = new Vector2(f.center.x + dx, y + f.height * 0.05f);
                float s = f.height * 0.012f;
                Line(p + new Vector2(-s, -s), p, thin * 2f, Blue);
                Line(p + new Vector2(s, -s), p, thin * 2f, Blue);
                Line(p + new Vector2(-s, -s), p + new Vector2(s, -s), thin * 2f, Blue);
            }
        }

        /// <summary>Horizontal scale with a dot (here the throttle, 0-100 %).</summary>
        static void ThrottleScale(Vector2 origin, float length, float throttle)
        {
            float thin = Mathf.Max(1f, length * 0.003f), tick = _small.fontSize * 0.5f;
            Line(origin, origin + Vector2.right * length, thin, Green);
            for (int i = 0; i <= 4; i++)
            {
                var p = origin + Vector2.right * length * i / 4f;
                Line(p, p + Vector2.up * tick, thin, Green);
                Text(new Rect(p.x - 40f, p.y + tick, 80f, _small.fontSize * 1.6f), (i * 25).ToString(), _small, Green, TextAnchor.UpperCenter);
            }
            Text(new Rect(origin.x - 90f, origin.y - _small.fontSize, 80f, _small.fontSize * 2f), "THR", _small, Green, TextAnchor.MiddleRight);
            var dot = origin + Vector2.right * length * Mathf.Clamp01(throttle);
            Box(new Rect(dot.x - tick * 0.7f, dot.y - tick * 0.7f, tick * 1.4f, tick * 1.4f), Blue);
        }

        /// <summary>AI target lock: a box and a "number | class" tag on each live mission target in view.</summary>
        static void Targets(Camera cam, Rect f)
        {
            float thin = Mathf.Max(1.5f, f.height * 0.0022f);
            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                if (target.Model == null || target.Neutralized) continue;
                var bounds = target.HullBounds; // cached per frame on the target
                Vector3 to = bounds.center - cam.transform.position;
                if (to.sqrMagnitude > RecognitionRange * RecognitionRange || Vector3.Dot(to, cam.transform.forward) <= 0f) continue;
                if (!target.InSightFrom(cam.transform.position)) continue;
                if (!ScreenRect(cam, bounds, f.height, out var r)) continue;
                float min = f.height * 0.018f;
                r = new Rect(r.center.x - Mathf.Max(r.width, min) * 0.5f, r.center.y - Mathf.Max(r.height, min) * 0.5f, Mathf.Max(r.width, min), Mathf.Max(r.height, min));
                Brackets(r, thin);
                string tag = Tag(target, i);
                TagContent.text = tag;
                float tw = _small.CalcSize(TagContent).x + 10f, th = _small.fontSize * 1.4f;
                var tr = new Rect(r.center.x - tw * 0.3f, r.yMin - th - 4f, tw, th);
                Box(tr, TagBack);
                Text(tr, tag, _small, Orange, TextAnchor.MiddleCenter);
            }
        }

        static void Brackets(Rect r, float thin)
        {
            float k = Mathf.Min(r.width, r.height) * 0.35f;
            Corner(new Vector2(r.xMin, r.yMin), k, k, thin);
            Corner(new Vector2(r.xMax, r.yMin), -k, k, thin);
            Corner(new Vector2(r.xMin, r.yMax), k, -k, thin);
            Corner(new Vector2(r.xMax, r.yMax), -k, -k, thin);
        }

        static void Corner(Vector2 corner, float dx, float dy, float thin)
        {
            Line(corner, corner + new Vector2(dx, 0f), thin, Lock);
            Line(corner, corner + new Vector2(0f, dy), thin, Lock);
        }

        static readonly GUIContent TagContent = new GUIContent();

        /// <summary>"number | class" tag, built once per target (no string formatting every frame).</summary>
        static string Tag(MissionTarget target, int index)
        {
            if (Classes.TryGetValue(target, out var tag)) return tag;
            return Classes[target] = $"{index + 1} | {Class(target)}";
        }

        /// <summary>Class code from what the vehicle carries (the recognition classes are illustrative).</summary>
        static string Class(MissionTarget target)
        {
            string c;
            bool radar = false, launcher = false, gun = false, rotor = false, tracks = false;
            foreach (var m in target.Model.Modules)
            {
                radar |= m.Type == ModuleType.Radar;
                launcher |= m.Type == ModuleType.Launcher;
                gun |= m.Type == ModuleType.Gun;
                rotor |= m.Type == ModuleType.Rotor;
                tracks |= m.Type == ModuleType.Track;
            }
            c = rotor ? "AIR/HEL" : launcher && gun ? "VEH/AD" : launcher ? "VEH/SAM" : radar ? "RAD" : tracks || gun ? "VEH/ARM" : "VEH";
            return c;
        }

        static bool ScreenRect(Camera cam, Bounds b, float h, out Rect r)
        {
            Vector3 c = b.center, e = b.extents;
            int n = 0;
            for (int i = 0; i < 8; i++)
                Corners[n++] = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in Corners)
            {
                var s = cam.WorldToScreenPoint(p);
                if (s.z <= 0f) { r = default; return false; }
                x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x);
                y0 = Mathf.Min(y0, h - s.y); y1 = Mathf.Max(y1, h - s.y);
            }
            r = Rect.MinMaxRect(x0, y0, x1, y1);
            return x1 > 0f && x0 < Screen.width && y1 > 0f && y0 < h;
        }

        /// <summary>Bearing from the drone to the middle of the live targets (the programmed target area), deg.</summary>
        static float? TargetBearing(DroneController drone)
        {
            if (!TargetArea(out var area)) return null;
            var d = area - drone.transform.position;
            return (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
        }

        /// <summary>Middle of the live mission targets (the programmed target area).</summary>
        static bool TargetArea(out Vector3 area)
        {
            area = Vector3.zero;
            if (_targets == null) return false;
            int n = 0;
            foreach (var target in _targets)
                if (target != null && target.Model != null && !target.Neutralized) { area += target.transform.position; n++; }
            if (n == 0) return false;
            area /= n;
            return true;
        }

        static void Line(Vector2 a, Vector2 b, float thickness, Color color)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            var prev = GUI.color;
            GUI.color = Shadow;
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f + 1f, len, thickness + 1f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.matrix = matrix;
        }

        static void Box(Rect r, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static void Text(Rect r, string text, GUIStyle style, Color color, TextAnchor anchor)
        {
            if (string.IsNullOrEmpty(text)) return;
            style.alignment = anchor;
            style.normal.textColor = Shadow;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        static void EnsureStyles()
        {
            if (_small != null && _styleHeight == Screen.height) return;
            _styleHeight = Screen.height;
            int size = Mathf.Max(11, Mathf.RoundToInt(Screen.height * 0.022f));
            _small = new GUIStyle(GUI.skin.label) { font = UiTheme.MonoBold, fontSize = size, clipping = TextClipping.Overflow, wordWrap = false };
            _value = new GUIStyle(_small) { fontSize = Mathf.RoundToInt(size * 1.35f) };
        }
    }
}
