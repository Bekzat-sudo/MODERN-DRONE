// Inverted-hull outline for the hangar: back faces pushed out along the normal by a fixed width in pixels.
Shader "ModernDrone/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.95, 0.62, 0.15, 1)
        _Width ("Width (px)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 n = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(v.normalOS));
                float2 dir = normalize(n.xy + 1e-5);
                o.positionCS.xy += dir * _Width * 2.0 * o.positionCS.w / _ScreenParams.xy;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
