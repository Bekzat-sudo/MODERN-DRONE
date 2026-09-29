// Full-screen FPV video look for URP's Full Screen Pass renderer feature.
// Driven by globals from ModernDrone.Video.VideoFeed:
//   _FpvEnabled (0/1), _FpvSignal (0..1 video quality), _FpvAnalog (1 analog, ~0.2 fiber), _FpvTime,
//   _FpvLens (0..1 fisheye strength), _FpvFrameAspect (4:3 = 1.333 pillarboxed, 0 = full screen).
Shader "Hidden/ModernDrone/AnalogVideo"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "AnalogVideo"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _FpvEnabled;
            float _FpvSignal;
            float _FpvAnalog;
            float _FpvTime;
            float _FpvThermal;
            float _FpvThermalGain;
            float _FpvThermalDrift;
            float4 _FpvThermalRes;
            float _FpvLens;
            float _FpvFrameAspect;

            // Wide-angle FPV lens: barrel distortion of the rendered (rectilinear) image inside the camera's frame.
            // The horizontal frame edge samples the far edge of the render, the corners fall off into darkness,
            // like a 2.1 mm fisheye on a 4:3 analog camera. Returns the source uv; `inside` is 0 outside the image.
            float2 Lens(float2 uv, out float inside)
            {
                float screenAspect = _ScreenParams.x / _ScreenParams.y;
                float frameAspect = _FpvFrameAspect > 0.1 ? min(_FpvFrameAspect, screenAspect) : screenAspect;
                float fw = frameAspect / screenAspect;                  // frame width in screen uv
                float2 f = float2((uv.x - 0.5) / fw, uv.y - 0.5);        // -0.5..0.5 inside the frame
                inside = step(abs(f.x), 0.5);
                float2 p = float2(f.x * frameAspect, f.y) * 2.0;          // frame half-height = 1
                float r2 = dot(p, p) / (frameAspect * frameAspect);       // 1 at the horizontal edge
                float k = _FpvLens * (screenAspect / frameAspect - 1.0 + 0.25);
                float2 q = p * (1.0 + k * r2) / (1.0 + k * 0.5625);        // keep the vertical edge on the image
                float2 src = 0.5 + float2(q.x / (2.0 * screenAspect), q.y * 0.5);
                float2 over = max(abs(src - 0.5) - 0.5, 0.0);
                inside *= saturate(1.0 - length(over) * 40.0);             // soft dark corners where the lens runs out
                return src;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half ThermalLum(float2 uv)
            {
                return dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb, half3(0.299, 0.587, 0.114));
            }

            half3 SampleRgbShift(float2 uv, float shift)
            {
                half r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(shift, 0)).r;
                half g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                half b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(shift, 0)).b;
                return half3(r, g, b);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_FpvEnabled < 0.5)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float inside;
                uv = Lens(uv, inside);
                if (inside <= 0.0) return half4(0, 0, 0, 1);

                float signal = saturate(_FpvSignal);
                float bad = 1.0 - signal;
                float analog = _FpvAnalog;
                float t = _FpvTime;
                float frame = floor(t * 30.0);

                // Horizontal line jitter, grows with interference.
                float lineIdx = floor(uv.y * 240.0);
                float jitter = (Hash(float2(lineIdx, frame)) - 0.5) * 0.03 * bad * bad * analog;

                // Tearing bands: whole groups of lines shifted sideways.
                float bandRow = floor(uv.y * 18.0);
                float band = step(1.0 - bad * 0.35, Hash(float2(bandRow, floor(t * 14.0))));
                jitter += band * (Hash(float2(lineIdx, frame + 7.0)) - 0.5) * 0.2 * bad * analog;

                // Vertical sync loss (picture rolls) when the signal is very weak.
                float roll = bad > 0.7 ? frac(t * 0.9) * saturate((bad - 0.7) * 3.3) * analog : 0.0;
                // Only a rolling picture wraps vertically; otherwise stay on the image (no sky at the bottom corners).
                float2 uvDistorted = float2(uv.x + jitter, roll > 0.0 ? frac(uv.y + roll) : saturate(uv.y));

                // A strong analog link is nearly clean (strike footage): artefacts come with a weakening signal.
                float shift = (0.0005 + 0.007 * bad) * analog;
                half3 col = SampleRgbShift(uvDistorted, shift);

                // Thermal camera (white-hot) as seen through a CVBS FPV feed: core resolution from the fitted camera,
                // AGC-driven gain, detail enhancement (halo around hot objects), column fixed-pattern noise and a
                // cloudy non-uniformity that builds up between shutter calibrations (flyeye.io FFC notes).
                if (_FpvThermal > 0.5)
                {
                    float2 res = max(_FpvThermalRes.xy, float2(64.0, 48.0));
                    float2 px = 1.0 / res;
                    float2 tuv = (floor(uvDistorted * res) + 0.5) / res;
                    half l = ThermalLum(tuv);
                    half blur = (ThermalLum(tuv + float2(px.x * 2.0, 0)) + ThermalLum(tuv - float2(px.x * 2.0, 0)) +
                                 ThermalLum(tuv + float2(0, px.y * 2.0)) + ThermalLum(tuv - float2(0, px.y * 2.0))) * 0.25;
                    l = l + (l - blur) * 1.5;
                    l = saturate(pow(saturate(l * _FpvThermalGain), 0.7));
                    l += (Hash(float2(floor(tuv.x * res.x), 3.0)) - 0.5) * 0.015;
                    float cloud = sin(uv.x * 4.7 + 1.3) * sin(uv.y * 3.9 + 0.4) + 0.5 * sin(uv.x * 9.1 - uv.y * 6.3);
                    float2 v = uv - 0.5;
                    l += (cloud * 0.05 - dot(v, v) * 0.25) * _FpvThermalDrift;
                    col = saturate(l).xxx;
                }

                // Colour fades out as the signal weakens.
                half lum = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, saturate(bad * 1.3) * 0.85 * analog);

                // Grain -> snow.
                float n = Hash(uv * float2(720.0, 480.0) + frac(t * 17.0) * 100.0);
                float snow = (0.012 + pow(bad, 2.4) * 0.95) * analog;
                col = lerp(col, n.xxx, saturate(snow));

                // Scanlines and vignette (lens + CRT feel).
                col *= 1.0 - (0.025 + 0.06 * bad) * analog * (0.5 + 0.5 * sin(uv.y * 960.0));
                float2 d = uv - 0.5;
                col *= 1.0 - dot(d, d) * 0.7;

                // No link: analog shows static, fiber (cable cut) goes black.
                if (signal <= 0.02)
                    col = analog > 0.5 ? n.xxx * 0.55 : half3(0, 0, 0);

                return half4(col * inside, 1.0);
            }
            ENDHLSL
        }
    }
}
