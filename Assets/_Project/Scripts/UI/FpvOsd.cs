using System.Globalization;
using ModernDrone.Drone;
using ModernDrone.Video;
using UnityEngine;

namespace ModernDrone.UI
{
    /// <summary>
    /// Betaflight-style on-screen display drawn with IMGUI, modelled on the Betaflight OSD: a monospaced character grid
    /// (30 x 16 on PAL, glyphs 12 x 18 px on a 720 x 576 frame), white glyphs outlined in black, flight modes AIR/ANGL
    /// and !FS!, the craft name upper case, a battery symbol that empties with the cell voltage, blinking centre
    /// warnings (FAIL SAFE, LINK QUALITY, LAND NOW, LOW BATTERY) and a short ARMED message on arming
    /// (github.com/betaflight/betaflight src/main/osd: osd_elements.c, osd_warnings.c, osd.c).
    /// </summary>
    public sealed class FpvOsd : MonoBehaviour
    {
        [SerializeField] DroneController drone;
        [SerializeField] DroneInput input;
        [Tooltip("Betaflight 'warning' cell voltage (LOW BATTERY).")]
        [SerializeField] float lowCellVoltage = 3.4f;
        [Tooltip("Betaflight 'critical' cell voltage (LAND NOW).")]
        [SerializeField] float criticalCellVoltage = 3.2f;
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

        /// <summary>The camera's frame on screen: 4:3 with side bars when the pilot shows it, else the full screen.</summary>
        static Rect Frame()
        {
            float w = Screen.width, h = Screen.height;
            if (!ControlSettings.Current.frame43) return new Rect(0, 0, w, h);
            float fw = Mathf.Min(w, h * 4f / 3f);
            return new Rect((w - fw) * 0.5f, 0, fw, h);
        }

        // Text lines of one slot; icon >= 0 draws a battery symbol filled to that level in front of the text.
        readonly System.Collections.Generic.List<(string text, Color color, float icon)> _lines = new System.Collections.Generic.List<(string, Color, float)>();
        bool _wasArmed;
        float _armedAt = -10f;

        /// <summary>OSD elements blink at about 2 Hz, like Betaflight's blink mask.</summary>
        static bool BlinkOn => Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.33f;

