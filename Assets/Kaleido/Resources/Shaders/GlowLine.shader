// Scene 10: view-facing glowing ribbon (Lorenz trail + ghost attractor).
// normal = tangent direction, uv.x = side (-1/1), uv.y = age 0 (head) .. 1 (tail end).
Shader "Kaleido/GlowLine"
{
    Properties
    {
        _Width ("Width", Float) = 0.01
        _Color ("Colour", Color) = (0.4, 0.9, 1, 1)
        _Fade ("Fade", Range(0, 1)) = 1
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "KaleidoCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Width;
                float4 _Color;
                float _Fade;
                float _Intensity;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            V Vert(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 tanWS = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                float3 viewWS = normalize(GetCameraPositionWS() - posWS);
                float3 side = normalize(cross(viewWS, tanWS));
                float age = IN.uv.y;
                float w = _Width * (1.0 - 0.6 * age);
                posWS += side * IN.uv.x * w;
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = IN.uv;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float across = abs(IN.uv.x);
                float core = 1.0 - smoothstep(0.0, 0.35, across);
                float halo = 1.0 - smoothstep(0.2, 1.0, across);
                float age = IN.uv.y;
                float life = 1.0 - age;
                float3 col = (_Color.rgb * halo * 0.6 + core * (0.5 + 0.5 * _Color.rgb)) * life * _Intensity;
                return KaleidoOutput(col * _Fade, 1.0);
            }
            ENDHLSL
        }
    }
}
