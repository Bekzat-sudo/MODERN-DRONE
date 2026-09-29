using System;
using System.Collections;
using ModernDrone.Damage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModernDrone.Missions
{
    /// <summary>
    /// Post-strike replay camera, cut like strike compilations: the FPV feed dies in analog static, then an overwatch
    /// (recon) drone's stabilised zoom camera replays the attack run (rewind / fast-forward / pause), then the world
    /// resumes and the explosion and fire play out live under that camera, then an orbiting x-ray view of the struck
    /// vehicle with module states and strike traces.
    /// Overwatch geometry is illustrative: Mavic 3T-class quadcopters hover roughly 100-200 m up and 300-500 m out
    /// so they stay unheard, zooming in to about 28x (dji.com/mavic-3-enterprise/specs: 56x hybrid zoom).
    /// Only the drone's flight is recorded (<see cref="FlightTrack"/>): during the replay the world is frozen at the
    /// moment of impact (timeScale 0, audio paused, the fresh explosion hidden), so moving vehicles stand where they
    /// were hit rather than where they were earlier in the run.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class Killcam : MonoBehaviour
    {
        [SerializeField] KillcamAssets assets;
        [SerializeField] float staticPhase = 0.4f;
        [SerializeField] float reconPhase = 12f;
        [SerializeField] float reconMissPhase = 4f;
        [Tooltip("Recon drone slant range and height above the target, m (illustrative).")]
        [SerializeField] float reconRange = 350f;
        [SerializeField] float reconHeight = 130f;
        [Tooltip("Frame width at the target at the start and end of the slow zoom-in, m.")]
        [SerializeField] float reconFrameStart = 55f;
        [SerializeField] float reconFrameEnd = 28f;
        [Tooltip("Seconds before impact the replay starts from (the whole flight can be rewound).")]
        [SerializeField] float replayWindow = 20f;
        [SerializeField] float scrubSpeed = 4f;
        [SerializeField] float xrayPhase = 9f;
        [SerializeField] float orbitSpeed = 22f;
        [SerializeField] float orbitRadius = 11f;
        [SerializeField] float orbitHeight = 4.5f;

        Camera _camera;
        AudioListener _listener;
        XRayView _xray;
        StrikeReport _report;
        string _caption;
        bool _static, _recon, _showSummary, _replay, _replayPaused, _frozen;
        float _reconStart, _reconZoom, _replayTime, _replayRate, _baseFov;
        readonly FlightTrack _track = new FlightTrack();
        Transform _tracked;
        GameObject _ghost;
        readonly System.Collections.Generic.List<Behaviour> _hiddenLights = new System.Collections.Generic.List<Behaviour>();
        readonly System.Collections.Generic.List<Renderer> _hiddenFx = new System.Collections.Generic.List<Renderer>();
        Texture2D _noise;
        Color32[] _noisePixels;
        GUIStyle _title, _body, _legend, _legendCentered, _legendRight;

        public bool Running { get; private set; }

        public void SetAssets(KillcamAssets a) => assets = a;

        /// <summary>Starts a fresh flight recording of <paramref name="drone"/> for the next replay.</summary>
        public void BeginRecording(Transform drone)
        {
            _track.Clear();
            _tracked = drone;
        }

        void LateUpdate()
        {
            if (!Running && _tracked != null) _track.Add(Time.time, _tracked.position, _tracked.rotation);
        }

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
            if (_tracked != null) _track.Add(report.time, _tracked.position, _tracked.rotation, force: true);
            // Before any frame runs, so the strike's effects cannot start ahead of the replay.
            Freeze(true);
            _xray ??= new XRayView(assets);
            SetActive(true);

            Vector3 dir = report.direction.sqrMagnitude > 0f ? report.direction.normalized : Vector3.forward;
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flat.sqrMagnitude < 1e-3f) flat = Vector3.forward;
            flat.Normalize();

            bool hitVehicle = report.stateBefore.Count > 0;
            Vector3 focus = hitVehicle ? VehicleCenter(report) : report.point;

            float fov = _baseFov = _camera.fieldOfView;
            Vector3 reconPos = ReconPosition(focus, flat);

            // Phase 0: the FPV feed dies in static, as the video link does at impact.
            _caption = null;
            _showSummary = false;
            _static = true;
            float t = 0f;
            transform.position = reconPos;
            transform.LookAt(focus);
            yield return null; // swallow the input frame that caused the detonation
            while (t < staticPhase) { t += Time.unscaledDeltaTime; yield return null; }
            _static = false;

            // Phase 1a: the recon drone's recording of the attack run, world frozen at the moment of impact.
            if (_track.Count >= 2)
                yield return Replay(focus, reconPos);
            Freeze(false);
            yield return null; // a skip press ends the replay only, not the live explosion too

            // Phase 1b: same camera, live: slow zoom-in while the target explodes and burns.
            _caption = hitVehicle ? "ПОПАДАНИЕ" : "ПРОМАХ";
            _showSummary = !hitVehicle;
            _recon = true;
            _reconStart = Time.unscaledTime;
            float duration = hitVehicle ? reconPhase : reconMissPhase;
            float distance = Vector3.Distance(reconPos, focus);
            t = 0f;
            while (t < duration && !SkipPressed())
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                float frame = Mathf.Lerp(reconFrameStart, reconFrameEnd, k);
                _camera.fieldOfView = 2f * Mathf.Atan(frame * 0.5f / _camera.aspect / distance) * Mathf.Rad2Deg;
                _reconZoom = fov / _camera.fieldOfView;
                transform.position = reconPos;
                transform.rotation = Quaternion.LookRotation(focus - reconPos) * Wander();
                t += Time.deltaTime;
                yield return null;
            }
            _recon = false;
            _camera.fieldOfView = fov;

            // Phase 2: x-ray orbit.
            if (hitVehicle)
            {
                _caption = "РЕНТГЕН";
                _showSummary = true;
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

        /// <summary>
        /// Plays the recorded flight back from the recon position: the camera keeps both the drone and the target in
        /// frame and zooms in as they close. Held arrows / shoulders scrub, down / X pauses, R / Y restarts, Space / A
        /// skips; reaching the impact moves on to the live explosion.
        /// </summary>
        IEnumerator Replay(Vector3 focus, Vector3 reconPos)
        {
            _replay = true;
            _replayPaused = false;
            _caption = "ПОВТОР";
            float start = _track.StartTime, end = _track.EndTime;
            _replayTime = Mathf.Max(start, end - replayWindow);
            _ghost = BuildGhost();
            var path = BuildPath();
            bool first = true;
            while (true)
            {
                if (!ModernDrone.Core.GamePause.IsPaused)
                {
                    Time.timeScale = 0f; // closing the pause menu resets it to 1
                    if (SkipPressed()) break;
                    var kb = Keyboard.current;
                    var pad = Gamepad.current;
                    bool back = (kb != null && kb.leftArrowKey.isPressed) || (pad != null && (pad.leftShoulder.isPressed || pad.dpad.left.isPressed));
                    bool fwd = (kb != null && kb.rightArrowKey.isPressed) || (pad != null && (pad.rightShoulder.isPressed || pad.dpad.right.isPressed));
                    if ((kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame))
                        _replayPaused = !_replayPaused;
                    if ((kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
                        _replayTime = start;
                    _replayRate = back ? -scrubSpeed : fwd ? scrubSpeed : _replayPaused ? 0f : 1f;
                    _replayTime = Mathf.Clamp(_replayTime + _replayRate * Time.unscaledDeltaTime, start, end);
                    if (_replayTime >= end && _replayRate > 0f) break;
                }

                var pose = _track.Sample(_replayTime);
                _ghost.transform.SetPositionAndRotation(pose.position, pose.rotation);
                Vector3 aim = Vector3.Lerp(focus, pose.position, 0.5f);
                float frame = Mathf.Min(Vector3.Distance(focus, pose.position) + reconFrameStart, 900f);
                float distance = Mathf.Max(1f, Vector3.Distance(reconPos, aim));
                _camera.fieldOfView = Mathf.Min(60f, 2f * Mathf.Atan(frame * 0.5f / _camera.aspect / distance) * Mathf.Rad2Deg);
                _reconZoom = _baseFov / _camera.fieldOfView;
                // A few pixels wide at any zoom, like a route drawn over the video.
                if (path != null) path.widthMultiplier = frame / Mathf.Max(1, Screen.width) * 3f;
                // The operator's tracking lags a little behind the drone.
                var look = Quaternion.LookRotation(aim - reconPos) * Wander();
                transform.position = reconPos;
                transform.rotation = first ? look : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                first = false;
                yield return null;
            }
            if (path != null) Destroy(path.gameObject);
            Destroy(_ghost);
            _ghost = null;
            _replay = false;
        }

        /// <summary>
        /// Freezes the world at the moment of impact and hides what the strike has just spawned (particle systems that
        /// have not simulated yet, and the lights beside them), or restores all of it.
        /// </summary>
        void Freeze(bool on)
        {
            if (on == _frozen) return;
            _frozen = on;
            if (on)
            {
                Time.timeScale = 0f;
                AudioListener.pause = true;
                foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                {
                    if (ps.time > 0f) continue; // already burning before the strike
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r != null && r.enabled) { r.enabled = false; _hiddenFx.Add(r); }
                    if (ps.transform.parent == null) continue;
                    foreach (var l in ps.transform.parent.GetComponentsInChildren<Light>())
                        if (l.enabled) { l.enabled = false; _hiddenLights.Add(l); }
                }
            }
            else
            {
                foreach (var r in _hiddenFx) if (r != null) r.enabled = true;
                foreach (var l in _hiddenLights) if (l != null) l.enabled = true;
                _hiddenFx.Clear();
                _hiddenLights.Clear();
                AudioListener.pause = false;
                if (!ModernDrone.Core.GamePause.IsPaused) Time.timeScale = 1f;
            }
        }

        /// <summary>Render-only copy of the drone's meshes (the drone's own scripts must not run on it).</summary>
        GameObject BuildGhost()
        {
            var root = new GameObject("Replay_Drone");
            if (_tracked == null) return root;
            var inverse = Quaternion.Inverse(_tracked.rotation);
            foreach (var mf in _tracked.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                var go = new GameObject(mf.name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = _tracked.InverseTransformPoint(mf.transform.position);
                go.transform.localRotation = inverse * mf.transform.rotation;
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            }
            return root;
        }

        /// <summary>The whole recorded route as a thin line (at most ~1000 points).</summary>
        LineRenderer BuildPath()
        {
            if (assets == null || assets.line == null) return null;
            var lr = new GameObject("Replay_Path").AddComponent<LineRenderer>();
            lr.sharedMaterial = assets.line;
            lr.useWorldSpace = true;
            int step = Mathf.Max(1, _track.Count / 1000);
            var points = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < _track.Count; i += step) points.Add(_track[i].position);
            points.Add(_track[_track.Count - 1].position);
            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());
            lr.startWidth = lr.endWidth = 1f;
            lr.startColor = new Color(1f, 0.85f, 0.3f, 0.35f);
            lr.endColor = new Color(1f, 0.55f, 0.2f, 0.85f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>Gimbal-stabilised, so only a slow sub-degree wander.</summary>
        static Quaternion Wander()
        {
            float tt = Time.unscaledTime * 0.35f;
            return Quaternion.Euler((Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * 0.12f, (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * 0.12f, 0f);
        }

        /// <summary>
        /// Overwatch position: off to the side of the attack run, at the azimuth whose view of the target and of points
        /// around it is least blocked by terrain and scenery, so tree lines do not fill the frame.
        /// </summary>
        Vector3 ReconPosition(Vector3 focus, Vector3 flat)
        {
            int blockers = ModernDrone.Core.GameLayers.TerrainMask | ModernDrone.Core.GameLayers.EnvironmentMask;
            float horizontal = Mathf.Sqrt(Mathf.Max(1f, reconRange * reconRange - reconHeight * reconHeight));
            Vector3 best = focus;
            int bestScore = -1;
            foreach (float az in new[] { 140f, -140f, 100f, -100f, 180f, 60f, -60f, 0f })
            {
                Vector3 p = focus + Quaternion.Euler(0f, az, 0f) * flat * horizontal + Vector3.up * reconHeight;
                // Keep the drone its height above whatever ground lies under it.
                if (Physics.Raycast(p + Vector3.up * 500f, Vector3.down, out var ground, 1500f, ModernDrone.Core.GameLayers.TerrainMask))
                    p.y = Mathf.Max(p.y, ground.point.y + reconHeight * 0.5f);
                // The target counts triple; eight points 12 m around it stand for the rest of the frame.
                int score = Physics.Linecast(p, focus + Vector3.up * 0.5f, blockers, QueryTriggerInteraction.Ignore) ? 0 : 3;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 q = focus + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 12f + Vector3.up * 1.5f;
                    if (!Physics.Linecast(p, q, blockers, QueryTriggerInteraction.Ignore)) score++;
                }
                if (score > bestScore) { bestScore = score; best = p; }
                if (score == 11) break;
            }
            return best;
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
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

        void OnDisable()
        {
            _xray?.Hide();
            Freeze(false);
        }

        void OnDestroy()
        {
            if (_noise != null) Destroy(_noise);
        }

        void OnGUI()
        {
            if (!Running || _report == null || ModernDrone.Core.GamePause.IsPaused) return;
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            if (_static) { DrawStatic(w, h); return; }
            if (_recon || _replay) DrawReconOverlay(w, h);
            if (_replay) DrawReplayOverlay(w, h);

            if (_caption != null)
                GUI.Label(new Rect(0, h * 0.05f, w, _title.fontSize * 1.5f), _caption, _title);

            if (_showSummary)
            {
                var summary = _report.Summary();
                var rect = new Rect(w * 0.03f, h * 0.14f, w * 0.34f, h * 0.5f);
                float height = _body.CalcHeight(new GUIContent(summary), rect.width);
                Box(new Rect(rect.x - 10, rect.y - 8, rect.width + 20, height + 16));
                GUI.Label(rect, summary, _body);
            }

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

            GUI.Label(new Rect(0, h * 0.92f, w, _body.fontSize * 1.5f), _replay
                ? "← → / LB RB — перемотка     ↓ / X — пауза     R / Y — с начала     SPACE / A — дальше"
                : "SPACE / A — продолжить", _legendCentered);
        }

        /// <summary>Analog static with a bright vertical band, the last frames of an FPV feed at impact.</summary>
        void DrawStatic(float w, float h)
        {
            const int nw = 160, nh = 120;
            if (_noise == null)
            {
                _noise = new Texture2D(nw, nh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                _noisePixels = new Color32[nw * nh];
            }
            float band = Mathf.Repeat(Time.time * 0.9f, 1f) * nw;
            for (int y = 0; y < nh; y++)
            {
                float row = UnityEngine.Random.value * 0.35f; // horizontal streaks
                for (int x = 0; x < nw; x++)
                {
                    float v = UnityEngine.Random.value * 0.75f + row;
                    float d = Mathf.Abs(x - band);
                    if (d < 10f) v += (1f - d / 10f) * 0.8f;
                    byte b = (byte)(Mathf.Clamp01(v) * 255f);
                    _noisePixels[y * nw + x] = new Color32(b, b, b, 255);
                }
            }
            _noise.SetPixels32(_noisePixels);
            _noise.Apply(false);
            GUI.DrawTexture(new Rect(0, 0, w, h), _noise);
        }

        /// <summary>Minimal overwatch camera overlay: centre cross, corner brackets, zoom and recording time.</summary>
        void DrawReconOverlay(float w, float h)
        {
            var c = new Color(1f, 1f, 1f, 0.8f);
            float s = h * 0.03f, th = Mathf.Max(1f, h * 0.002f), cx = w * 0.5f, cy = h * 0.5f;
            Bar(new Rect(cx - s, cy - th * 0.5f, s * 0.6f, th), c);
            Bar(new Rect(cx + s * 0.4f, cy - th * 0.5f, s * 0.6f, th), c);
            Bar(new Rect(cx - th * 0.5f, cy - s, th, s * 0.6f), c);
            Bar(new Rect(cx - th * 0.5f, cy + s * 0.4f, th, s * 0.6f), c);
            float bx = w * 0.18f, by = h * 0.16f, bl = h * 0.06f;
            foreach (var (x, y, sx, sy) in new[] { (bx, by, 1f, 1f), (w - bx, by, -1f, 1f), (bx, h - by, 1f, -1f), (w - bx, h - by, -1f, -1f) })
            {
                Bar(new Rect(sx > 0 ? x : x - bl, sy > 0 ? y : y - th, bl, th), c);
                Bar(new Rect(sx > 0 ? x : x - th, sy > 0 ? y : y - bl, th, bl), c);
            }
            float lh = _legend.fontSize * 1.5f;
            float rec = _replay ? _replayTime - _track.StartTime : Time.unscaledTime - _reconStart;
            GUI.Label(new Rect(bx, h - by + lh * 0.3f, w, lh), $"ВИД С РАЗВЕДЧИКА   ZOOM {_reconZoom:0.0}x", _legend);
            GUI.Label(new Rect(bx, by - lh * 1.3f, w, lh), (Mathf.Repeat(rec, 1f) < 0.6f ? "● " : "   ") + $"REC {(int)rec / 60:00}:{(int)rec % 60:00}", _legend);
        }

        /// <summary>Tracking box on the replayed drone, playback state and a timeline of the whole flight.</summary>
        void DrawReplayOverlay(float w, float h)
        {
            var c = new Color(1f, 0.85f, 0.3f, 0.9f);
            float th = Mathf.Max(1f, h * 0.002f), lh = _legend.fontSize * 1.5f;
            if (_ghost != null)
            {
                var sp = _camera.WorldToScreenPoint(_ghost.transform.position);
                if (sp.z > 0f)
                {
                    float r = h * 0.018f, x = sp.x, y = h - sp.y;
                    Bar(new Rect(x - r, y - r, r * 2f, th), c);
                    Bar(new Rect(x - r, y + r, r * 2f, th), c);
                    Bar(new Rect(x - r, y - r, th, r * 2f), c);
                    Bar(new Rect(x + r, y - r, th, r * 2f + th), c);
                }
            }

            float bx = w * 0.18f, bw = w - 2f * bx, by = h * 0.905f;
            float span = Mathf.Max(0.01f, _track.EndTime - _track.StartTime);
            float k = (_replayTime - _track.StartTime) / span;
            Bar(new Rect(bx, by, bw, th * 3f), new Color(1f, 1f, 1f, 0.25f));
            Bar(new Rect(bx, by, bw * k, th * 3f), c);
            Bar(new Rect(bx + bw * k - th * 1.5f, by - th * 3f, th * 3f, th * 9f), Color.white);
            string state = _replayRate < 0f ? $"◀◀ ×{scrubSpeed:0}" : _replayRate > 1f ? $"▶▶ ×{scrubSpeed:0}" : _replayRate == 0f ? "ПАУЗА" : "▶";
            float left = _track.EndTime - _replayTime;
            GUI.Label(new Rect(bx, h * 0.84f + lh * 0.3f, bw, lh), $"{state}   до удара −{(int)left / 60:00}:{(int)left % 60:00}", _legendRight);
        }

        static void Bar(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
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
            _legendRight = new GUIStyle(_legend) { alignment = TextAnchor.UpperRight };
        }
    }
}