        void Draw()
        {
            EnsureStyles();

            var t = drone.Telemetry;
            var osd = ControlSettings.Current.osd;
            bool aircraft = drone.Config != null && drone.Config.fixedWing;
            var f = aircraft ? new Rect(0f, 0f, Screen.width, Screen.height) : Frame();
            float h = f.height;
            float pad = h * 0.05f;
            float line = _style.fontSize * 1.3f;
            bool lowBat = t.cellVoltage < lowCellVoltage;

            // An aeroplane (Hornet) has its own aircraft-style HUD instead of the Betaflight elements.
            if (aircraft) HornetHud.Draw(drone, _fpvCamera, f);
            // Text elements, stacked in their slots in list order.
            if (!aircraft)
            foreach (OsdSlot slot in System.Enum.GetValues(typeof(OsdSlot)))
            {
                if (slot == OsdSlot.Off) continue;
                _lines.Clear();
                foreach (var e in osd.entries)
                    if (e.slot == slot) Text(e.item, t, lowBat, osd);
                if (_lines.Count == 0) continue;
                bool top = slot == OsdSlot.TopLeft || slot == OsdSlot.TopCenter || slot == OsdSlot.TopRight;
                bool bottom = slot == OsdSlot.BottomLeft || slot == OsdSlot.BottomCenter || slot == OsdSlot.BottomRight;
                bool left = slot == OsdSlot.TopLeft || slot == OsdSlot.MiddleLeft || slot == OsdSlot.BottomLeft;
                bool right = slot == OsdSlot.TopRight || slot == OsdSlot.MiddleRight || slot == OsdSlot.BottomRight;
                float y0 = top ? f.y + pad : bottom ? f.yMax - pad - line * _lines.Count : f.y + h * 0.5f - line * _lines.Count * 0.5f;
                float x = left ? f.x + pad : right ? f.xMax - pad - 400f : f.center.x - 250f;
                float width = left || right ? 400f : 500f;
                var anchor = left ? TextAnchor.MiddleLeft : right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                // Middle slots sit in from the edge like Betaflight's speed / altitude columns.
                if (slot == OsdSlot.MiddleLeft) x = f.x + f.width * 0.12f;
                if (slot == OsdSlot.MiddleRight) x = f.xMax - f.width * 0.12f - 400f;
                for (int k = 0; k < _lines.Count; k++)
                {
                    var (text, color, icon) = _lines[k];
                    var r = new Rect(x, y0 + k * line, width, line);
                    if (icon >= 0f)
                    {
                        // The symbol sits on the text's leading side.
                        float tw = _style.CalcSize(new GUIContent(text)).x, iw = line * 0.45f;
                        float tx = anchor == TextAnchor.MiddleLeft ? r.x + iw * 1.6f : anchor == TextAnchor.MiddleRight ? r.xMax - tw : r.center.x - tw * 0.5f + iw * 0.8f;
                        BatteryIcon(new Rect(tx - iw * 1.6f, r.y + line * 0.2f, iw, line * 0.62f), icon, color);
                        if (anchor == TextAnchor.MiddleLeft) r.x += iw * 1.6f;
                        else if (anchor == TextAnchor.MiddleCenter) r.x += iw * 0.8f;
                    }
                    Label(r, text, color, anchor);
                }
            }

            if (!aircraft)
            {
                if (osd.crosshair) Crosshair.Draw(new Vector2(f.center.x, f.center.y + osd.crosshairOffset * 0.01f * h), h * 0.02f, osd);
                if (osd.chargeAxis) DrawChargeAxis(h);
                if (osd.horizon) DrawHorizon(f, t);
                if (osd.sidebars) DrawSidebars(f);
            }
            if (t.armed && !t.crashed) DrawThreat(f.width, h, f.x);

            // "ARMED" for half a second on arming (Betaflight logo_on_arming_duration default 5 = 0.5 s).
            if (t.armed && !_wasArmed) _armedAt = Time.unscaledTime;
            _wasArmed = t.armed;
            bool armedMsg = Time.unscaledTime - _armedAt < 0.5f;
            bool landNow = t.cellVoltage < criticalCellVoltage;

            // Centre warnings
            var source = input != null ? input.ActiveSource : InputSource.KeyboardMouse;
            var linkNow = VideoFeed.HasLink ? VideoFeed.Current : default;
            string crash = input != null && !input.AllowReset ? "CRASH" : $"CRASH  [{ControlInput.HintFor(PilotAction.Reset, source)}]";
            // Arming and crash prompts always show; the rest follow the warnings switch.
            string warning = aircraft && !t.armed && !t.crashed ? null // waiting at the launch point: no arming prompts
                : armedMsg ? "ARMED"
                : t.crashed ? crash
                : drone.ArmBlock == ArmBlockReason.Throttle ? "ARM BLOCKED: THROTTLE"
                : drone.ArmBlock == ArmBlockReason.Failsafe ? "ARM BLOCKED: FAILSAFE"
                : !t.armed ? $"DISARMED  [{ControlInput.HintFor(PilotAction.Arm, source)}]"
                : !osd.warnings ? null
                // Betaflight warning texts, blinking.
                : linkNow.cableCut ? (BlinkOn ? "CABLE CUT" : "")
                : drone.Failsafe ? (BlinkOn ? "FAIL SAFE" : "")
                : VideoFeed.HasLink && linkNow.control < 0.3f ? (BlinkOn ? "LINK QUALITY" : "")
                : landNow ? (BlinkOn ? "LAND NOW" : "")
                : lowBat ? (BlinkOn ? "LOW BATTERY" : "") : null;
            if (warning != null)
            {
                var r = new Rect(f.x, h * 0.62f, f.width, _big.fontSize * 1.4f);
                Shadowed(r, warning, _big, t.crashed || lowBat || drone.Failsafe || linkNow.cableCut || (VideoFeed.HasLink && linkNow.control < 0.3f) || drone.ArmBlock != ArmBlockReason.None ? Color.red : textColor);
            }
        }

