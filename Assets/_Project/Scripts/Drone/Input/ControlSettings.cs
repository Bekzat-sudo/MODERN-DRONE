using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModernDrone.Drone
{
    public enum ControlDevice
    {
        Auto,
        Joystick,
        Gamepad,
        KeyboardMouse
    }

    public enum GamepadThrottleMode
    {
        /// <summary>Spring-centred stick, centre = 50%.</summary>
        CenterIsHalf,
        /// <summary>Spring-centred stick, centre = hover throttle.</summary>
        CenterIsHover,
        /// <summary>Stick changes throttle gradually; value is held when released.</summary>
        Incremental,
        /// <summary>Right trigger is throttle 0..100%.</summary>
        Trigger,
        /// <summary>Spring-centred stick, centre = 0%, full up = 100% (lower half unused).</summary>
        CenterIsZero
    }

    public enum PilotAction
    {
        Arm,
        Mode,
        Reset,
        TiltUp,
        TiltDown,
        Pause,
        ThrottleUp,
        ThrottleDown,
        YawLeft,
        YawRight,
        ThrottleCut,
        /// <summary>Restart the current mission (appended last: saved bindings are stored by enum value).</summary>
        Restart
    }

    /// <summary>Calibrated analog axis of a joystick-class device (RC transmitter, HOTAS...).</summary>
    [Serializable]
    public sealed class AxisCalibration
    {
        public string control;
        public float min = -1f;
        public float center;
        public float max = 1f;
        public bool invert;

        public AxisCalibration() { }
        public AxisCalibration(string control) => this.control = control;

        public static float? ReadRaw(InputDevice device, string control)
        {
            if (device == null || string.IsNullOrEmpty(control)) return null;
            var axis = device.TryGetChildControl<AxisControl>(control);
            return axis?.ReadUnprocessedValue();
        }

        /// <summary>-1..1 around the calibrated centre.</summary>
        public float? ReadBipolar(InputDevice device)
        {
            var raw = ReadRaw(device, control);
            return raw.HasValue ? NormalizeBipolar(raw.Value) : (float?)null;
        }

        /// <summary>Maps a raw value to -1..1 with separate scaling either side of the centre.</summary>
        public float NormalizeBipolar(float r)
        {
            float v = r >= center
                ? (max > center ? (r - center) / (max - center) : 0f)
                : (center > min ? (r - center) / (center - min) : 0f);
            v = Mathf.Clamp(v, -1f, 1f);
            return invert ? -v : v;
        }

        /// <summary>0..1 across the calibrated range (throttle).</summary>
        public float? ReadUnipolar(InputDevice device)
        {
            var raw = ReadRaw(device, control);
            return raw.HasValue ? NormalizeUnipolar(raw.Value) : (float?)null;
        }

        public float NormalizeUnipolar(float r)
        {
            float v = Mathf.Approximately(max, min) ? 0f : Mathf.InverseLerp(min, max, r);
            return invert ? 1f - v : v;
        }
    }

    /// <summary>Per-action bindings for each device family (Input System control paths).</summary>
    [Serializable]
    public sealed class ActionBinding
    {
        public PilotAction action;
        public string keyboard;
        public string gamepad;
        public string joystick;

        public ActionBinding() { }

        public ActionBinding(PilotAction action, string keyboard, string gamepad, string joystick = "")
        {
            this.action = action;
            this.keyboard = keyboard;
            this.gamepad = gamepad;
            this.joystick = joystick;
        }
    }

    /// <summary>
    /// All pilot-facing control settings, persisted as JSON in the player's data folder.
    /// </summary>
    [Serializable]
    public sealed class ControlSettings
    {
        const string FileName = "controls.json";

        /// <summary>Bumped when defaults change in a way saved files must be migrated.</summary>
        public int version = CurrentVersion;
        public const int CurrentVersion = 5;

        public ControlDevice device = ControlDevice.Auto;
        [Tooltip("Display name of the preferred joystick; empty = first found.")]
        public string joystickName = "";

        [Header("Joystick / RC (AETR default: X, Y, Z, RX)")]
        public AxisCalibration jsRoll = new AxisCalibration("stick/x");
        public AxisCalibration jsPitch = new AxisCalibration("stick/y");
        public AxisCalibration jsThrottle = new AxisCalibration("z");
        public AxisCalibration jsYaw = new AxisCalibration("rx");
        public bool jsUseArmAxis = true;
        public AxisCalibration jsArm = new AxisCalibration("ry");
        public bool jsUseModeAxis;
        public AxisCalibration jsMode = new AxisCalibration("rz");
        public float jsDeadband = 0.01f;

        [Header("Gamepad")]
        public GamepadThrottleMode gpThrottleMode = GamepadThrottleMode.CenterIsZero;
        public float gpDeadzone = 0.03f;
        public float gpIncrementalSpeed = 0.8f;
        [Tooltip("Stick up = nose up (camera style). Off = stick forward pitches the nose down (FPV / aircraft).")]
        public bool gpPitchUpIsNoseUp = true;
        public bool gpInvertRoll;
        [Tooltip("Scale for pitch / roll stick deflection.")]
        public float gpTiltSensitivity = 0.6f;
        public float gpExpo;

        [Header("Keyboard + mouse")]
        [Tooltip("Virtual stick deflection per pixel of mouse movement.")]
        public float mouseSensitivity = 0.002f;
        [Tooltip("Speed at which the virtual stick returns to centre, per second (0 = holds position).")]
        public float mouseReturn = 2.5f;
        [Tooltip("Mouse up = nose up (FPS style). Off = mouse forward pitches the nose down.")]
        public bool mousePitchUpIsNoseUp = true;
        public bool mouseInvertRoll;
        public float kbThrottleSpeed = 0.5f;
        public float kbYaw = 0.6f;

        [Header("Flight")]
        [Tooltip("Acro holds the attitude when the stick is released (no self-levelling).")]
        public FlightMode startMode = FlightMode.Acro;
        [Tooltip("Maximum tilt in Angle mode, degrees.")]
        public float angleMaxTilt = 45f;
        public AxisRates rollRates = new AxisRates(100f, 650f, 0.6f);
        public AxisRates pitchRates = new AxisRates(100f, 650f, 0.6f);
        public AxisRates yawRates = new AxisRates(100f, 500f, 0.5f);
        [Tooltip("Stick smoothing (setpoint low-pass), Hz. Lower = smoother, laggier.")]
        public float stickSmoothingHz = 12f;
        [Tooltip("Pilot offset added to each drone model's stock camera uptilt, deg.")]
        public float cameraTiltOffset;
        /// <summary>Absolute uptilt saved before v4; migrated into <see cref="cameraTiltOffset"/>.</summary>
        public float cameraTilt = 25f;
        public float cameraFov = 118f;
        [Tooltip("Fisheye strength of the FPV lens, 0 = rectilinear.")]
        [Range(0f, 1f)] public float lensDistortion = 0.6f;
        [Tooltip("Show the analog camera's 4:3 frame with side bars, as FPV goggles and DVRs do.")]
        public bool frame43 = true;

        [Header("OSD")]
        public OsdSettings osd = new OsdSettings();

        [Header("Display & audio")]
        public bool fullscreen = true;
        public bool vSync = true;
        [Range(0f, 1f)] public float masterVolume = 1f;
        [Range(0f, 1f)] public float motorVolume = 1f;

        [Header("Graphics (applied by ModernDrone.Video.GraphicsQuality)")]
        [Tooltip("0 off, 1 FXAA, 2 SMAA, 3 STP (temporal upscaler / anti-aliasing).")]
        public int antiAliasing = 3;
        [Range(0.5f, 1f)] public float renderScale = 1f;
        [Tooltip("0 off, 1 low, 2 medium, 3 high.")]
        public int shadowQuality = 3;
        [Tooltip("Tree and bush LOD bias: 1 low, 2 medium, 3 high.")]
        public int vegetationDetail = 3;
        [Tooltip("Grass draw distance: 0 off, 1 low, 2 medium, 3 high.")]
        public int grassQuality = 3;
        [Tooltip("Index into GraphicsQuality.FrameLimits (0 = no cap); ignored with vertical sync.")]
        public int frameRateLimit;
        public bool showFps;

        public ActionBinding[] bindings = DefaultBindings();

        static ControlSettings _current;

        /// <summary>Loaded lazily: persistentDataPath cannot be read from static initialisers.</summary>
        public static ControlSettings Current => _current ??= Load();

        /// <summary>Raised when settings are edited (so the drone can re-apply rates / camera).</summary>
        public static event Action<ControlSettings> Changed;

        public static void NotifyChanged() => Changed?.Invoke(Current);

        public static ActionBinding[] DefaultBindings() => new[]
        {
            new ActionBinding(PilotAction.Arm, "<Keyboard>/space", "<Gamepad>/rightShoulder"),
            new ActionBinding(PilotAction.Mode, "<Keyboard>/m", "<Gamepad>/leftShoulder"),
            new ActionBinding(PilotAction.Reset, "<Keyboard>/r", "<Gamepad>/selectButton"),
            new ActionBinding(PilotAction.TiltUp, "<Keyboard>/rightBracket", "<Gamepad>/dpad/up"),
            new ActionBinding(PilotAction.TiltDown, "<Keyboard>/leftBracket", "<Gamepad>/dpad/down"),
            // Menu / Start acts as Esc on gamepads.
            new ActionBinding(PilotAction.Pause, "<Keyboard>/escape", "<Gamepad>/startButton"),
            new ActionBinding(PilotAction.ThrottleUp, "<Keyboard>/w", ""),
            new ActionBinding(PilotAction.ThrottleDown, "<Keyboard>/s", ""),
            new ActionBinding(PilotAction.YawLeft, "<Keyboard>/a", ""),
            new ActionBinding(PilotAction.YawRight, "<Keyboard>/d", ""),
            new ActionBinding(PilotAction.ThrottleCut, "<Keyboard>/x", ""),
            new ActionBinding(PilotAction.Restart, "<Keyboard>/f5", "<Gamepad>/buttonNorth"),
        };

        public ActionBinding Binding(PilotAction action)
        {
            foreach (var b in bindings)
                if (b.action == action) return b;
            // Settings saved by an older version may lack new actions.
            var def = Array.Find(DefaultBindings(), b => b.action == action);
            var list = new System.Collections.Generic.List<ActionBinding>(bindings) { def };
            bindings = list.ToArray();
            return def;
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static ControlSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonUtility.FromJson<ControlSettings>(File.ReadAllText(FilePath));
                    if (s != null) return Migrate(s);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModernDrone] Could not read control settings, using defaults: {e.Message}");
            }
            return new ControlSettings();
        }

        /// <summary>
        /// v2: Actual Rates replaced the old RC-rate fields (old values no longer parse, so they are reset), and the
        /// default start mode became Acro.
        /// </summary>
        static ControlSettings Migrate(ControlSettings s)
        {
            if (s.version < 2)
            {
                var d = new ControlSettings();
                s.rollRates = d.rollRates;
                s.pitchRates = d.pitchRates;
                s.yawRates = d.yawRates;
                s.stickSmoothingHz = d.stickSmoothingHz;
                s.startMode = FlightMode.Acro;
            }
            if (s.version < 3)
            {
                // v3: gamepad Menu/Start opens the pause menu (like Esc); drone reset moved to View/Select.
                var pause = s.Binding(PilotAction.Pause);
                var reset = s.Binding(PilotAction.Reset);
                if (pause.gamepad == "<Gamepad>/selectButton") pause.gamepad = "<Gamepad>/startButton";
                if (reset.gamepad == "<Gamepad>/startButton") reset.gamepad = "<Gamepad>/selectButton";
            }
            if (s.version < 4)
            {
                // v4: the camera uptilt is per drone model; keep the pilot's old preference as an offset from the
                // previous 25 deg default.
                s.cameraTiltOffset = Mathf.Clamp(s.cameraTilt - 25f, -30f, 40f);
            }
            if (s.version < 5 && s.gpThrottleMode == GamepadThrottleMode.CenterIsHover)
                s.gpThrottleMode = GamepadThrottleMode.CenterIsZero; // v5: released stick = no throttle by default
            s.version = CurrentVersion;
            return s;
        }

        /// <summary>Applies display and audio settings (on start and whenever settings change).</summary>
        public static void ApplyGlobal()
        {
            var s = Current;
            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            if (!Application.isEditor) Screen.fullScreen = s.fullscreen;
            AudioListener.volume = Mathf.Clamp01(s.masterVolume);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyOnStart()
        {
            ApplyGlobal();
            Changed -= OnChangedApply;
            Changed += OnChangedApply;
        }

        static void OnChangedApply(ControlSettings _) => ApplyGlobal();

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModernDrone] Could not save control settings: {e.Message}");
            }
            NotifyChanged();
        }

        public static void ResetToDefaults()
        {
            _current = new ControlSettings();
            Save();
        }

        /// <summary>For tests and tools: replace the active settings without touching disk.</summary>
        public static void SetCurrent(ControlSettings settings)
        {
            _current = settings ?? new ControlSettings();
            NotifyChanged();
        }
    }
}
