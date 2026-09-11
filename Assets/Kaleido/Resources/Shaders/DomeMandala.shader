// Scene 6: mandala. Lace of thin luminous lines with 16-fold symmetry flowing outward from the
// zenith (base speed 1x). Built only from smooth periodic functions of (r - t), so motion is
// continuous and the rotation of any angular term stays far below 30 deg/s.
Shader "Kaleido/DomeMandala"
{
    Properties
    {
        _EvoTime ("Evolution time", Float) = 0
        _Speed ("Flow speed", Float) = 1
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
                float _EvoTime;
                float _Speed;
                float _Fade;
            CBUFFER_END

            // thin line where s (in [-1,1]) crosses 1: width in the same units
            float Line(float s, float width) { return 1.0 - smoothstep(0.0, width, abs(1.0 - s)); }

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 d = IN.dirOS;
                float phi, theta;
                DomePolar(d, phi, theta);
                float t = _EvoTime * _Speed;
                float r = phi;                                   // 0 at zenith .. pi/2 at horizon
                // fold into 16 petals (mirror symmetric), so all angular terms are seamless
                float w = KALEIDO_PI / 16.0;
                float a = FoldSector(theta + t * 0.01, w) / w;   // 0..1 inside a petal
                float pet = cos(a * KALEIDO_PI);                 // 1 on petal centre, -1 on mirror

                // anti-aliasing width from radius (thicker far out)
                float lw = 0.06 + 0.05 * r;

                // family A: outward flowing scallops
                float sA = sin(r * 14.0 - t * 0.6 + pet * 0.9);
                float lineA = Line(sA, lw);
                // family B: slower, larger arcs bending the other way
                float sB = sin(r * 7.0 - t * 0.3 - pet * 1.4 + 1.0);
                float lineB = Line(sB, lw * 1.2);
                // family C: fine petals between the arcs
                float sC = sin(r * 22.0 - t * 0.9 + pet * 2.2 + 2.0);
                float lineC = Line(sC, lw * 0.8) * smoothstep(0.15, 0.45, r);
                // radial spokes on the mirrors, faint
                float spoke = 1.0 - smoothstep(0.0, 0.05 + 0.02 * r, (1.0 - abs(pet)) * max(r, 0.05));

                // colours per family, cycling slowly with radius so rings differ
                float3 cA = KPalette(r * 0.9 + 0.0 + t * 0.005, float3(0.55, 0.35, 0.65), float3(0.45, 0.35, 0.35), float3(1.0, 1.0, 1.0), float3(0.7, 0.4, 0.2));
                float3 cB = float3(0.35, 0.55, 1.0);
                float3 cC = float3(1.0, 0.45, 0.75);
                float3 bg = lerp(float3(0.02, 0.01, 0.06), float3(0.10, 0.03, 0.18), 0.5 + 0.5 * sin(r * 7.0 - t * 0.3));
                float3 col = bg;
                col += cA * lineA * 0.9;
                col += cB * lineB * 0.7;
                col += cC * lineC * 0.55;
                col += float3(0.5, 0.4, 0.8) * spoke * 0.25 * smoothstep(0.05, 0.3, r);
                // soft glow halo around the bright lines
                float glow = (Line(sA, lw * 3.0) + Line(sB, lw * 3.5)) * 0.15;
                col += glow * float3(0.5, 0.3, 0.6);
                col *= 1.0 - 0.6 * smoothstep(2.0, 2.55, phi);
                col *= HorizonMask(d);
                return KaleidoOutput(col, _Fade);
            }
            ENDHLSL
        }
    }
}
