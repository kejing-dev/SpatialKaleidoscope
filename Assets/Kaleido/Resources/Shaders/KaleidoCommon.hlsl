#ifndef KALEIDO_COMMON_INCLUDED
#define KALEIDO_COMMON_INCLUDED

// Shared helpers for all Spatial Kaleidoscope shaders.
// Every shader is URP + single-pass instanced / multiview safe: the macros below
// must be used in each vertex/fragment pair.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Global output-brightness governor (0..1). Set from C# via Shader.SetGlobalFloat.
// This is the "post tone-mapping brightness target" from PRD section 7: it scales the
// final colour of every kaleido shader, it does not touch the headset brightness.
float _KaleidoBrightness;

#define KALEIDO_PI 3.14159265359
#define KALEIDO_TWO_PI 6.28318530718

// ---------------------------------------------------------------- dome geometry
struct DomeAttributes
{
    float4 positionOS : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DomeVaryings
{
    float4 positionCS : SV_POSITION;
    float3 dirOS      : TEXCOORD0;   // normalised object-space direction (dome centre = origin)
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

DomeVaryings DomeVert(DomeAttributes IN)
{
    DomeVaryings OUT;
    UNITY_SETUP_INSTANCE_ID(IN);
    UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
    OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
    OUT.dirOS = normalize(IN.positionOS.xyz);
    return OUT;
}

// Polar coordinates around the zenith (+Y). phi = angle from zenith [0, pi], theta = azimuth (-pi, pi].
void DomePolar(float3 d, out float phi, out float theta)
{
    phi = acos(clamp(d.y, -1.0, 1.0));
    theta = atan2(d.x, d.z);
}

// Azimuthal-equidistant projection from the zenith onto a plane: length(p) == phi.
float2 DomePlane(float3 d)
{
    float phi, theta;
    DomePolar(d, phi, theta);
    return phi * float2(sin(theta), cos(theta));
}

// Smooth darkening below the horizon so the full sphere never shows a hard edge.
float HorizonMask(float3 d)
{
    // 1 above ~+12 deg, 0 below ~-20 deg.
    return smoothstep(-0.35, 0.2, d.y);
}

// ---------------------------------------------------------------- folding
// Two-mirror kaleidoscope: mirrors meet at angle w, image consists of 2*pi/w sectors,
// alternate sectors mirrored. Continuous everywhere (triangle wave), exact reflections.
float FoldSector(float theta, float w)
{
    float a = theta + KALEIDO_PI;          // [0, 2pi)
    float m = fmod(a, 2.0 * w);            // [0, 2w)
    return abs(m - w);                     // [0, w]
}

// Three-mirror (equilateral, *333) fold. Lattice translations a = B-A, b = C-A.
// Reduce modulo lattice, then reflect the "down" triangle across the shared edge.
// Exact closed form: every image is a true reflection so seams are continuous.
float2 FoldTriangle(float2 p, float2 A, float2 B, float2 C)
{
    float2 a = B - A;
    float2 b = C - A;
    float2 q = p - A;
    float det = a.x * b.y - a.y * b.x;
    float u = (q.x * b.y - q.y * b.x) / det;
    float v = (a.x * q.y - a.y * q.x) / det;
    u = frac(u);
    v = frac(v);
    if (u + v > 1.0)
    {
        float nu = 1.0 - v;
        float nv = 1.0 - u;
        u = nu;
        v = nv;
    }
    return A + u * a + v * b;
}

// Distance (in lattice units) of the folded point to the nearest mirror edge; used for faint seams.
float TriangleEdgeDistance(float2 p, float2 A, float2 B, float2 C)
{
    float2 a = B - A;
    float2 b = C - A;
    float2 q = p - A;
    float det = a.x * b.y - a.y * b.x;
    float u = (q.x * b.y - q.y * b.x) / det;
    float v = (a.x * q.y - a.y * q.x) / det;
    float w = 1.0 - u - v;
    return min(min(u, v), w);
}

// ---------------------------------------------------------------- noise
float KHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float KHash31(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

float KNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = KHash21(i);
    float b = KHash21(i + float2(1, 0));
    float c = KHash21(i + float2(0, 1));
    float d = KHash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float KNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = KHash31(i);
    float n100 = KHash31(i + float3(1, 0, 0));
    float n010 = KHash31(i + float3(0, 1, 0));
    float n110 = KHash31(i + float3(1, 1, 0));
    float n001 = KHash31(i + float3(0, 0, 1));
    float n101 = KHash31(i + float3(1, 0, 1));
    float n011 = KHash31(i + float3(0, 1, 1));
    float n111 = KHash31(i + float3(1, 1, 1));
    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    float y0 = lerp(x00, x10, u.y);
    float y1 = lerp(x01, x11, u.y);
    return lerp(y0, y1, u.z);
}

float KFbm3(float3 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * KNoise3(p);
        p = p * 2.03 + 17.1;
        a *= 0.5;
    }
    return v;
}


// Cellular / Voronoi noise: returns F1 (nearest), F2 (second nearest) and the nearest cell id.
void KCellular(float2 p, float jitter, out float f1, out float f2, out float2 id)
{
    float2 n = floor(p);
    float2 f = frac(p);
    f1 = 8.0; f2 = 8.0; id = 0;
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++)
    {
        float2 g = float2(i, j);
        float2 cell = n + g;
        float2 o = float2(KHash21(cell), KHash21(cell + 19.19)) * jitter;
        float2 r = g + o - f;
        float d = dot(r, r);
        if (d < f1) { f2 = f1; f1 = d; id = cell; }
        else if (d < f2) { f2 = d; }
    }
    f1 = sqrt(f1); f2 = sqrt(f2);
}

// Cosine palette (Inigo Quilez). All channels stay in [0,1].
float3 KPalette(float t, float3 a, float3 b, float3 c, float3 d)
{
    return a + b * cos(KALEIDO_TWO_PI * (c * t + d));
}

// ---------------------------------------------------------------- output
// Final colour: clamp, apply the global brightness governor, alpha carries the crossfade.
float4 KaleidoOutput(float3 rgb, float fade)
{
    rgb = saturate(rgb) * saturate(_KaleidoBrightness);
    return float4(rgb, saturate(fade));
}

#endif
