using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ModernDrone.Video
{
    /// <summary>
    /// Copies the scene-referred (pre-exposure, pre-tonemap) colour of the camera that carries the active
    /// <see cref="AutoExposure"/> into its small metering texture. Metering and adaptation run on the CPU.
    /// </summary>
    public sealed class AutoExposureFeature : ScriptableRendererFeature
    {
        sealed class MeterPass : ScriptableRenderPass
        {
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var ae = AutoExposure.Active;
                if (ae == null || ae.Target == null || frameData.Get<UniversalCameraData>().camera != ae.Camera) return;
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                renderGraph.AddBlitPass(resources.activeColorTexture, renderGraph.ImportTexture(ae.Target), Vector2.one, Vector2.zero, passName: "AutoExposure Meter");
            }
        }

        MeterPass _pass;

        public override void Create() => _pass = new MeterPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (AutoExposure.Active != null) renderer.EnqueuePass(_pass);
        }
    }
}
