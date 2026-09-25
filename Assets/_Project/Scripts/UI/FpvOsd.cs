using System.Globalization;
using ModernDrone.Drone;
using ModernDrone.Video;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// Betaflight-style on-screen display drawn with IMGUI. Prototype-level: will move to UI Toolkit later.
    /// </summary>
    public sealed class FpvOsd : MonoBehaviour
    {
        [SerializeField] DroneController drone;
        [SerializeField] DroneInput input;
        [SerializeField] float lowCellVoltage = 3.4f;
        [SerializeField] Color textColor = Color.white;

        Camera _fpvCamera;
        GUIStyle _style;
        GUIStyle _big;
        int _styleHeight;

        void Reset()
        {
            drone = GetComponentInParent<DroneController>();
            input = GetComponentInParent<DroneInput>();
        }

        void OnGUI()
        {
            if (drone == null || ModernDrone.Core.GamePause.IsPaused) return;
            if (_fpvCamera == null) _fpvCamera = drone.GetComponentInChildren<Camera>();
            // The OSD belongs to the video feed: hide it when another camera (killcam) has taken over.
            if (_fpvCamera != null && !_fpvCamera.enabled) return;
            // OSD numbers use '.' as in Betaflight, regardless of system locale.
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try { Draw(); }
            finally { CultureInfo.CurrentCulture = culture; }
        }

        void Draw()
        {
            EnsureStyles();

            var t = drone.Telemetry;
            float w = Screen.width, h = Screen.height;
            float pad = h * 0.04f;
            float line = _style.fontSize * 1.3f;

            // Corners
            Label(new Rect(pad, pad, 400, line), t.mode == FlightMode.Acro ? "ACRO" : "ANGL");
            Label(new Rect(pad, pad + line, 400, line), (input != null ? SourceName(input.ActiveSource) : "") + (ModernDrone.Core.NightMode.Thermal ? "  TIR" : ""));
            if (VideoFeed.HasLink)
            {
                var link = VideoFeed.Current;
                bool lowLq = link.control < 0.3f;
                Label(new Rect(pad, pad + line * 2, 500, line), $"LQ {link.control * 100f:0}  VID {link.video * 100f:0}", lowLq ? Color.red : textColor);
                if (link.type == LinkType.Fiber)
                    Label(new Rect(pad, pad + line * 3, 500, line), $"SPOOL {link.cableUsed / 1000f:0.00}/{link.spoolLength / 1000f:0.0}km",
                        link.cableUsed > link.spoolLength * 0.85f ? Color.red : textColor);
                else if (link.jam > 0.05f)
                    Label(new Rect(pad, pad + line * 3, 500, line), $"EW {link.jam * 100f:0}%", Color.red);
            }
            LabelRight(new Rect(w - pad - 400, pad, 400, line), FormatTime(t.flightTime));
            Label(new Rect(w * 0.5f - 150, pad, 300, line), $"{Compass(t.headingDeg)} {t.headingDeg:000}", textColor, TextAnchor.MiddleCenter);

            bool lowBat = t.cellVoltage < lowCellVoltage;
            Label(new Rect(pad, h - pad - line * 2, 400, line), $"{t.voltage:0.0}V  {t.cellVoltage:0.00}v", lowBat ? Color.red : textColor);
            Label(new Rect(pad, h - pad - line, 400, line), $"{t.current:0}A");
            LabelRight(new Rect(w - pad - 400, h - pad - line * 2, 400, line), $"{t.usedMah:0}mAh");
            LabelRight(new Rect(w - pad - 400, h - pad - line, 400, line), $"THR {t.throttle * 100f:0}%");

            // Side columns: speed left, altitude right
            Label(new Rect(w * 0.18f, h * 0.5f - line * 0.5f, 300, line), $"{t.speed * 3.6f:0}km/h");
            string alt = t.altitudeAgl >= 0f ? $"{t.altitudeAgl:0.0}m" : $"{t.heightAboveHome:0}m";
            LabelRight(new Rect(w * 0.82f - 300, h * 0.5f - line * 0.5f, 300, line), alt);
            LabelRight(new Rect(w * 0.82f - 300, h * 0.5f + line * 0.5f, 300, line), $"{t.verticalSpeed:+0.0;-0.0}m/s");
            Label(new Rect(w * 0.5f - 150, h - pad - line, 300, line), $"H {t.distanceHome:0}m", textColor, TextAnchor.MiddleCenter);

            DrawCrosshair(w * 0.5f, h * 0.5f, h * 0.02f);
            DrawChargeAxis(h);
            if (t.armed && !t.crashed) DrawThreat(w, h);
            DrawHorizon(w, h, t);

            // Centre warnings
            var source = input != null ? input.ActiveSource : InputSource.KeyboardMouse;
            var linkNow = VideoFeed.HasLink ? VideoFeed.Current : default;
            string crash = input != null && !input.AllowReset ? "CRASH" : $"CRASH  [{ControlInput.HintFor(PilotAction.Reset, source)}]";
            string warning = linkNow.cableCut ? "CABLE CUT"
                : drone.Failsafe ? "FAILSAFE"
                : t.crashed ? crash
                : drone.ArmBlock == ArmBlockReason.Throttle ? "ARM BLOCKED: THROTTLE"
                : drone.ArmBlock == ArmBlockReason.Failsafe ? "ARM BLOCKED: FAILSAFE"
                : !t.armed ? $"DISARMED  [{ControlInput.HintFor(PilotAction.Arm, source)}]"
                : lowBat ? "LOW BATTERY"
                : VideoFeed.HasLink && linkNow.control < 0.3f ? "LOW LQ" : null;
            if (warning != null)
            {
                var r = new Rect(0, h * 0.62f, w, _big.fontSize * 1.4f);
                Shadowed(r, warning, _big, t.crashed || lowBat || drone.Failsafe || linkNow.cableCut || (VideoFeed.HasLink && linkNow.control < 0.3f) || drone.ArmBlock != ArmBlockReason.None ? Color.red : textColor);
            }
        }

        /// <summary>Enemy observation (detection bar) and interceptor warning with an on-screen marker or edge arrow.</summary>
        void DrawThreat(float w, float h)
        {
            float y = h * 0.2f;
            if (ModernDrone.Core.ThreatWarning.UnderGroundFire && Mathf.Repeat(Time.unscaledTime, 0.3f) < 0.2f)
                Shadowed(new Rect(0, h * 0.3f, w, _big.fontSize * 1.4f), "ОБСТРЕЛ С ЗЕМЛИ", _big, Color.red);
            if (ModernDrone.Core.InterceptorWarning.RecentMiss)
                Shadowed(new Rect(0, y - _big.fontSize * 1.6f, w, _big.fontSize * 1.4f), "ПРОМАХ ПЕРЕХВАТЧИКА", _big, new Color(0.4f, 1f, 0.4f));
            if (ModernDrone.Core.InterceptorWarning.Active)
            {
                float dist = ModernDrone.Core.InterceptorWarning.Distance;
                bool close = dist < 60f;
                if (!close || Mathf.Repeat(Time.unscaledTime, 0.3f) < 0.2f)
                    Shadowed(new Rect(0, y, w, _big.fontSize * 1.4f), $"ПЕРЕХВАТЧИК {dist:0}м", _big, close ? Color.red : new Color(1f, 0.6f, 0.2f));
                DrawInterceptorMarker(w, h, ModernDrone.Core.InterceptorWarning.Position);
                return;
            }

            float detection = ModernDrone.Core.ThreatWarning.Lock;
            if (detection <= 0f) return;
            Shadowed(new Rect(0, y, w, _big.fontSize * 1.4f), "ОБНАРУЖЕН", _big, detection > 0.7f ? Color.red : new Color(1f, 0.6f, 0.2f));
            float bw = w * 0.2f, bh = Mathf.Max(4f, h * 0.008f);
            var bar = new Rect(w * 0.5f - bw * 0.5f, y + _big.fontSize * 1.5f, bw, bh);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * detection, bar.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void DrawInterceptorMarker(float w, float h, Vector3 world)
        {
            if (_fpvCamera == null) return;
            Vector3 sp = _fpvCamera.WorldToScreenPoint(world);
            bool behind = sp.z < 0f;
            var guiPos = new Vector2(sp.x, h - sp.y);
            float size = h * 0.025f;
            bool onScreen = !behind && guiPos.x > 0 && guiPos.x < w && guiPos.y > 0 && guiPos.y < h;
            if (onScreen)
            {
                // Diamond around the interceptor.
                Line(guiPos + new Vector2(0, -size), guiPos + new Vector2(size, 0), 2f);
                Line(guiPos + new Vector2(size, 0), guiPos + new Vector2(0, size), 2f);
                Line(guiPos + new Vector2(0, size), guiPos + new Vector2(-size, 0), 2f);
                Line(guiPos + new Vector2(-size, 0), guiPos + new Vector2(0, -size), 2f);
                return;
            }
            // Off-screen: arrow on a ring around the centre pointing toward it.
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f);
            Vector2 dir = guiPos - c;
            if (behind) dir = -dir;
            if (dir.sqrMagnitude < 1f) dir = Vector2.down;
            dir.Normalize();
            Vector2 tip = c + dir * h * 0.3f;
            Vector2 side = new Vector2(-dir.y, dir.x) * size * 0.7f;
            Line(tip, tip - dir * size * 1.4f + side, 3f);
            Line(tip, tip - dir * size * 1.4f - side, 3f);
        }

        void DrawCrosshair(float cx, float cy, float size)
        {
            float thick = Mathf.Max(2f, size * 0.12f);
            Line(new Vector2(cx - size * 2f, cy), new Vector2(cx - size * 0.6f, cy), thick);
            Line(new Vector2(cx + size * 0.6f, cy), new Vector2(cx + size * 2f, cy), thick);
            Line(new Vector2(cx, cy - size * 0.6f), new Vector2(cx, cy - size * 0.2f), thick);
        }

        /// <summary>
        /// Where the shaped-charge jet will go: the payload points along the frame axis, the camera is tilted up
        /// from it, so the marker sits below the crosshair by the camera tilt.
        /// </summary>
        void DrawChargeAxis(float h)
        {
            if (_fpvCamera == null) return;
            Vector3 sp = _fpvCamera.WorldToScreenPoint(drone.transform.position + drone.transform.forward * 100f);
            if (sp.z <= 0f) return;
            var c = new Vector2(sp.x, h - sp.y);
            float r = h * 0.012f, thick = Mathf.Max(2f, h * 0.0025f);
            const int Segments = 12;
            for (int i = 0; i < Segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / Segments, a1 = (i + 1) * Mathf.PI * 2f / Segments;
                Line(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, thick);
            }
            Line(c + Vector2.down * thick * 0.5f, c + Vector2.up * thick * 0.5f, thick);
        }

        /// <summary>Artificial horizon: rolls with the frame and shifts with pitch (camera tilt ignored).</summary>
        void DrawHorizon(float w, float h, in DroneTelemetry t)
        {
            float pxPerDeg = h / 90f;
            float offset = Mathf.Clamp(-t.pitchDeg * pxPerDeg, -h * 0.3f, h * 0.3f);
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f + offset);
            float half = w * 0.12f;
            float ang = -t.rollDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float thick = Mathf.Max(2f, h * 0.003f);
            Line(c - dir * half, c - dir * half * 0.3f, thick);
            Line(c + dir * half * 0.3f, c + dir * half, thick);
        }

        void Line(Vector2 a, Vector2 b, float thickness)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(a.x - 1f, a.y - thickness * 0.5f - 1f, len + 2f, thickness + 2f), Texture2D.whiteTexture);
            GUI.color = textColor;
            GUI.DrawTexture(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.matrix = matrix;
        }

        void Label(Rect r, string text) => Label(r, text, textColor);

        void Label(Rect r, string text, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            _style.alignment = anchor;
            Shadowed(r, text, _style, color);
        }

        void LabelRight(Rect r, string text) => Label(r, text, textColor, TextAnchor.MiddleRight);

        static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = Color.black;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0)
                    GUI.Label(new Rect(r.x + dx * 2, r.y + dy * 2, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }

        void EnsureStyles()
        {
            if (_style != null && _styleHeight == Screen.height) return;
            _styleHeight = Screen.height;
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.028f));
            _style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            _big = new GUIStyle(_style) { fontSize = Mathf.RoundToInt(size * 1.4f), alignment = TextAnchor.MiddleCenter };
        }

        static string Compass(float deg)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(deg / 45f) % 8];
        }

        static string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        static string SourceName(InputSource source) => source switch
        {
            InputSource.Joystick => "RC",
            InputSource.Gamepad => "PAD",
            InputSource.KeyboardMouse => "KBM",
            _ => ""
        };
    }
}
