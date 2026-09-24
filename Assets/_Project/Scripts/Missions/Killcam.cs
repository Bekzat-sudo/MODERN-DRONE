using System;
using System.Collections;
using ModernDrone.Damage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Post-strike replay camera: a short external view of the impact, then an orbiting x-ray view
    /// of the struck vehicle with module states and strike traces.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class Killcam : MonoBehaviour
    {
        [SerializeField] KillcamAssets assets;
        [SerializeField] float impactPhase = 2.5f;
        [SerializeField] float xrayPhase = 9f;
        [SerializeField] float orbitSpeed = 22f;
        [SerializeField] float orbitRadius = 11f;
        [SerializeField] float orbitHeight = 4.5f;

        Camera _camera;
        AudioListener _listener;
        XRayView _xray;
        StrikeReport _report;
        string _caption;
        GUIStyle _title, _body, _legend, _legendCentered;

        public bool Running { get; private set; }

        public void SetAssets(KillcamAssets a) => assets = a;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _listener = GetComponent<AudioListener>();
            SetActive(false);
        }

        readonly System.Collections.Generic.List<Behaviour> _suspended = new System.Collections.Generic.List<Behaviour>();

        /// <summary>Takes over rendering and audio: other cameras / listeners are paused and restored afterwards.</summary>
        void SetActive(bool active)
        {
            if (active)
            {
                _suspended.Clear();
                foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (cam != _camera && cam.enabled) { cam.enabled = false; _suspended.Add(cam); }
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (l != _listener && l.enabled) { l.enabled = false; _suspended.Add(l); }
            }
            else
            {
                foreach (var b in _suspended)
                    if (b != null) b.enabled = true;
                _suspended.Clear();
            }
            _camera.enabled = active;
            if (_listener != null) _listener.enabled = active;
        }

        public IEnumerator Play(StrikeReport report, Action onDone)
        {
            Running = true;
            _report = report;
            _xray ??= new XRayView(assets);
            SetActive(true);

            Vector3 dir = report.direction.sqrMagnitude > 0f ? report.direction.normalized : Vector3.forward;
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flat.sqrMagnitude < 1e-3f) flat = Vector3.forward;
            flat.Normalize();

            bool hitVehicle = report.stateBefore.Count > 0;
            Vector3 focus = hitVehicle ? VehicleCenter(report) : report.point;

            // Phase 1: external view back along the approach, slow push-in while the explosion plays.
            _caption = hitVehicle ? "ПОПАДАНИЕ" : "ПРОМАХ";
            float t = 0f;
            bool skipped = false;
            yield return null; // swallow the input frame that caused the detonation
            while (t < impactPhase && !(skipped = SkipPressed()))
            {
                float k = t / impactPhase;
                transform.position = report.point - flat * Mathf.Lerp(9f, 6f, k) + Vector3.up * Mathf.Lerp(2.5f, 1.8f, k);
                transform.LookAt(report.point);
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 2: x-ray orbit.
            if (hitVehicle && !skipped)
            {
                _caption = "РЕНТГЕН";
                _xray.Show(report);
                float angle = Mathf.Atan2(-flat.x, -flat.z) * Mathf.Rad2Deg;
                t = 0f;
                yield return null;
                while (t < xrayPhase && !SkipPressed())
                {
                    angle += orbitSpeed * Time.deltaTime;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius;
                    transform.position = focus + offset + Vector3.up * orbitHeight;
                    transform.LookAt(focus);
                    t += Time.deltaTime;
                    yield return null;
                }
                _xray.Hide();
            }

            SetActive(false);
            _report = null;
            Running = false;
            onDone?.Invoke();
        }

        static Vector3 VehicleCenter(StrikeReport report)
        {
            foreach (var vehicle in report.stateBefore.Keys)
            {
                if (vehicle == null) continue;
                var renderers = vehicle.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return vehicle.transform.position;
                var b = renderers[0].bounds;
                foreach (var r in renderers)
                    if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
                return b.center;
            }
            return report.point;
        }

        static bool SkipPressed()
        {
            if (ModernDrone.Core.GamePause.IsPaused) return false;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        void OnDisable() => _xray?.Hide();

        void OnGUI()
        {
            if (!Running || _report == null || ModernDrone.Core.GamePause.IsPaused) return;
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            GUI.Label(new Rect(0, h * 0.05f, w, _title.fontSize * 1.5f), _caption, _title);

            var summary = _report.Summary();
            var rect = new Rect(w * 0.03f, h * 0.14f, w * 0.34f, h * 0.5f);
            float height = _body.CalcHeight(new GUIContent(summary), rect.width);
            Box(new Rect(rect.x - 10, rect.y - 8, rect.width + 20, height + 16));
            GUI.Label(rect, summary, _body);

            if (_xray != null && _xray.Visible)
            {
                float lx = w * 0.72f, ly = h * 0.14f, lh = _legend.fontSize * 1.5f;
                Box(new Rect(lx - 10, ly - 8, w * 0.26f, lh * 7 + 16));
                Legend(lx, ly + lh * 0, lh, DamageSolver.ColorOf(TraceKind.Jet), "Струя — пробитие");
                Legend(lx, ly + lh * 1, lh, DamageSolver.ColorOf(TraceKind.JetStopped), "Струя — остановлена");
                Legend(lx, ly + lh * 2, lh, DamageSolver.ColorOf(TraceKind.Spall), "Заброневые осколки");
                Legend(lx, ly + lh * 3, lh, DamageSolver.ColorOf(TraceKind.Fragment), "Осколки подрыва");
                Legend(lx, ly + lh * 4, lh, Opaque(assets.moduleIntact), "Модуль цел");
                Legend(lx, ly + lh * 5, lh, Opaque(assets.moduleDamaged), "Модуль повреждён");
                Legend(lx, ly + lh * 6, lh, Opaque(assets.moduleDestroyed), "Модуль уничтожен");
            }

            GUI.Label(new Rect(0, h * 0.92f, w, _body.fontSize * 1.5f), "SPACE / A — продолжить", _legendCentered);
        }

        void Legend(float x, float y, float lh, Color color, string text)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y + lh * 0.25f, lh * 0.5f, lh * 0.5f), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(x + lh * 0.8f, y, 400, lh), text, _legend);
        }

        static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        static void Box(Rect r)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void EnsureStyles()
        {
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.024f));
            if (_body != null && _body.fontSize == size) return;
            _body = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, wordWrap = true };
            _body.normal.textColor = Color.white;
            _legend = new GUIStyle(_body) { wordWrap = false, fontSize = Mathf.RoundToInt(size * 0.85f) };
            _title = new GUIStyle(_body) { fontSize = Mathf.RoundToInt(size * 1.8f), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _legendCentered = new GUIStyle(_legend) { alignment = TextAnchor.MiddleCenter };
            _legendCentered.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        }
    }
}
