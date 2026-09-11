// Dark-field glowing beads: screen blend onto a black-cleared cell texture (PRD section 4).
Shader "Kaleido/CellScreen"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        // ---- Pass 1: screen (dark-field glowing beads)
        Pass
        {
            Name "Screen"
            Blend One OneMinusSrcColor
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "KaleidoCommon.hlsl"

            struct A { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };

            V Vert(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.color = IN.color;
                o.uv = IN.uv;
                o.uv2 = IN.uv2;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                float rr = length(IN.uv);
                // bead: bright core, glowing rim, soft edge
                float core = 1.0 - smoothstep(0.0, 0.75, rr);
                float rim = smoothstep(0.55, 0.85, rr) * (1.0 - smoothstep(0.85, 1.0, rr));
                float3 col = IN.color.rgb * (0.45 + 0.55 * core) + rim * (IN.color.rgb * 0.6 + 0.25);
                col *= IN.color.a;
                col *= 1.0 - smoothstep(0.92, 1.0, rr);
                return float4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
