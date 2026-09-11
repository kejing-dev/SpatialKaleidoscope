// Physical kaleidoscope dome: samples the 2D fragment-cell texture through an exact
// mirror fold (two-mirror sector fold or three-mirror equilateral tiling).
Shader "Kaleido/DomeMirror"
{
    Properties
    {
        _CellTex ("Cell Texture", 2D) = "white" {}
        _Mode ("Mode (0 sector, 1 triangle)", Float) = 0
        _SectorAngle ("Sector angle (rad)", Float) = 1.0471975
        _Scale ("Plane scale", Float) = 1.0
        _TriA ("Triangle A", Vector) = (-0.85, -0.55, 0, 0)
        _TriB ("Triangle B", Vector) = (0.85, -0.55, 0, 0)
        _TriC ("Triangle C", Vector) = (0, 0.922, 0, 0)
        _DarkField ("Dark field (0 backlit, 1 dark)", Float) = 0
        _Fade ("Fade", Range(0, 1)) = 1
        _DiscRadius ("Disc radius", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Dome"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex DomeVert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "KaleidoCommon.hlsl"

            TEXTURE2D(_CellTex);
            SAMPLER(sampler_CellTex);

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float _SectorAngle;
                float _Scale;
                float4 _TriA;
                float4 _TriB;
                float4 _TriC;
                float _DarkField;
                float _Fade;
                float _DiscRadius;
            CBUFFER_END

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 d = IN.dirOS;
                float phi, theta;
                DomePolar(d, phi, theta);

                float3 rgb;
                float seam = 0.0;
                if (_Mode < 0.5)
                {
                    // Two-mirror scope: the physical cell is a disc of radius _DiscRadius with the
                    // mirror apex at its centre. We only "see" the wedge [0, w]; reflections tile it.
                    float r = phi * _Scale;
                    float a = FoldSector(theta, _SectorAngle);
                    float2 cell = r * float2(cos(a), sin(a));
                    float2 uv = cell * 0.5 + 0.5;
                    float3 tex = SAMPLE_TEXTURE2D(_CellTex, sampler_CellTex, uv).rgb;
                    float3 glow = SAMPLE_TEXTURE2D_LOD(_CellTex, sampler_CellTex, uv, 3.5).rgb;
                    float inside = 1.0 - smoothstep(_DiscRadius * 0.96, _DiscRadius * 1.02, r);

                    // seam: distance to either mirror line, in radians of azimuth
                    float sa = min(a, _SectorAngle - a) * max(r, 0.02);
                    seam = 1.0 - smoothstep(0.0, 0.012, sa);

                    // outside the disc: the dark tube interior, faintly lit near the rim
                    float rim = 1.0 - smoothstep(_DiscRadius, _DiscRadius * 1.6, r);
                    float3 tube = float3(0.025, 0.026, 0.034) + rim * float3(0.05, 0.05, 0.06);
                    if (_DarkField > 0.5)
                    {
                        rgb = tex + glow * 0.55;
                        rgb = lerp(rgb, rgb * 0.5, seam);
                        rgb = lerp(tube * 0.6, rgb, inside);
                    }
                    else
                    {
                        // backlit glass: bright diffuse light behind the cell, soft vignette to tube wall
                        float backlight = 0.95 - 0.3 * smoothstep(0.3, 1.0, r / _DiscRadius);
                        rgb = tex * backlight;
                        rgb = lerp(rgb, rgb * 0.7, seam * 0.8);
                        rgb = lerp(tube, rgb, inside);
                    }
                }
                else
                {
                    // Three-mirror scope: the plane is tiled by reflected copies of the triangular cell.
                    float2 p = DomePlane(d) * _Scale;
                    float2 q = FoldTriangle(p, _TriA.xy, _TriB.xy, _TriC.xy);
                    float2 uv = q * 0.5 + 0.5;
                    float3 tex = SAMPLE_TEXTURE2D(_CellTex, sampler_CellTex, uv).rgb;
                    float ed = TriangleEdgeDistance(q, _TriA.xy, _TriB.xy, _TriC.xy);
                    seam = 1.0 - smoothstep(0.0, 0.012, ed);

                    float rr = length(p) / _Scale;           // radians from zenith
                    float backlight = 0.95 - 0.45 * smoothstep(0.35, 1.45, rr);
                    if (_DarkField > 0.5)
                    {
                        float3 glow = SAMPLE_TEXTURE2D_LOD(_CellTex, sampler_CellTex, uv, 3.5).rgb;
                        rgb = (tex + glow * 0.55) * (backlight + 0.05);
                    }
                    else
                    {
                        rgb = tex * backlight;
                    }
                    rgb = lerp(rgb, rgb * 0.72, seam * 0.8);
                }

                rgb *= HorizonMask(d);
                return KaleidoOutput(rgb, _Fade);
            }
            ENDHLSL
        }
    }
}
