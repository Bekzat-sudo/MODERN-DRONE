using UnityEngine;

namespace ModernDrone.Video
{
    /// <summary>Current link state for the HUD (OSD) and the full-screen video shader.</summary>
    public struct LinkTelemetry
    {
        public LinkType type;
        public float video;          // 0..1
        public float control;        // 0..1 (LQ)
        public float latency;        // s
        public float jam;            // 0..1 strongest jammer level at the drone
        public float cableUsed;      // m (fiber)
        public float spoolLength;    // m (fiber)
        public bool failsafe;
        public bool cableCut;
    }

    /// <summary>
    /// Publishes the drone's link state and drives the global shader parameters used by the
    /// analog-video full-screen pass. The pass is a no-op unless <see cref="Enabled"/> is set.
    /// </summary>
    public static class VideoFeed
    {
        static readonly int EnabledId = Shader.PropertyToID("_FpvEnabled");
        static readonly int SignalId = Shader.PropertyToID("_FpvSignal");
        static readonly int AnalogId = Shader.PropertyToID("_FpvAnalog");
        static readonly int TimeId = Shader.PropertyToID("_FpvTime");
        static readonly int ThermalId = Shader.PropertyToID("_FpvThermal");
        static readonly int ThermalGainId = Shader.PropertyToID("_FpvThermalGain");
        static readonly int ThermalDriftId = Shader.PropertyToID("_FpvThermalDrift");

        public static LinkTelemetry Current { get; private set; }
        public static bool HasLink { get; private set; }

        public static void Publish(in LinkTelemetry telemetry)
        {
            Current = telemetry;
            HasLink = true;
            Shader.SetGlobalFloat(SignalId, telemetry.video);
            // Fiber carries a clean picture; analog shows its characteristic artefacts even when strong.
            Shader.SetGlobalFloat(AnalogId, telemetry.type == LinkType.Radio ? 1f : 0.2f);
            Shader.SetGlobalFloat(TimeId, Time.unscaledTime);
            Shader.SetGlobalFloat(ThermalId, ModernDrone.Core.NightMode.Thermal ? 1f : 0f);
            Shader.SetGlobalFloat(ThermalGainId, ModernDrone.Core.NightMode.ThermalGain);
            Shader.SetGlobalFloat(ThermalDriftId, ModernDrone.Core.NightMode.ThermalDrift);
        }

        /// <summary>Effect applies only while the FPV camera is the one being watched.</summary>
        public static void SetEnabled(bool enabled) => Shader.SetGlobalFloat(EnabledId, enabled ? 1f : 0f);

        public static void Clear()
        {
            HasLink = false;
            SetEnabled(false);
        }
    }
}
