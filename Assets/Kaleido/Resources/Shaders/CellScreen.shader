// Dark-field glowing beads (SDF quads, analytic anti-aliasing): screen blend onto a
// black-cleared cell texture (PRD section 4).
Shader "Kaleido/CellScreen"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Screen"
            Blend One OneMinusSrcColor
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "KaleidoCommon.hlsl"
            struct A { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float2 uv3 : TEXCOORD2; };
            struct V { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float2 uv3 : TEXCOORD2; };

            V Vert(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.color = IN.color;
                o.uv = IN.uv;
                o.uv2 = IN.uv2;
                o.uv3 = IN.uv3;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                float sd = SdFragment(IN.uv, IN.uv2.x, IN.uv3.x);
                float aa = max(fwidth(sd), 1e-4);
                float cover = 1.0 - smoothstep(-aa, aa, sd);
                if (cover <= 0.002) discard;
                // bead: luminous core, brighter rim ring, soft halo just outside the edge
                float depth = saturate(-sd * 1.4);
                float core = 0.55 + 0.45 * depth;
                float rim = smoothstep(0.0, 0.12, -sd) * (1.0 - smoothstep(0.12, 0.3, -sd));
                float3 col = IN.color.rgb * core + rim * (IN.color.rgb * 0.5 + 0.35);
                col *= IN.color.a * cover;
                return float4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
