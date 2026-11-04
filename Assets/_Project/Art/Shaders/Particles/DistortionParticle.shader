// Refraction for particles: the blast wave as an expanding ring of bent light, and heat haze shimmering over fires.
// Bends the opaque scene (_CameraOpaqueTexture); drawn just before the other transparent effects so flames and smoke
// stay on top. The shock front itself is invisible; what shows on footage is the background displaced along a thin
// expanding shell (and, in humid air, a brief condensation shell, not modelled). Vertex colour alpha = strength.
// Streams: Position, Color, UV. The haze moves with _FxTime (game time: it freezes with the world).
Shader "ModernDrone/Particles/Distortion"
{
    Properties
    {
        [KeywordEnum(Ring, Haze)] _Mode ("Mode", Float) = 0
        _Strength ("Strength (screen fraction)", Float) = 0.02
        _RingWidth ("Ring width", Range(0.02, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Distortion"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _MODE_RING _MODE_HAZE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength, _RingWidth;
            CBUFFER_END
            float _FxTime;

            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float4 screenPos : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(v.positionOS);
                o.color = v.color;
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453);
                float b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453);
                float d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float r = length(c) * 2.0;
                float2 offset;
                half mask;
                #if defined(_MODE_HAZE)
                    // Rising shimmer, strongest low in the sprite, gone at its edges.
                    float t = _FxTime;
                    float2 n = float2(Noise(i.uv * float2(6, 3) + float2(0, -t * 2.2)), Noise(i.uv * float2(6, 3) + float2(5.3, -t * 2.7))) - 0.5;
                    mask = saturate(1.0 - r) * saturate(1.3 - i.uv.y);
                    offset = n * mask;
                #else
                    // Thin shell at the sprite's rim, pushing the background outward.
                    float ring = saturate(1.0 - abs(r - (1.0 - _RingWidth)) / _RingWidth);
                    mask = ring * ring;
                    offset = (r > 1e-4 ? c / r : 0) * mask;
                #endif
                float2 suv = i.screenPos.xy / i.screenPos.w;
                // Only bend what lies behind the effect.
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half behind = saturate((sceneZ - i.screenPos.w) * 0.5);
                float2 duv = suv + offset * _Strength * i.color.a * behind;
                half3 col = SampleSceneColor(duv);
                return half4(col, saturate(mask * 4.0) * i.color.a * behind);
            }
            ENDHLSL
        }
    }
}
