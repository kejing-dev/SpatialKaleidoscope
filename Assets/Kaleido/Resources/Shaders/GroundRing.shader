// Faint static ground ring at the user's feet (visual reference only, PRD section 7).
Shader "Kaleido/GroundRing"
{
    Properties
    {
        _Color ("Colour", Color) = (0.5, 0.55, 0.65, 0.35)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+3" "RenderPipeline" = "UniversalPipeline" }
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
                float4 _Color;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            V Vert(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                // uv.x = radial 0 (inner) .. 1 (outer)
                float a = smoothstep(0.0, 0.25, IN.uv.x) * (1.0 - smoothstep(0.75, 1.0, IN.uv.x));
                return KaleidoOutput(_Color.rgb, a * _Color.a);
            }
            ENDHLSL
        }
    }
}
