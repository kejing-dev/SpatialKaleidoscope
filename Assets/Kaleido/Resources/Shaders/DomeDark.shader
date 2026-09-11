// Quiet dark backdrop used behind the 3D-object scenes (Mandelbulb, tree, Lorenz).
// Static gradient with a faint, non-twinkling star field.
Shader "Kaleido/DomeDark"
{
    Properties
    {
        _Fade ("Fade", Range(0, 1)) = 1
        _TopColor ("Top", Color) = (0.05, 0.06, 0.12, 1)
        _BottomColor ("Bottom", Color) = (0.01, 0.01, 0.02, 1)
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

            CBUFFER_START(UnityPerMaterial)
                float _Fade;
                float4 _TopColor;
                float4 _BottomColor;
            CBUFFER_END

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 d = IN.dirOS;
                float up = saturate(d.y * 0.5 + 0.5);
                float3 col = lerp(_BottomColor.rgb, _TopColor.rgb, smoothstep(0.35, 1.0, up));
                // static stars: hash on a coarse grid of direction, no time term (no twinkle)
                float3 g = floor(d * 90.0);
                float h = KHash31(g);
                float star = step(0.985, h) * saturate((h - 0.985) * 40.0);
                float3 cellCentre = (g + 0.5) / 90.0;
                float dist = length(d * 90.0 - (g + 0.5));
                star *= 1.0 - smoothstep(0.15, 0.35, dist);
                col += star * 0.35 * smoothstep(0.05, 0.4, d.y);
                return KaleidoOutput(col, _Fade);
            }
            ENDHLSL
        }
    }
}
