// Scene 7: Julia set z -> z^2 + c, rendered to a 2D texture via Blit once per frame.
// c moves on a closed path (90 s loop). Smooth (continuous) escape-time colouring,
// up to _MaxIter iterations with early exit.
Shader "Kaleido/ComputeJulia"
{
    Properties
    {
        _C ("c", Vector) = (-0.8, 0.156, 0, 0)
        _MaxIter ("Max iterations", Float) = 300
        _View ("View (centre.xy, half-size)", Vector) = (0, 0, 1.6, 0)
        _EvoTime ("Evolution time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off Cull Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "KaleidoCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _C;
                float _MaxIter;
                float4 _View;
                float _EvoTime;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V Vert(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                float2 z = _View.xy + (IN.uv * 2.0 - 1.0) * _View.z;
                float2 c = _C.xy;
                int maxIter = (int)_MaxIter;
                float n = 0.0;
                float escaped = 0.0;
                float trap = 1e9;
                [loop]
                for (int i = 0; i < 300; i++)
                {
                    if (i >= maxIter) break;
                    z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
                    float m2 = dot(z, z);
                    trap = min(trap, abs(z.x * z.y));
                    if (m2 > 256.0)
                    {
                        // continuous escape count
                        n = (float)i + 1.0 - log2(log2(m2)) + 4.0;
                        escaped = 1.0;
                        break;
                    }
                }
                float3 col;
                if (escaped > 0.5)
                {
                    float t = log2(1.0 + n) * 0.28;                  // log scale: bands across the whole exterior
                    float hue = t + _EvoTime * 0.01;
                    col = KPalette(hue, float3(0.5, 0.42, 0.55), float3(0.45, 0.4, 0.45), float3(1.0, 1.0, 0.9), float3(0.0, 0.2, 0.55));
                    // thin bright contour lines between bands, darker far away from the set
                    float band = frac(t * 2.0);
                    col *= 0.75 + 0.35 * smoothstep(0.0, 0.15, band) * (1.0 - smoothstep(0.85, 1.0, band));
                    col *= 0.3 + 0.7 * smoothstep(0.0, 10.0, n);
                }
                else
                {
                    // interior: dark velvet with orbit-trap texture
                    float tr = 1.0 - exp(-trap * 6.0);
                    col = lerp(float3(0.08, 0.03, 0.12), float3(0.32, 0.12, 0.42), tr);
                }
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