        /// <summary>Lines one OSD element contributes to its slot.</summary>
        void Text(OsdItem item, in DroneTelemetry t, bool lowBat, OsdSettings osd)
        {
            switch (item)
            {
                // Airmode is on by default in Betaflight, so acro reads AIR; !FS! replaces the mode in failsafe.
                case OsdItem.FlightMode: _lines.Add((drone.Failsafe ? "!FS!" : t.mode == FlightMode.Acro ? "AIR" : "ANGL", textColor, -1f)); break;
                case OsdItem.InputSource:
                    _lines.Add(((input != null ? SourceName(input.ActiveSource) : "") + (ModernDrone.Core.NightMode.Thermal ? "  TIR" : ""), textColor, -1f));
                    break;
                case OsdItem.Link:
                    if (!VideoFeed.HasLink) break;
                    var link = VideoFeed.Current;
                    _lines.Add(($"LQ {link.control * 100f:0}  VID {link.video * 100f:0}", link.control < 0.3f ? Color.red : textColor, -1f));
                    if (link.type == LinkType.Fiber)
                        _lines.Add(($"SPOOL {link.cableUsed / 1000f:0.00}/{link.spoolLength / 1000f:0.0}km", link.cableUsed > link.spoolLength * 0.85f ? Color.red : textColor, -1f));
                    else if (link.jam > 0.05f)
                        _lines.Add(($"EW {link.jam * 100f:0}%", Color.red, -1f));
                    break;
                case OsdItem.Timer: _lines.Add((FormatTime(t.flightTime), textColor, -1f)); break;
                case OsdItem.Heading: _lines.Add(($"{Compass(t.headingDeg)} {t.headingDeg:000}", textColor, -1f)); break;
                case OsdItem.Voltage:
                    _lines.Add(($"{t.voltage:0.0}V {t.cellVoltage:0.00}", lowBat ? Color.red : textColor, Mathf.InverseLerp(criticalCellVoltage, 4.2f, t.cellVoltage)));
                    break;
                case OsdItem.Current: _lines.Add(($"{t.current:0}A", textColor, -1f)); break;
                case OsdItem.UsedMah: _lines.Add(($"{t.usedMah:0}mAh", textColor, -1f)); break;
                case OsdItem.Throttle: _lines.Add(($"THR {t.throttle * 100f:0}", textColor, -1f)); break;
                case OsdItem.Speed: _lines.Add(($"{t.speed * 3.6f:0}km/h", textColor, -1f)); break;
                case OsdItem.Altitude: _lines.Add((t.altitudeAgl >= 0f ? $"{t.altitudeAgl:0.0}m" : $"{t.heightAboveHome:0}m", textColor, -1f)); break;
                case OsdItem.VerticalSpeed: _lines.Add(($"{t.verticalSpeed:+0.0;-0.0}m/s", textColor, -1f)); break;
                case OsdItem.HomeDistance: _lines.Add(($"H {t.distanceHome:0}m", textColor, -1f)); break;
                case OsdItem.CraftName: _lines.Add((osd.craftName.ToUpperInvariant(), textColor, -1f)); break;
            }
        }

        /// <summary>Interceptor warning with an on-screen marker or edge arrow (no detection warning: the pilot cannot tell when they have been spotted).</summary>
        void DrawThreat(float w, float h, float x0)
        {
            float y = h * 0.2f;
            if (ModernDrone.Core.InterceptorWarning.RecentMiss)
                Shadowed(new Rect(x0, y - _big.fontSize * 1.6f, w, _big.fontSize * 1.4f), "ПРОМАХ ПЕРЕХВАТЧИКА", _big, new Color(0.4f, 1f, 0.4f));
            if (ModernDrone.Core.InterceptorWarning.Active)
            {
                float dist = ModernDrone.Core.InterceptorWarning.Distance;
                bool close = dist < 60f;
                if (!close || Mathf.Repeat(Time.unscaledTime, 0.3f) < 0.2f)
                    Shadowed(new Rect(x0, y, w, _big.fontSize * 1.4f), $"ПЕРЕХВАТЧИК {dist:0}м", _big, close ? Color.red : new Color(1f, 0.6f, 0.2f));
                DrawInterceptorMarker(w, h, ModernDrone.Core.InterceptorWarning.Position);
            }
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
            Crosshair.Ring(c, r, thick, textColor, true);
            Crosshair.Ring(c, thick * 0.6f, 0f, textColor, true);
        }

