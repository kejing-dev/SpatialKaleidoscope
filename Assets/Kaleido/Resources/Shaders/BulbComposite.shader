// Composites the per-eye half-resolution Mandelbulb textures onto the proxy sphere in the
// main XR pass. Screen-space lookup: the helper cameras used the exact per-eye view and
// projection matrices, so NDC coordinates match 1:1 regardless of resolution.
Shader "Kaleido/BulbComposite"
{
    Properties
    {
        _BulbTexL ("Left", 2D) = "black" {}
        _BulbTexR ("Right", 2D) = "black" {}
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "KaleidoCommon.hlsl"

            TEXTURE2D(_BulbTexL); SAMPLER(sampler_BulbTexL);
            TEXTURE2D(_BulbTexR); SAMPLER(sampler_BulbTexR);

            CBUFFER_START(UnityPerMaterial)
                float _Fade;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            V Vert(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float4 s;
                if (unity_StereoEyeIndex == 0)
                    s = SAMPLE_TEXTURE2D(_BulbTexL, sampler_BulbTexL, uv);
                else
                    s = SAMPLE_TEXTURE2D(_BulbTexR, sampler_BulbTexR, uv);
                // helper textures are cleared to (0,0,0,0); alpha = coverage
                return float4(s.rgb, s.a * _Fade);
            }
            ENDHLSL
        }
    }
}
