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
                float outside = max(sd, 0.0);
                float3 col;
                if (IN.uv3.y > 1.5)
                {
                    // crystal shard: saturated body with a bright cut edge, a flat facet highlight and a
                    // pale inner glow; sharp SDF edge, soft halo outside
                    float seed = IN.uv2.y;
                    float depth = saturate(-sd * 1.3);
                    float edge = smoothstep(0.0, 0.06, -sd) * (1.0 - smoothstep(0.06, 0.16, -sd));
                    float2 fdir = normalize(float2(0.75, 0.55 + 0.5 * seed));
                    float facet = smoothstep(0.3, 0.04, abs(dot(IN.uv, fdir) - (0.2 - 0.35 * seed)));
                    float3 base = IN.color.rgb * (0.55 + 0.35 * depth);
                    float3 body = base + edge * (IN.color.rgb * 0.4 + 0.5) + facet * depth * (0.25 + IN.color.rgb * 0.25);
                    float halo = exp(-outside * 7.0) * 0.28 + exp(-outside * 2.2) * 0.05;   // tight halo: shards stay crisp, background stays dark
                    col = body * cover + IN.color.rgb * halo * (1.0 - cover);
                }
                else
                {
                    // bead: luminous core, thin bright rim ring
                    float depth = saturate(-sd * 1.4);
                    float core = 0.5 + 0.5 * depth;
                    float rim = smoothstep(0.0, 0.07, -sd) * (1.0 - smoothstep(0.07, 0.2, -sd));
                    float3 bead = IN.color.rgb * core + rim * (IN.color.rgb * 0.45 + 0.45);
                    // analytic halo: smooth exponential falloff of the signed distance outside the bead,
                    // computed at full texture resolution (no blurred mip -> no blocky glow)
                    float halo = exp(-outside * 5.5) * 0.55 + exp(-outside * 1.8) * 0.12;
                    col = bead * cover + IN.color.rgb * halo * (1.0 - cover);
                }
                col *= IN.color.a;
                return float4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
