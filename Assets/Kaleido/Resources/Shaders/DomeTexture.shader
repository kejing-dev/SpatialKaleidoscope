// Dome that shows a pre-rendered 2D texture (Julia / Mandelbrot) mapped with the
// azimuthal-equidistant projection from the zenith. Eye-independent: the fractal is
// computed once per frame into a texture, not per eye.
Shader "Kaleido/DomeTexture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Scale ("Plane scale", Float) = 0.526
        _FoldRadius ("Fold radius (rad from zenith)", Float) = 1.9
        _LodBias ("Mip bias (blur)", Float) = 0
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
                float _FoldRadius;
                float _LodBias;
                float _Fade;
                float4 _Tint;
            CBUFFER_END

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 d = IN.dirOS;
                float phi, theta;
                DomePolar(d, phi, theta);
                // texture disc covers phi <= _FoldRadius; beyond it the image is mirrored back
                // (continuous) so the pattern reaches far below the horizon.
                float phiF = RadialFold(phi, _FoldRadius);
                float2 p = phiF * float2(sin(theta), cos(theta)) * _Scale;
                float2 uv = p * 0.5 + 0.5;
                float2 cuv = clamp(uv, 0.002, 0.998);
                // gradients from the unfolded coordinate keep mip selection seamless at the fold
                float2 p0 = DomePlane(d) * _Scale;
                float gs = 0.5 * exp2(_LodBias);
                float3 col = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, cuv, ddx(p0) * gs, ddy(p0) * gs).rgb * _Tint.rgb;
                col *= HorizonMask(d);
                return KaleidoOutput(col, _Fade);
            }
            ENDHLSL
        }
    }
}