        /// <summary>Betaflight battery symbol: an outlined cell that empties as the voltage falls.</summary>
        void BatteryIcon(Rect r, float level, Color color)
        {
            float t = Mathf.Max(1f, r.height * 0.1f);
            Outlined(new Rect(r.x + r.width * 0.3f, r.y - t * 1.5f, r.width * 0.4f, t * 1.5f), color);
            Outlined(new Rect(r.x, r.y, r.width, t), color);
            Outlined(new Rect(r.x, r.yMax - t, r.width, t), color);
            Outlined(new Rect(r.x, r.y, t, r.height), color);
            Outlined(new Rect(r.xMax - t, r.y, t, r.height), color);
            float fill = (r.height - 4f * t) * Mathf.Clamp01(level);
            Outlined(new Rect(r.x + 2f * t, r.yMax - 2f * t - fill, r.width - 4f * t, fill), color);
        }

        static void Outlined(Rect r, Color color)
        {
            if (r.height <= 0f || r.width <= 0f) return;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>Betaflight horizon sidebars: dashed vertical scales 3 characters above and below the centre, with arrows.</summary>
        void DrawSidebars(Rect f)
        {
            float cell = f.height / 16f, cx = f.center.x, cy = f.center.y;
            float dx = f.width * 7f / 30f; // 7 of the 30 columns out from the centre
            float thick = Mathf.Max(2f, f.height * 0.003f);
            foreach (float side in new[] { -1f, 1f })
            {
                float x = cx + side * dx;
                for (int i = -3; i <= 3; i++)
                {
                    if (i == 0) continue;
                    float y = cy + i * cell;
                    Line(new Vector2(x, y - cell * 0.3f), new Vector2(x, y + cell * 0.3f), thick);
                }
                // Centre arrow pointing in towards the crosshair.
                Line(new Vector2(x, cy), new Vector2(x + side * cell * 0.4f, cy - cell * 0.25f), thick);
                Line(new Vector2(x, cy), new Vector2(x + side * cell * 0.4f, cy + cell * 0.25f), thick);
            }
        }

        /// <summary>
        /// Artificial horizon as Betaflight draws it: a row of dots that rolls with the frame and shifts with pitch
        /// (camera tilt ignored), clamped to the middle of the picture.
        /// </summary>
        void DrawHorizon(Rect f, in DroneTelemetry t)
        {
            float h = f.height;
            float pxPerDeg = h / 90f;
            float offset = Mathf.Clamp(-t.pitchDeg * pxPerDeg, -h * 0.3f, h * 0.3f);
            Vector2 c = new Vector2(f.center.x, f.y + h * 0.5f + offset);
            float half = f.width * 0.22f;
            float ang = -t.rollDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float dot = Mathf.Max(3f, h * 0.007f);
            const int Dots = 9;
            for (int i = 0; i < Dots; i++)
            {
                float u = Mathf.Lerp(-1f, 1f, i / (float)(Dots - 1));
                if (Mathf.Abs(u) < 0.12f) continue; // gap for the crosshair
                var p = c + dir * half * u;
                Line(p - dir * dot, p + dir * dot, dot);
            }
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
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 18f / 576f * 0.95f));
            _style = new GUIStyle(GUI.skin.label) { font = ModernDrone.Core.UiTheme.MonoBold, fontSize = size, clipping = TextClipping.Overflow };
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
