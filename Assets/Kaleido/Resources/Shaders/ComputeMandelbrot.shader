// Scene 12: Mandelbrot deep zoom to 1e10 using perturbation theory.
// The reference orbit Z_n (computed on the CPU in double precision at a Misiurewicz point,
// which never escapes) is stored in _RefOrbit (RGFloat, one texel per iteration).
// Per pixel we iterate the delta: d_{n+1} = 2 Z_n d_n + d_n^2 + d_0, with Zhuoran-style
// rebasing (restart the reference index when |Z+d| < |d|) which removes the classic
// single-reference glitches.
Shader "Kaleido/ComputeMandelbrot"
{
    Properties
    {
        _RefOrbit ("Reference orbit", 2D) = "black" {}
        _RefCount ("Reference length", Float) = 2048
        _HalfSize ("Half view size (complex plane)", Float) = 1.5
        _MaxIter ("Max iterations", Float) = 1500
        _EvoTime ("Evolution time", Float) = 0
        _ZoomLog ("log10 zoom", Float) = 0
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
            #pragma target 3.5
            #include "KaleidoCommon.hlsl"

            TEXTURE2D(_RefOrbit);
            SAMPLER(sampler_RefOrbit);
            float4 _RefOrbit_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _RefCount;
                float _HalfSize;
                float _MaxIter;
                float _EvoTime;
                float _ZoomLog;
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

            float2 RefZ(int n)
            {
                // texel n in a (RefCount x 1) texture
                float u = ((float)n + 0.5) * _RefOrbit_TexelSize.x;
                return SAMPLE_TEXTURE2D_LOD(_RefOrbit, sampler_RefOrbit, float2(u, 0.5), 0).xy;
            }

            float3 Shade(float2 uv, float pix)
            {
                float2 d0 = (uv * 2.0 - 1.0) * _HalfSize;   // offset from reference c
                float2 d = d0;
                float2 dz = float2(0.0, 0.0);                    // derivative of the FULL orbit w.r.t. c
                int maxIter = (int)_MaxIter;
                int refCount = (int)_RefCount;
                int refIdx = 0;
                float n = -1.0;
                float m2 = 0.0;
                float2 zfull = RefZ(0) + d;
                [loop]
                for (int i = 0; i < 2048; i++)
                {
                    if (i >= maxIter) break;
                    float2 Z = RefZ(refIdx);
                    dz = 2.0 * float2(zfull.x * dz.x - zfull.y * dz.y, zfull.x * dz.y + zfull.y * dz.x) + float2(1.0, 0.0);
                    float2 twoZd = 2.0 * float2(Z.x * d.x - Z.y * d.y, Z.x * d.y + Z.y * d.x);
                    float2 dd = float2(d.x * d.x - d.y * d.y, 2.0 * d.x * d.y);
                    d = twoZd + dd + d0;
                    refIdx++;
                    if (refIdx >= refCount) { refIdx = 0; }
                    float2 Zn = RefZ(refIdx);
                    zfull = Zn + d;
                    m2 = dot(zfull, zfull);
                    if (m2 > 1e6) { n = (float)i; break; }
                    if (m2 < dot(d, d)) { d = zfull; refIdx = 0; }   // rebase
                }
                // Solid "set" colour for everything that is inside OR finer than the pixel grid.
                // Structures smaller than a texel are never drawn (they would only alias); they
                // appear once the zoom makes them larger than a texel. This keeps the image calm.
                const float3 setColor = float3(0.10, 0.05, 0.14);
                if (n < 0.0) return setColor;
                float mz = sqrt(m2);
                float de = 0.5 * mz * log(mz) / max(length(dz), 1e-30);       // distance to the set
                float solid = 1.0 - smoothstep(0.8 * pix, 2.5 * pix, de);      // anti-aliased boundary
                float v = log2(max(de / pix, 1.0));                            // distance in texels (>= 0)
                // smooth exterior gradient by distance only (no iteration-count terms)
                float t = v * 0.11 + _EvoTime * 0.004;
                float3 pal = KPalette(t, float3(0.60, 0.45, 0.50), float3(0.36, 0.32, 0.36), float3(0.5, 0.5, 0.5), float3(0.0, 0.15, 0.30));
                float3 farCol = float3(0.72, 0.50, 0.45);                       // warm haze far from the set
                float3 col = lerp(pal, farCol, smoothstep(6.0, 14.0, v));
                col += float3(1.0, 0.85, 0.6) * 0.25 * (1.0 - smoothstep(0.0, 3.0, v)); // soft rim glow
                return lerp(col, setColor, solid);
            }

            float4 Frag(V IN) : SV_Target
            {
                float pix = _HalfSize * 2.0 * _TexelSize;      // one texel in the complex plane
                return float4(Shade(IN.uv, pix), 1.0);
            }
            ENDHLSL
        }
    }
}
