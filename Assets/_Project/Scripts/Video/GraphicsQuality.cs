using ModernDrone.Core;
using ModernDrone.Drone;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ModernDrone.Video
{
    /// <summary>
    /// Applies the player's graphics settings (ControlSettings, "Графика" tab) to URP at start, on every change and
    /// on scene load: anti-aliasing (FXAA / SMAA per camera, or STP - Unity 6's temporal upscaler, which also
    /// anti-aliases), render scale (upscaled by STP, or FSR 1.0 without it), shadow quality, vegetation LOD bias,
    /// grass distance, frame-rate cap and an FPS counter. Defaults equal the shipped pipeline settings.
    /// In the editor the pipeline asset is put back on exit so play sessions do not rewrite it.
    /// </summary>
    public sealed class GraphicsQuality : MonoBehaviour
    {
        public static readonly string[] AntiAliasingNames = { "выкл", "FXAA", "SMAA", "STP (временное)" };
        public static readonly string[] LevelNames = { "выкл", "низкое", "среднее", "высокое" };
        public static readonly int[] FrameLimits = { 0, 30, 60, 90, 120, 144 };

        // Shadow levels: distance (m), cascades, map size. High is the shipped setting.
        static readonly (float distance, int cascades, int resolution)[] Shadows =
            { (0f, 1, 1024), (150f, 2, 2048), (250f, 3, 2048), (350f, 4, 4096) };
        static readonly float[] LodBias = { 1f, 1f, 1.5f, 2f };
        static readonly float[] GrassDistance = { 0f, 60f, 100f, 150f };

        static GraphicsQuality _instance;
        static bool _saved;
        static float _renderScale, _shadowDistance, _lodBias;
        static int _cascades, _shadowResolution;
        static UpscalingFilterSelection _filter;

        float _fps, _fpsTimer;
        int _fpsFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            _instance = new GameObject("GraphicsQuality").AddComponent<GraphicsQuality>();
            DontDestroyOnLoad(_instance.gameObject);
            ControlSettings.Changed += _ => Apply();
            SceneManager.sceneLoaded += (_, _) => ApplyScene();
            RenderPipelineManager.beginCameraRendering += OnCamera;
            Apply();
        }

        static UniversalRenderPipelineAsset Pipeline => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        public static void Apply()
        {
            var urp = Pipeline;
            if (urp == null) return;
            var s = ControlSettings.Current;
            if (!_saved)
            {
                _renderScale = urp.renderScale; _filter = urp.upscalingFilter; _shadowDistance = urp.shadowDistance;
                _cascades = urp.shadowCascadeCount; _shadowResolution = urp.mainLightShadowmapResolution; _lodBias = QualitySettings.lodBias;
                _saved = true;
            }
            urp.renderScale = Mathf.Clamp(s.renderScale, 0.5f, 1f);
            urp.upscalingFilter = s.antiAliasing == 3 ? UpscalingFilterSelection.STP
                : urp.renderScale < 0.99f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
            var sh = Shadows[Mathf.Clamp(s.shadowQuality, 0, 3)];
            if (sh.distance > 0f)
            {
                urp.shadowDistance = sh.distance;
                urp.shadowCascadeCount = sh.cascades;
                urp.mainLightShadowmapResolution = sh.resolution;
            }
            QualitySettings.lodBias = LodBias[Mathf.Clamp(s.vegetationDetail, 0, 3)];
            Application.targetFrameRate = FrameLimits[Mathf.Clamp(s.frameRateLimit, 0, FrameLimits.Length - 1)] is int f && f > 0 ? f : -1;
            ApplyScene();
        }

        /// <summary>Per-scene parts: grass distance on the terrains, the sun's shadows.</summary>
        static void ApplyScene()
        {
            var s = ControlSettings.Current;
            foreach (var t in Terrain.activeTerrains)
                t.detailObjectDistance = GrassDistance[Mathf.Clamp(s.grassQuality, 0, 3)];
            if (RenderSettings.sun != null)
                RenderSettings.sun.shadows = s.shadowQuality > 0 ? LightShadows.Soft : LightShadows.None;
        }

        static void OnCamera(ScriptableRenderContext _, Camera cam)
        {
            if (cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            int aa = ControlSettings.Current.antiAliasing;
            data.antialiasing = aa == 1 ? AntialiasingMode.FastApproximateAntialiasing
                : aa == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None; // STP brings its own
            data.antialiasingQuality = AntialiasingQuality.High;
        }

        void Update()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer < 0.5f) return;
            _fps = _fpsFrames / _fpsTimer;
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }

        void OnGUI()
        {
            if (!ControlSettings.Current.showFps) return;
            GUI.depth = -1000; // above menus and the OSD
            var t = UiTheme.T;
            var text = $"{_fps:0} FPS · {1000f / Mathf.Max(_fps, 1f):0.0} мс";
            float w = UiTheme.Width(text, t.caption) + UiTheme.Px(20f);
            var r = new Rect(Screen.width - w - UiTheme.Px(8f), UiTheme.Px(8f), w, UiTheme.Px(30f));
            UiTheme.Fill(r, UiTheme.OverVideo);
            UiTheme.Text(r, text, t.captionCenter, _fps < 30f ? UiTheme.Warning : UiTheme.TextPrimary);
        }

        void OnApplicationQuit()
        {
            if (!Application.isEditor || !_saved || Pipeline == null) return;
            var urp = Pipeline;
            urp.renderScale = _renderScale; urp.upscalingFilter = _filter; urp.shadowDistance = _shadowDistance;
            urp.shadowCascadeCount = _cascades; urp.mainLightShadowmapResolution = _shadowResolution; QualitySettings.lodBias = _lodBias;
        }
    }
}
