// Composites the per-eye 0.6x-resolution Mandelbulb textures onto the proxy sphere in the
// main XR pass. Screen-space lookup: the helper cameras used the exact per-eye view and
// projection matrices, so NDC coordinates match 1:1 regardless of resolution.
//
// Upsampling: a 4-tap rotated-grid (RGSS) reconstruction, each tap bilinear, offset by
// (+-0.375, +-0.125) texel. The helper target is cleared to (0,0,0,0) and the bulb writes
// (colour, 1), so every bilinear sample is already coverage-PREMULTIPLIED colour (black
// clear) with alpha = coverage. The taps are averaged in that premultiplied form and blended
// with premultiplied-alpha blending, which keeps edge coverage exact (no dark fringe, no
// double alpha) while softening edges and residual noise.
Shader "Kaleido/BulbComposite"
{
    Properties
    {
        _BulbTexL ("Left", 2D) = "black" {}
        _BulbTexR ("Right", 2D) = "black" {}
        _Fade ("Fade", Range(0, 1)) = 1
        _Proximity ("Proximity fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+2" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One OneMinusSrcAlpha      // premultiplied: rgb already scaled by coverage * fade
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
                float4 _BulbTexL_TexelSize;   // (1/w, 1/h, w, h), filled by Unity for the texture property
                float4 _BulbTexR_TexelSize;
                float _Fade;
                float _Proximity;   // 1 normally, 0 when the head is inside / too close to the object
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

            // 4-tap rotated-grid sample; returns premultiplied colour (rgb) and coverage (a), both averaged.
            float4 SampleRGSS(TEXTURE2D_PARAM(tex, smp), float2 uv, float2 texel)
            {
                const float2 o0 = float2( 0.375,  0.125);
                const float2 o1 = float2(-0.125,  0.375);
                const float2 o2 = float2(-0.375, -0.125);
                const float2 o3 = float2( 0.125, -0.375);
                float4 s0 = SAMPLE_TEXTURE2D(tex, smp, uv + o0 * texel);
                float4 s1 = SAMPLE_TEXTURE2D(tex, smp, uv + o1 * texel);
                float4 s2 = SAMPLE_TEXTURE2D(tex, smp, uv + o2 * texel);
                float4 s3 = SAMPLE_TEXTURE2D(tex, smp, uv + o3 * texel);
                return (s0 + s1 + s2 + s3) * 0.25;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float4 s;
                if (unity_StereoEyeIndex == 0)
                    s = SampleRGSS(TEXTURE2D_ARGS(_BulbTexL, sampler_BulbTexL), uv, _BulbTexL_TexelSize.xy);
                else
                    s = SampleRGSS(TEXTURE2D_ARGS(_BulbTexR, sampler_BulbTexR), uv, _BulbTexR_TexelSize.xy);
                // s.rgb is coverage-premultiplied colour, s.a is coverage: scale both by the crossfade.
                return s * saturate(_Fade) * saturate(_Proximity);
            }
            ENDHLSL
        }
    }
}
