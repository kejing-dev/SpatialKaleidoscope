// Backlit glass fragments: multiply blend onto a white-cleared cell texture (PRD section 4).
Shader "Kaleido/CellMultiply"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        // ---- Pass 0: multiply (backlit glass)
        Pass
        {
            Name "Multiply"
            Blend DstColor Zero
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
                float bevel = 1.0 - 0.35 * smoothstep(0.55, 1.0, rr);     // darker, denser glass at rim
                float n = KNoise2(IN.uv * 3.0 + IN.uv2.y * 17.0);
                float opacity = IN.color.a;
                float3 tint = lerp(IN.color.rgb, IN.color.rgb * IN.color.rgb, 0.45);   // deeper, more saturated glass
                if (IN.uv2.x > 0.5)
                {
                    // organic petal / feather: soft translucent centre, veins along uv.y
                    float vein = 0.5 + 0.5 * cos(IN.uv.x * 14.0 + n * 3.0);
                    opacity *= 0.55 + 0.45 * smoothstep(0.0, 0.9, 1.0 - rr) ;
                    tint = lerp(tint, tint * 0.75, vein * 0.35);
                }
                else
                {
                    tint *= 0.9 + 0.1 * n;
                }
                // Multiply: transmitted light = 1 - opacity*(1 - tint)
                float3 transmit = lerp(float3(1, 1, 1), tint * bevel, opacity);
                return float4(transmit, 1.0);
            }
            ENDHLSL
        }
    }
}
