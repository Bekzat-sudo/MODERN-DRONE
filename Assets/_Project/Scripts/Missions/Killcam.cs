using System;
using System.Collections;
using ModernDrone.Core;
using ModernDrone.Damage;
using ModernDrone.Drone;
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
    /// The drone and every vehicle's root pose are recorded (<see cref="FlightTrack"/>). During the replay the world is
    /// frozen at the moment of impact (timeScale 0, audio paused, the fresh explosion hidden) and vehicles are posed
    /// from the recording, then put back exactly where the strike found them before physics resumes. Turrets, gun
    /// mounts, interceptors and vehicles that left the map before the strike are not replayed.
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
        [Tooltip("Manual zoom range during the replay, as a multiple of the automatic framing.")]
        [SerializeField] float minUserZoom = 0.3f;
        [SerializeField] float maxUserZoom = 12f;
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
        float _userZoom = 1f;
        // Pilot-steered recon camera: orbit around the target (deg), range factor, gimbal pan (deg).
        float _orbit, _range = 1f, _panYaw, _panPitch;
        // Narrowest view: about 55x on the 55 deg base, in line with the Mavic 3T's 56x hybrid zoom
        // (dji.com/mavic-3-enterprise/specs).
        const float MinFov = 1f;
        readonly FlightTrack _track = new FlightTrack();
        // Vehicles drive at ~10 m/s at most, so 10 Hz with interpolation keeps them within centimetres of the recording.
        const float VehicleInterval = 0.1f;
        readonly System.Collections.Generic.List<(Transform, FlightTrack)> _vehicles = new System.Collections.Generic.List<(Transform, FlightTrack)>();
        readonly System.Collections.Generic.List<Rigidbody> _unInterpolated = new System.Collections.Generic.List<Rigidbody>();
        readonly System.Collections.Generic.List<(Vector3, Quaternion)> _impactPoses = new System.Collections.Generic.List<(Vector3, Quaternion)>();
        Transform _tracked;
        ModernDrone.Drone.DroneController _trackedDrone;
        GameObject _ghost;
        Transform[] _ghostProps = new Transform[0];
        bool _ghostPusher;
        float _ghostPropSpin;
        readonly System.Collections.Generic.List<Behaviour> _hiddenLights = new System.Collections.Generic.List<Behaviour>();
        readonly System.Collections.Generic.List<Renderer> _hiddenFx = new System.Collections.Generic.List<Renderer>();
        Texture2D _noise;
        Color32[] _noisePixels;
        GUIStyle _body, _legend, _legendRight;

        public bool Running { get; private set; }

        public void SetAssets(KillcamAssets a) => assets = a;

        /// <summary>Starts a fresh recording of <paramref name="drone"/> and of every vehicle for the next replay.</summary>
        public void BeginRecording(Transform drone)
        {
            _track.Clear();
            _tracked = drone;
            _trackedDrone = drone != null ? drone.GetComponent<ModernDrone.Drone.DroneController>() : null;
            _vehicles.Clear();
            foreach (var v in FindObjectsByType<VehicleDamageModel>(FindObjectsSortMode.None))
                _vehicles.Add((v.transform, new FlightTrack(VehicleInterval)));
        }

        void LateUpdate()
        {
            if (Running || _tracked == null) return;
            _track.Add(Time.time, _tracked.position, _tracked.rotation, false, Motors());
            RecordVehicles(Time.time, false);
        }

        /// <summary>The tracked drone's motor speeds for the replay's propellers.</summary>
        Vector4 Motors()
        {
            if (_trackedDrone == null) return Vector4.zero;
            var s = _trackedDrone.MotorSpin;
            return s.Length >= 4 ? new Vector4(s[0], s[1], s[2], s[3]) : Vector4.zero;
        }

        void RecordVehicles(float time, bool force)
        {
            foreach (var (t, track) in _vehicles)
                if (t != null && t.gameObject.activeInHierarchy) track.Add(time, t.position, t.rotation, force);
        }

        /// <summary>Hands the vehicles back to physics exactly where the strike found them.</summary>
        void RestoreVehicles()
        {
            for (int i = 0; i < _vehicles.Count && i < _impactPoses.Count; i++)
            {
                var t = _vehicles[i].Item1;
                if (t != null && t.gameObject.activeInHierarchy) t.SetPositionAndRotation(_impactPoses[i].Item1, _impactPoses[i].Item2);
            }
            foreach (var rb in _unInterpolated) if (rb != null) rb.interpolation = RigidbodyInterpolation.Interpolate;
            _unInterpolated.Clear();
        }

        /// <summary>Puts each recorded vehicle where it was at <paramref name="time"/> (the world is frozen meanwhile).</summary>
        void PoseVehicles(float time)
        {
            foreach (var (t, track) in _vehicles)
            {
                if (t == null || track.Count < 2 || !t.gameObject.activeInHierarchy) continue;
                var pose = track.Sample(time);
                t.SetPositionAndRotation(pose.position, pose.rotation);
            }
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
            ResetCameraControl();
            _report = report;
            // Strikes land in FixedUpdate, whose clock can trail the last frame's sample.
            float impact = Mathf.Max(report.time, _track.EndTime + 0.001f);
            if (_tracked != null) _track.Add(impact, _tracked.position, _tracked.rotation, true, Motors());
            RecordVehicles(impact, true);
            _impactPoses.Clear();
            foreach (var (vt, _) in _vehicles)
                _impactPoses.Add(vt != null ? (vt.position, vt.rotation) : default);
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
            t = 0f;
            while (t < duration && !SkipPressed())
            {
                if (!ModernDrone.Core.GamePause.IsPaused) CameraInput();
                Vector3 camPos = MovedRecon(focus, reconPos);
                float distance = Vector3.Distance(camPos, focus);
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                float frame = Mathf.Lerp(reconFrameStart, reconFrameEnd, k) / _userZoom;
                _camera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(frame * 0.5f / _camera.aspect / distance) * Mathf.Rad2Deg, MinFov, 60f);
                _reconZoom = fov / _camera.fieldOfView;
                transform.position = camPos;
                transform.rotation = Quaternion.LookRotation(focus - camPos) * Pan() * Wander();
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
            // Interpolation would keep writing the body's own pose over the replayed one.
            foreach (var (t, _) in _vehicles)
                if (t != null && t.TryGetComponent<Rigidbody>(out var rb) && rb.interpolation == RigidbodyInterpolation.Interpolate)
                {
                    rb.interpolation = RigidbodyInterpolation.None;
                    _unInterpolated.Add(rb);
                }
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
                    CameraInput();
                    _replayTime = Mathf.Clamp(_replayTime + _replayRate * Time.unscaledDeltaTime, start, end);
                    if (_replayTime >= end && _replayRate > 0f) break;
                }

                var pose = _track.Sample(_replayTime);
                _ghost.transform.SetPositionAndRotation(pose.position, pose.rotation);
                // Props spin at the recorded motor speeds, following the replay (stopped when paused, backwards when
                // scrubbing back, like rewound video).
                for (int i = 0; i < _ghostProps.Length && i < 4; i++)
                    if (_ghostProps[i] != null)
                    {
                        var axis = _ghostPusher ? _ghostProps[i].InverseTransformDirection(_ghost.transform.forward) : Vector3.up;
                        _ghostProps[i].Rotate(axis, ModernDrone.Drone.QuadMixer.Spin[i] * pose.motors[i] * _ghostPropSpin * _replayRate * Time.unscaledDeltaTime, Space.Self);
                    }
                PoseVehicles(_replayTime);
                Vector3 aim = Vector3.Lerp(focus, pose.position, 0.5f);
                Vector3 camPos = MovedRecon(focus, reconPos);
                float frame = Mathf.Min(Vector3.Distance(focus, pose.position) + reconFrameStart, 900f) / _userZoom;
                float distance = Mathf.Max(1f, Vector3.Distance(camPos, aim));
                _camera.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(frame * 0.5f / _camera.aspect / distance) * Mathf.Rad2Deg, MinFov, 60f);
                _reconZoom = _baseFov / _camera.fieldOfView;
                // A few pixels wide at any zoom, like a route drawn over the video.
                if (path != null) path.widthMultiplier = frame / Mathf.Max(1, Screen.width) * 3f;
                // The operator's tracking lags a little behind the drone.
                var look = Quaternion.LookRotation(aim - camPos) * Pan() * Wander();
                transform.position = camPos;
                transform.rotation = first ? look : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                first = false;
                yield return null;
            }
            RestoreVehicles();
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

        /// <summary>
        /// Render-only copy of the drone's meshes (the drone's own scripts must not run on it). Propeller meshes hang
        /// under their own pivots, copied from PropVisuals, so the replay can spin them at the recorded motor speeds.
        /// </summary>
        GameObject BuildGhost()
        {
            var root = new GameObject("Replay_Drone");
            _ghostProps = new Transform[0];
            if (_tracked == null) return root;
            root.transform.SetPositionAndRotation(_tracked.position, _tracked.rotation);
            var visuals = _tracked.GetComponentInChildren<ModernDrone.Drone.PropVisuals>(true);
            var props = visuals != null ? visuals.Props : new Transform[0];
            _ghostPropSpin = visuals != null ? visuals.MaxVisualSpin : 0f;
            var flown = _tracked.GetComponent<ModernDrone.Drone.DroneController>();
            _ghostPusher = flown != null && flown.Config != null && flown.Config.fixedWing; // prop on the flight axis
            _ghostProps = new Transform[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i] == null) continue;
                _ghostProps[i] = new GameObject("Prop" + i).transform;
                _ghostProps[i].SetPositionAndRotation(props[i].position, props[i].rotation);
                _ghostProps[i].SetParent(root.transform, true);
            }
            foreach (var mf in _tracked.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                Transform parent = root.transform;
                for (int i = 0; i < props.Length; i++)
                    if (props[i] != null && mf.transform.IsChildOf(props[i])) { parent = _ghostProps[i]; break; }
                var go = new GameObject(mf.name);
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.SetParent(parent, true);
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

        void ResetCameraControl()
        {
            _userZoom = 1f;
            _orbit = _panYaw = _panPitch = 0f;
            _range = 1f;
        }

        /// <summary>
        /// Recon camera steering: WASD / left stick fly the recon drone around the target and in or out, right mouse
        /// drag / right stick pan the gimbal (finer when zoomed in), C / right stick click resets, plus zoom.
        /// </summary>
        void CameraInput()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            if ((kb != null && kb.cKey.wasPressedThisFrame) || (pad != null && pad.rightStickButton.wasPressedThisFrame))
            {
                ResetCameraControl();
                return;
            }
            _userZoom = Mathf.Clamp(_userZoom * ZoomStep(), minUserZoom, maxUserZoom);

            Vector2 move = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            if (kb != null)
            {
                if (kb.aKey.isPressed) move.x -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
            }
            _orbit += Mathf.Clamp(move.x, -1f, 1f) * 35f * dt;
            _range = Mathf.Clamp(_range * Mathf.Exp(-Mathf.Clamp(move.y, -1f, 1f) * 0.8f * dt), 0.3f, 2.5f);

            Vector2 look = Vector2.zero;
            if (mouse != null && mouse.rightButton.isPressed) look += mouse.delta.ReadValue() * 0.15f;
            if (pad != null) look += pad.rightStick.ReadValue() * 90f * dt;
            float fine = _camera.fieldOfView / 60f;
            _panYaw = Mathf.Clamp(_panYaw + look.x * fine, -60f, 60f);
            _panPitch = Mathf.Clamp(_panPitch - look.y * fine, -40f, 40f);
        }

        /// <summary>Recon position after the pilot's orbit / range input, kept clear of the ground below it.</summary>
        Vector3 MovedRecon(Vector3 focus, Vector3 reconPos)
        {
            Vector3 p = focus + Quaternion.Euler(0f, _orbit, 0f) * (reconPos - focus) * _range;
            if (Physics.Raycast(p + Vector3.up * 1000f, Vector3.down, out var ground, 3000f, ModernDrone.Core.GameLayers.TerrainMask))
                p.y = Mathf.Max(p.y, ground.point.y + 15f);
            return p;
        }

        Quaternion Pan() => Quaternion.Euler(_panPitch, _panYaw, 0f);

        /// <summary>Manual zoom factor for this frame: mouse wheel notches, held + / − keys or the gamepad triggers.</summary>
        static float ZoomStep()
        {
            float step = 1f;
            var mouse = Mouse.current;
            if (mouse != null) step *= Mathf.Pow(1.15f, mouse.scroll.ReadValue().y / 120f);
            float rate = 0f; // e-folds per second while held
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) rate += 1.5f;
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) rate -= 1.5f;
            }
            var pad = Gamepad.current;
            if (pad != null) rate += 1.5f * (pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue());
            return step * Mathf.Exp(rate * Time.unscaledDeltaTime);
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

            // Captions, the strike summary, the X-ray legend and the prompts are game UI (UiTheme); the camera
            // overlays above imitate the recon feed and keep their own look.
            var t = UiTheme.T;
            if (_caption != null)
                UiTheme.Banner(UiTheme.Px(40f), _caption, _caption == "ПРОМАХ" ? UiTheme.Warning : _caption == "ПОПАДАНИЕ" ? UiTheme.Success : UiTheme.TextPrimary);

            if (_showSummary)
            {
                var summary = _report.Summary();
                float pw = Mathf.Min(w * 0.34f, UiTheme.Px(600f)), pad = UiTheme.Px(UiTheme.S5);
                float height = t.body.CalcHeight(new GUIContent(summary), pw - 2f * pad);
                var panel = new Rect(UiTheme.Px(UiTheme.Margin), h * 0.16f, pw, height + 2f * pad + UiTheme.Px(32f));
                UiTheme.Fill(panel, UiTheme.OverVideo);
                UiTheme.Text(new Rect(panel.x + pad, panel.y + pad, pw - 2f * pad, UiTheme.Px(26f)), "РЕЗУЛЬТАТ УДАРА", t.caption, UiTheme.TextSecondary);
                UiTheme.Text(new Rect(panel.x + pad, panel.y + pad + UiTheme.Px(32f), pw - 2f * pad, height), summary, t.body, UiTheme.TextPrimary);
            }

            if (_xray != null && _xray.Visible)
            {
                float lh = UiTheme.Px(32f), pad = UiTheme.Px(UiTheme.S5), lw = UiTheme.Px(380f);
                var panel = new Rect(w - UiTheme.Px(UiTheme.Margin) - lw, h * 0.16f, lw, lh * 7f + 2f * pad + UiTheme.Px(32f));
                UiTheme.Fill(panel, UiTheme.OverVideo);
                float lx = panel.x + pad, ly = panel.y + pad;
                UiTheme.Text(new Rect(lx, ly, lw, UiTheme.Px(26f)), "РЕНТГЕН · ЛЕГЕНДА", t.caption, UiTheme.TextSecondary);
                ly += UiTheme.Px(32f);
                Legend(lx, ly + lh * 0, lh, DamageSolver.ColorOf(TraceKind.Jet), "Струя — пробитие");
                Legend(lx, ly + lh * 1, lh, DamageSolver.ColorOf(TraceKind.JetStopped), "Струя — остановлена");
                Legend(lx, ly + lh * 2, lh, DamageSolver.ColorOf(TraceKind.Spall), "Заброневые осколки");
                Legend(lx, ly + lh * 3, lh, DamageSolver.ColorOf(TraceKind.Fragment), "Осколки подрыва");
                Legend(lx, ly + lh * 4, lh, Opaque(assets.moduleIntact), "Модуль цел");
                Legend(lx, ly + lh * 5, lh, Opaque(assets.moduleDamaged), "Модуль повреждён");
                Legend(lx, ly + lh * 6, lh, Opaque(assets.moduleDestroyed), "Модуль уничтожен");
            }

            // Two prompt rows under the replay timeline (0.905 h).
            var camera = new Rect(0, h - UiTheme.Px(40f), w, UiTheme.Px(32f));
            var prompts = new Rect(0, camera.y - UiTheme.Px(38f), w, UiTheme.Px(32f));
            string next = ControlInput.Key("Space", "A");
            if (_replay)
            {
                UiTheme.Prompts(prompts, true, (ControlInput.Key("← →", "LB RB"), "перемотка"), (ControlInput.Key("↓", "X"), "пауза"),
                    (ControlInput.Key("R", "Y"), "с начала"), (next, "дальше"));
            }
            else UiTheme.Prompts(prompts, true, (next, "продолжить"));
            if (_replay || _recon)
                UiTheme.Prompts(camera, true,
                    (ControlInput.Key("WASD", "L"), "облёт"), (ControlInput.Key("ПКМ", "R"), "поворот"),
                    (ControlInput.Key("колесо", "LT RT"), "зум"), (ControlInput.Key("C", "R3"), "сброс камеры"));
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

        static void Legend(float x, float y, float lh, Color color, string text)
        {
            float s = UiTheme.Px(14f);
            UiTheme.Fill(new Rect(x, y + (lh - s) * 0.5f, s, s), color);
            UiTheme.Text(new Rect(x + s + UiTheme.Px(UiTheme.S3), y, UiTheme.Px(320f), lh), text, UiTheme.T.label, UiTheme.TextBody);
        }

        static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        void EnsureStyles()
        {
            int size = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.024f));
            if (_body != null && _body.fontSize == size) return;
            _body = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, wordWrap = true };
            _body.normal.textColor = Color.white;
            _legend = new GUIStyle(_body) { wordWrap = false, fontSize = Mathf.RoundToInt(size * 0.85f) };
            _legendRight = new GUIStyle(_legend) { alignment = TextAnchor.UpperRight };
        }
    }
}
