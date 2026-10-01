// Six-way lit smoke and fire for the built-in particle system (Shuriken).
// Maps follow Unity's six-way convention: _MapA = (right, top, back, alpha), _MapB = (left, bottom, front, emission),
// baked from Mantaflow simulations by Tools/Blender/vfx_bake.py. Lighting is gathered per vertex (main light with its
// shadow, every Forward+ light, the ambient probe) into one colour per axis of the sprite, so the pixel shader is
// four texture reads and a dot product. Premultiplied blending: glowing gas adds light, smoke hides what is behind it.
//
// Vertex streams (set by SimpleVfx): Position, Normal, Color, UV, UV2, Center, AnimBlend, Custom1.xy, Tangent.
//   Custom1.x = heat (emission multiplier over the particle's life), Custom1.y = soot (darkens the gas, 0..1).
// Globals: _FpvThermal (white-hot thermal camera: cool smoke turns nearly transparent in LWIR, hot gas glows).
Shader "ModernDrone/Particles/SixWay"
{
    Properties
    {
        [NoScaleOffset] _MapA ("Map A (R right, G top, B back, A alpha)", 2D) = "black" {}
        [NoScaleOffset] _MapB ("Map B (R left, G bottom, B front, A emission)", 2D) = "black" {}
        _Tint ("Gas colour", Color) = (1, 1, 1, 1)
        _LightGain ("Direct light gain", Float) = 1
        _AmbientGain ("Ambient gain", Float) = 1
        _EmissionGain ("Emission gain (HDR)", Float) = 12
        _AlphaGain ("Opacity gain", Float) = 1
        _SoftFade ("Soft particle fade distance, m", Float) = 1.5
        _CameraFade ("Near-camera fade distance, m", Float) = 2
        _ThermalWarmth ("Thermal: warmth of the gas itself", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MapA); SAMPLER(sampler_MapA);
        TEXTURE2D(_MapB);

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _LightGain, _AmbientGain, _EmissionGain, _AlphaGain, _SoftFade, _CameraFade, _ThermalWarmth;
        CBUFFER_END
        float _FpvThermal;
        float _FpvEnabled;

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float4 uv : TEXCOORD0;      // uv, uv2 (next flipbook frame)
            float4 centerBlend : TEXCOORD1; // particle centre, frame blend
            float2 custom : TEXCOORD2;  // heat, soot
            float4 tangentOS : TANGENT;
        };

        half4 SampleBlended(TEXTURE2D_PARAM(tex, smp), float4 uv, half blend)
        {
            return lerp(SAMPLE_TEXTURE2D(tex, smp, uv.xy), SAMPLE_TEXTURE2D(tex, smp, uv.zw), blend);
        }
        ENDHLSL

        Pass
        {
            Name "SixWayForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                half4 blendHeatSootFog : TEXCOORD1;
                half3 right : TEXCOORD2;
                half3 left : TEXCOORD3;
                half3 top : TEXCOORD4;
                half3 bottom : TEXCOORD5;
                half3 back : TEXCOORD6;
                half3 front : TEXCOORD7;
                float4 screenPos : TEXCOORD8;
                half softFade : TEXCOORD9;
            };

            // One light into the six axis colours. Squared cosines sum to one, so a light between two axes is
            // shared out between their maps without a seam.
            void AddLight(half3 L, half3 color, half3 T, half3 B, half3 N, inout Varyings o)
            {
                half x = dot(L, T), y = dot(L, B), z = dot(L, N);
                o.right += color * (x > 0 ? x * x : 0);
                o.left += color * (x < 0 ? x * x : 0);
                o.top += color * (y > 0 ? y * y : 0);
                o.bottom += color * (y < 0 ? y * y : 0);
                o.front += color * (z > 0 ? z * z : 0);
                o.back += color * (z < 0 ? z * z : 0);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 positionWS = v.positionOS; // particles are simulated in world space
                o.positionCS = TransformWorldToHClip(positionWS);
                o.color = v.color;
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                // Big puffs fade over a share of their own size where they meet the ground, or the cut shows.
                o.softFade = max(_SoftFade, 0.25 * distance(positionWS, v.centerBlend.xyz));

                // Sprite frame: N toward the camera, T along the texture's u, B along v (rotates with the sprite).
                half3 N = normalize(v.normalOS);
                half3 T = normalize(v.tangentOS.xyz);
                half3 B = normalize(cross(N, T) * (v.tangentOS.w < 0 ? -1.0 : 1.0));
                // Lights are evaluated half way between the corner and the centre: big puffs keep some gradient
                // across them without a light close to a corner flaring the whole sprite.
                float3 P = lerp(v.centerBlend.xyz, positionWS, 0.5);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    Light main = GetMainLight(TransformWorldToShadowCoord(P));
                #else
                    Light main = GetMainLight();
                #endif
                AddLight(main.direction, main.color * main.shadowAttenuation * _LightGain, T, B, N, o);

                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = P;
                    inputData.normalizedScreenSpaceUV = o.screenPos.xy / o.screenPos.w;
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, P);
                        // A light inside the cloud (its own fireball, a fire under it) must not blow it out:
                        // cap the inverse-square falloff at ~4 m.
                        AddLight(l.direction, l.color * min(l.distanceAttenuation, 0.06) * _LightGain, T, B, N, o);
                    LIGHT_LOOP_END
                #endif

                // Ambient: the sky probe seen along each axis.
                half amb = 0.5 * _AmbientGain;
                o.top += SampleSH(B) * amb;
                o.bottom += SampleSH(-B) * amb;
                half3 side = (SampleSH(T) + SampleSH(-T)) * 0.5 * amb;
                o.right += side;
                o.left += side;
                o.front += SampleSH(N) * amb;
                o.back += SampleSH(-N) * amb;

                o.blendHeatSootFog = half4(v.centerBlend.w, v.custom.x, v.custom.y, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            // Glowing gas: deep red at the cool edges to yellow-white in the hot core (blackbody-like ramp).
            half3 Ramp(half t)
            {
                half3 c = lerp(half3(0.45, 0.04, 0.0), half3(1.0, 0.28, 0.03), saturate(t * 2.5));
                c = lerp(c, half3(1.0, 0.62, 0.18), saturate(t * 2.5 - 1.0));
                return lerp(c, half3(1.0, 0.78, 0.42), saturate(t * 2.5 - 1.5));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half blend = i.blendHeatSootFog.x, heat = i.blendHeatSootFog.y, soot = i.blendHeatSootFog.z;
                half4 a = SampleBlended(TEXTURE2D_ARGS(_MapA, sampler_MapA), i.uv, blend);
                half4 b = SampleBlended(TEXTURE2D_ARGS(_MapB, sampler_MapA), i.uv, blend);

                half alpha = saturate(a.a * i.color.a * _AlphaGain);
                // Soft intersection with the ground and hulls, and fade out right in front of the lens.
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float z = i.screenPos.w;
                half soft = saturate((sceneZ - z) / max(i.softFade, 1e-3)) * saturate((z - _ProjectionParams.y) / max(_CameraFade, 1e-3));
                alpha *= soft;

                half3 light = a.r * i.right + b.r * i.left + a.g * i.top + b.g * i.bottom + a.b * i.back + b.b * i.front;
                half3 albedo = i.color.rgb * _Tint.rgb * (1.0 - 0.85 * soot);
                half t = saturate(b.a * heat);
                half3 glow = Ramp(t) * (t * t * t) * _EmissionGain;

                half3 rgb = light * albedo * alpha + glow * i.color.a * soft;
                half fog = ComputeFogIntensity(i.blendHeatSootFog.w);
                rgb = lerp(unity_FogColor.rgb * alpha, rgb, fog);

                // White-hot LWIR camera: flame and hot gas glow, cooled smoke barely hides anything
                // (LWIR penetrates smoke far better than visible light).
                // Only while the thermal FPV feed is on screen (the recon camera is a day/night TV camera).
                if (_FpvThermal > 0.5 && _FpvEnabled > 0.5)
                {
                    half hot = saturate(b.a * heat * 3.0 + _ThermalWarmth * a.a * heat) * soft * i.color.a;
                    half ta = saturate(alpha * 0.2 + hot);
                    return half4(hot.xxx * ta, ta);
                }
                return half4(rgb, alpha);
            }
            ENDHLSL
        }

        // Smoke columns can shade the ground: stochastic (dithered) shadow of the puffs' alpha.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                half2 blendAlpha : TEXCOORD1;
            };

            ShadowVaryings ShadowVert(Attributes v)
            {
                ShadowVaryings o;
                float3 positionWS = v.positionOS;
                float3 normalWS = normalize(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - positionWS);
                #else
                    float3 L = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, L));
                o.positionCS = ApplyShadowClamping(positionCS);
                o.uv = v.uv;
                o.blendAlpha = half2(v.centerBlend.w, v.color.a);
                return o;
            }

            half ShadowFrag(ShadowVaryings i) : SV_Target
            {
                half a = SampleBlended(TEXTURE2D_ARGS(_MapA, sampler_MapA), i.uv, i.blendAlpha.x).a * i.blendAlpha.y * _AlphaGain;
                // Interleaved gradient noise: the shadow's density follows the smoke's opacity.
                half n = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                clip(a * 0.8 - n);
                return 0;
            }
            ENDHLSL
        }
    }
}
