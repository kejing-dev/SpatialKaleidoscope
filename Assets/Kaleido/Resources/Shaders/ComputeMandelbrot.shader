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

            float4 Frag(V IN) : SV_Target
            {
                float2 d0 = (IN.uv * 2.0 - 1.0) * _HalfSize;   // offset from reference c
                float2 d = d0;
                int maxIter = (int)_MaxIter;
                int refCount = (int)_RefCount;
                int refIdx = 0;
                float n = -1.0;
                float2 zfull = RefZ(0) + d;
                [loop]
                for (int i = 0; i < 2000; i++)
                {
                    if (i >= maxIter) break;
                    float2 Z = RefZ(refIdx);
                    // d' = 2 Z d + d^2 + d0
                    float2 twoZd = 2.0 * float2(Z.x * d.x - Z.y * d.y, Z.x * d.y + Z.y * d.x);
                    float2 dd = float2(d.x * d.x - d.y * d.y, 2.0 * d.x * d.y);
                    d = twoZd + dd + d0;
                    refIdx++;
                    if (refIdx >= refCount) { refIdx = 0; }   // reference is non-escaping; wrap is safe because we rebase
                    float2 Zn = RefZ(refIdx);
                    zfull = Zn + d;
                    float m2 = dot(zfull, zfull);
                    if (m2 > 1024.0)
                    {
                        n = (float)i + 1.0 - log2(log2(m2)) + 5.0;
                        break;
                    }
                    // rebase: if the full orbit is closer to 0 than the reference, restart the reference
                    if (m2 < dot(d, d))
                    {
                        d = zfull;
                        refIdx = 0;
                    }
                }
                float3 col;
                if (n < 0.0)
                {
                    col = float3(0.02, 0.01, 0.03);
                }
                else
                {
                    // log-scaled colouring so bands stay similar in width across the zoom
                    float t = log2(1.0 + n) * 0.42 + _ZoomLog * 0.12 + _EvoTime * 0.002;
                    col = KPalette(t, float3(0.55, 0.38, 0.42), float3(0.42, 0.38, 0.45), float3(1.0, 1.0, 1.0), float3(0.0, 0.1, 0.25));
                    float band = frac(t * 1.5);
                    col *= 0.7 + 0.4 * smoothstep(0.0, 0.2, band) * (1.0 - smoothstep(0.8, 1.0, band));
                    // warm sunset tint (see reference image 8) for the outer bands
                    col = lerp(col, float3(0.98, 0.55, 0.42), 0.35 * (1.0 - smoothstep(2.0, 60.0, n)));
                    col *= 0.55 + 0.45 * smoothstep(0.0, 8.0, n);
                }
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
