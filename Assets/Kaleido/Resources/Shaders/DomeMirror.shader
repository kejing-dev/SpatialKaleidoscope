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
        _WedgeRotation ("Wedge rotation (rad)", Float) = 0
        _WedgeHalfSin ("sin(half sector angle)", Float) = 0.5
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
                float _WedgeRotation;
                float _WedgeHalfSin;
            CBUFFER_END

            float4 Frag(DomeVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 d = IN.dirOS;
                float phi, theta;
                DomePolar(d, phi, theta);

                // Unfolded plane coordinates: their screen derivatives equal those of the folded
                // coordinates (folds are isometries), so we can sample with explicit gradients and
                // never get mip-level discontinuities along the mirror seams.
                float2 p0 = DomePlane(d) * _Scale;
                float2 gx = ddx(p0) * 0.5;
                float2 gy = ddy(p0) * 0.5;

                float3 rgb;
                float seam = 0.0;
                float bounceDim = 1.0;
                if (_Mode < 0.5)
                {
                    // Two-mirror scope: the physical cell is a disc of radius _DiscRadius with the
                    // mirror apex at its centre. Only the wedge [-w/2, w/2] around the bisector is visible;
                    // the cell camera frames exactly that wedge (bisector = texture V axis, rotated with
                    // gravity on the C# side), so the whole texture resolution goes to what is shown.
                    float r = phi * _Scale;
                    float a = FoldSector(theta, _SectorAngle);
                    float ar = a - 0.5 * _SectorAngle;                       // angle from the bisector
                    float wx = r * cos(ar);                                  // along the bisector 0..1
                    float wy = r * sin(ar);                                  // across, |wy| <= sin(w/2)
                    float hs = max(_WedgeHalfSin, 0.05);
                    float2 uv = float2(0.5 - wy / (2.0 * hs), wx);
                    float2 gxu = float2(gx.x / (2.0 * hs), gx.y);
                    float2 gyu = float2(gy.x / (2.0 * hs), gy.y);
                    float3 tex = SAMPLE_TEXTURE2D_GRAD(_CellTex, sampler_CellTex, uv, gxu, gyu).rgb;
                    float inside = 1.0 - smoothstep(_DiscRadius * 0.97, _DiscRadius * 1.02, r);

                    // number of reflections needed to reach this sector: each bounce loses a little light
                    float sectorIndex = floor((theta + KALEIDO_PI) / _SectorAngle);
                    float bounces = min(sectorIndex, 2.0 * KALEIDO_PI / _SectorAngle - sectorIndex);
                    bounceDim = pow(0.955, bounces);

                    // seam: distance to either mirror line (arc length), constant width on screen
                    float sa = min(a, _SectorAngle - a) * max(r, 0.03);
                    float seamW = max(length(gx) + length(gy), 0.004) * 1.5;
                    seam = 1.0 - smoothstep(0.0, seamW, sa);
                    float glint = (1.0 - smoothstep(seamW, seamW * 2.5, sa)) * (1.0 - seam);

                    // outside the disc: the dark tube interior, faintly lit near the rim
                    float rim = 1.0 - smoothstep(_DiscRadius, _DiscRadius * 1.5, r);
                    float3 tube = float3(0.02, 0.02, 0.03) + rim * float3(0.06, 0.06, 0.08);
                    if (_DarkField > 0.5)
                    {
                        rgb = tex * 1.15;                                   // halo is baked into the cell texture
                        rgb += glint * float3(0.12, 0.14, 0.18);
                        rgb = lerp(rgb, rgb * 0.35, seam);
                        rgb = lerp(tube * 0.6, rgb, inside);
                    }
                    else
                    {
                        // backlit glass: bright diffuse light behind the cell, gentle vignette to the tube wall
                        float backlight = 1.0 - 0.22 * smoothstep(0.4, 1.0, r / _DiscRadius);
                        rgb = tex * backlight;
                        rgb += glint * 0.10;
                        rgb = lerp(rgb, rgb * 0.55, seam);
                        rgb = lerp(tube, rgb, inside);
                    }
                }
                else
                {
                    // Three-mirror scope: the plane is tiled by reflected copies of the triangular cell.
                    float2 q = FoldTriangle(p0, _TriA.xy, _TriB.xy, _TriC.xy);
                    float2 uv = q * 0.5 + 0.5;
                    float3 tex = SAMPLE_TEXTURE2D_GRAD(_CellTex, sampler_CellTex, uv, gx, gy).rgb;
                    float ed = TriangleEdgeDistance(q, _TriA.xy, _TriB.xy, _TriC.xy);   // lattice units
                    float edW = max(length(gx) + length(gy), 0.004) * 1.2;              // ~ plane units
                    seam = 1.0 - smoothstep(0.0, edW, ed);
                    float glint = (1.0 - smoothstep(edW, edW * 2.5, ed)) * (1.0 - seam);

                    float backlight = 1.0 - 0.28 * smoothstep(0.6, KALEIDO_COVERAGE_RAD, phi);
                    if (_DarkField > 0.5)
                    {
                        rgb = tex * 1.15 * (backlight + 0.05);
                        rgb += glint * float3(0.12, 0.14, 0.18);
                    }
                    else
                    {
                        rgb = tex * backlight;
                        rgb += glint * 0.10;
                    }
                    rgb = lerp(rgb, rgb * 0.55, seam);
                }

                rgb *= bounceDim;
                rgb *= HorizonMask(d);
                return KaleidoOutput(rgb, _Fade);
            }
            ENDHLSL
        }
    }
}
