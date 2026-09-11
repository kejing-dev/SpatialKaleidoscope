// Left / right navigation buttons (resident, below the horizon). A rounded pill with an arrow
// glyph, all SDF so it stays crisp at any distance. _Dir = -1 (previous) / +1 (next).
Shader "Kaleido/NavButton"
{
    Properties
    {
        _Dir ("Direction", Float) = 1
        _Hover ("Hover", Range(0, 1)) = 0
        _Press ("Press", Range(0, 1)) = 0
        _Color ("Colour", Color) = (0.75, 0.82, 1.0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" }
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
                float _Dir;
                float _Hover;
                float _Press;
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
                o.uv = IN.uv * 2.0 - 1.0;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float2 p = IN.uv;
                float aa = max(fwidth(p.x), 0.002) * 1.5;
                // pill background
                float bg = SdRoundBox(p, float2(0.92, 0.62), 0.45);
                float bgCover = 1.0 - smoothstep(-aa, aa, bg);
                float ring = 1.0 - smoothstep(0.0, aa * 2.0, abs(bg + 0.05));
                // arrow glyph: triangle pointing along _Dir plus a short bar
                float2 q = float2(p.x * _Dir, p.y);
                // SdTriangle points toward +y; rotate so the arrow points toward +q.x
                float tri = SdTriangle(float2(-q.y, q.x) + float2(0.0, 0.05), 0.4);
                float glyph = 1.0 - smoothstep(-aa, aa, tri);

                float lit = 0.45 + 0.55 * _Hover + 0.6 * _Press;
                float3 col = _Color.rgb * lit;
                float alpha = bgCover * (0.28 + 0.25 * _Hover) + ring * (0.5 + 0.5 * _Hover);
                col = lerp(col * 0.25, col * (1.0 + 0.6 * _Press), glyph);
                alpha = max(alpha, glyph * (0.85 + 0.15 * _Hover));
                float4 o = KaleidoOutput(col, alpha * bgCover + ring * 0.6 + glyph);
                o.a = saturate(alpha);
                return o;
            }
            ENDHLSL
        }
    }
}
