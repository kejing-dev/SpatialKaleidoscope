// Dome that shows a pre-rendered 2D texture (Julia / Mandelbrot) mapped with the
// azimuthal-equidistant projection from the zenith. Eye-independent: the fractal is
// computed once per frame into a texture, not per eye.
Shader "Kaleido/DomeTexture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Scale ("Plane scale", Float) = 0.75
        _Fade ("Fade", Range(0, 1)) = 1
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex DomeVert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "KaleidoCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _Scale;
                float _Fade;
                float4 _Tint;
            CBUFFER_END

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 d = IN.dirOS;
                float2 p = DomePlane(d) * _Scale;          // plane, radius ~ 1 at phi = 1/_Scale
                float2 uv = p * 0.5 + 0.5;
                float inside = 1.0 - smoothstep(0.93, 1.0, length(p));
                float2 cuv = clamp(uv, 0.002, 0.998);
                float3 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, cuv).rgb * _Tint.rgb;
                float3 border = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, cuv, 6.0).rgb * 0.35;
                col = lerp(border, col, inside);
                col *= HorizonMask(d);
                return KaleidoOutput(col, _Fade);
            }
            ENDHLSL
        }
    }
}
