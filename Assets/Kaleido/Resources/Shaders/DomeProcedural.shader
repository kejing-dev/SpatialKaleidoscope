// Scene 5: procedural kaleidoscope. 12-fold mirror fold with a slowly rotating phase
// (one turn / 60 s = 6 deg/s) over a drifting stained-glass field built from two layers of
// cellular noise (crisp cell borders, one colour per cell) and soft radial rings.
// Every term is a smooth function of time: no flicker, no popping.
Shader "Kaleido/DomeProcedural"
{
    Properties
    {
        _Sectors ("Sectors", Float) = 12
        _Phase ("Phase (rad)", Float) = 0
        _Drift ("Drift", Vector) = (0, 0, 0, 0)
        _Fade ("Fade", Range(0, 1)) = 1
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
                float _Sectors;
                float _Phase;
                float4 _Drift;
                float _Fade;
            CBUFFER_END

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 d = IN.dirOS;
                float phi, theta;
                DomePolar(d, phi, theta);

                float w = KALEIDO_PI / _Sectors;             // mirror angle
                float a = FoldSector(theta + _Phase, w);     // [0, w]
                float r = phi * 1.25;                        // radial coordinate (rad -> plane)

                // wedge -> plane, then slow drift + gentle domain warp
                float2 p = r * float2(cos(a), sin(a));
                float2 warp = float2(KNoise2(p * 2.0 + _Drift.xy), KNoise2(p * 2.0 + 7.3 - _Drift.yx)) - 0.5;
                float2 q = p * 5.5 + warp * 0.35 + _Drift.xy * 0.5;

                // layer 1: large glass cells
                float f1, f2; float2 id;
                KCellular(q, 0.9, f1, f2, id);
                float edge1 = smoothstep(0.0, 0.08, f2 - f1);           // 0 on borders
                float hue1 = KHash21(id) ;
                float3 col1 = KPalette(hue1 + _Drift.w * 0.02, float3(0.5, 0.5, 0.5), float3(0.5, 0.5, 0.5), float3(1.0, 1.0, 1.0), float3(0.0, 0.33, 0.67));

                // layer 2: finer facets inside the cells
                float g1, g2; float2 id2;
                KCellular(q * 2.7 + 3.1, 0.8, g1, g2, id2);
                float edge2 = smoothstep(0.0, 0.12, g2 - g1);
                float shade2 = 0.75 + 0.25 * KHash21(id2);

                // radial rings drifting outward and a soft centre highlight
                float rings = 0.5 + 0.5 * cos(r * 7.0 - _Drift.z * 0.25);
                float centre = 1.0 - smoothstep(0.0, 0.35, r);

                float3 col = col1 * (0.55 + 0.45 * rings) * shade2;
                col *= 0.35 + 0.65 * edge1;                // dark leading between cells
                col *= 0.8 + 0.2 * edge2;                  // faint facet lines
                col += centre * float3(0.25, 0.22, 0.12);
                // bright bevel just inside each border
                float bevel = smoothstep(0.08, 0.16, f2 - f1) * (1.0 - smoothstep(0.16, 0.3, f2 - f1));
                col += bevel * 0.12;
                // mirror seams (thin, faint) and horizon vignette
                float seam = 1.0 - smoothstep(0.0, 0.01, min(a, w - a) * max(r, 0.05));
                col = lerp(col, col * 0.6, seam);
                col *= 1.0 - 0.55 * smoothstep(1.0, 1.6, phi);
                col *= HorizonMask(d);
                return KaleidoOutput(col, _Fade);
            }
            ENDHLSL
        }
    }
}
