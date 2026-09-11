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
        _TexelSize ("Texel size (1/resolution)", Float) = 0.0009765625
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
                float _TexelSize;
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

            // Distance-estimate colouring with a pixel-size threshold: anything inside the set or
            // finer than a texel is painted with one solid colour (no aliasing, no speckle);
            // the exterior is a smooth gradient of the distance to the set.
            float3 Shade(float2 z0, float2 c, int maxIter, float pix)
            {
                float2 z = z0;
                float2 dz = float2(1.0, 0.0);
                float n = -1.0;
                float m2 = 0.0;
                [loop]
                for (int i = 0; i < 200; i++)
                {
                    if (i >= maxIter) break;
                    dz = 2.0 * float2(z.x * dz.x - z.y * dz.y, z.x * dz.y + z.y * dz.x);
                    z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
                    m2 = dot(z, z);
                    if (m2 > 1e6) { n = (float)i; break; }
                }
                const float3 setColor = float3(0.12, 0.05, 0.18);
                if (n < 0.0) return setColor;
                float mz = sqrt(m2);
                float de = 0.5 * mz * log(mz) / max(length(dz), 1e-12);
                float solid = 1.0 - smoothstep(0.8 * pix, 2.5 * pix, de);
                float v = log2(max(de / pix, 1.0));
                float t = v * 0.10 + _EvoTime * 0.006;
                float3 pal = KPalette(t, float3(0.55, 0.42, 0.62), float3(0.35, 0.32, 0.35), float3(0.5, 0.5, 0.5), float3(0.0, 0.2, 0.55));
                float3 farCol = float3(0.30, 0.22, 0.42);
                float3 col = lerp(pal, farCol, smoothstep(6.0, 14.0, v));
                col += float3(0.95, 0.8, 1.0) * 0.25 * (1.0 - smoothstep(0.0, 3.0, v));
                return lerp(col, setColor, solid);
            }

            float4 Frag(V IN) : SV_Target
            {
                float2 c = _C.xy;
                int maxIter = (int)_MaxIter;
                float texel = _TexelSize;
                float pix = _View.z * 2.0 * texel;
                float2 o1 = float2(0.125, 0.375) * texel;
                float2 o2 = float2(-0.375, 0.125) * texel;
                float3 col = 0;
                col += Shade(_View.xy + ((IN.uv + o1) * 2.0 - 1.0) * _View.z, c, maxIter, pix);
                col += Shade(_View.xy + ((IN.uv - o1) * 2.0 - 1.0) * _View.z, c, maxIter, pix);
                col += Shade(_View.xy + ((IN.uv + o2) * 2.0 - 1.0) * _View.z, c, maxIter, pix);
                col += Shade(_View.xy + ((IN.uv - o2) * 2.0 - 1.0) * _View.z, c, maxIter, pix);
                return float4(col * 0.25, 1.0);
            }
            ENDHLSL
        }
    }
}
