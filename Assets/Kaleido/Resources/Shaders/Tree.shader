// Scene 9: L-system tree. Each branch segment grows from its base point: uv2 = (birth, duration)
// in growth-normalised time, TEXCOORD1 = base position. Growth 0..1 over 30 s, then hold, then fade.
Shader "Kaleido/Tree"
{
    Properties
    {
        _Growth ("Growth 0..1", Float) = 0
        _Fade ("Fade", Range(0, 1)) = 1
        _Color ("Colour", Color) = (0.55, 0.85, 0.5, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "KaleidoCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Growth;
                float _Fade;
                float4 _Color;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 basePos : TEXCOORD1; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD2; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float g : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            V Vert(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float g = saturate((_Growth - IN.uv2.x) / max(IN.uv2.y, 1e-4));
                g = g * g * (3.0 - 2.0 * g);
                float3 pos = IN.basePos + (IN.positionOS.xyz - IN.basePos) * g;
                o.positionCS = TransformObjectToHClip(pos);
                o.uv = IN.uv;
                o.color = IN.color;
                o.g = g;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 col = IN.color.rgb * _Color.rgb;
                // slight glow at the segment centre line (uv.x across the segment)
                float across = abs(IN.uv.x * 2.0 - 1.0);
                float a = (1.0 - smoothstep(0.6, 1.0, across)) * IN.color.a;
                return KaleidoOutput(col * (1.1 - 0.3 * across), a * _Fade * step(0.001, IN.g));
            }
            ENDHLSL
        }
    }
}
