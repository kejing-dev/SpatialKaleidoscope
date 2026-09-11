// Backlit glass fragments (SDF quads, analytic anti-aliasing): multiply blend onto a
// white-cleared cell texture (PRD section 4). uv = local [-1,1]^2, uv2 = (shape id, seed),
// uv3 = (aspect, style), colour = tint + opacity.
Shader "Kaleido/CellMultiply"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Multiply"
            Blend DstColor Zero
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

                float seed = IN.uv2.y;
                float n = KNoise2(IN.uv * 2.5 + seed * 23.0);
                float3 tint = lerp(IN.color.rgb, IN.color.rgb * IN.color.rgb, 0.3);   // slightly deeper glass, still luminous
                // glass body: thicker (darker) toward the rim, bright bevel just inside the edge
                float depth = saturate(-sd * 1.6);                                    // 0 at edge .. 1 inside
                float bevel = smoothstep(0.0, 0.18, -sd) * (1.0 - smoothstep(0.18, 0.42, -sd));
                float3 body = tint * (0.9 + 0.1 * depth) * (0.94 + 0.06 * n);
                body = lerp(body, body * 0.6 + 0.25, bevel * 0.45);                  // light catching the bevel
                float opacity = IN.color.a;
                if (IN.uv3.y > 0.5)
                {
                    // organic petal / feather: translucent centre, faint veins
                    float vein = 0.5 + 0.5 * cos(IN.uv.y * 9.0 + IN.uv.x * 3.0 + n * 4.0);
                    opacity *= 0.7 + 0.3 * depth;
                    body = lerp(body, body * 0.72, vein * 0.35);
                }
                // Multiply: transmitted light = 1 - a * (1 - body)
                float3 transmit = lerp(float3(1, 1, 1), body, opacity * cover);
                return float4(transmit, 1.0);
            }
            ENDHLSL
        }
    }
}
