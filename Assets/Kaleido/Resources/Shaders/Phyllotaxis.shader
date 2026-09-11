// Scene 11: phyllotaxis spiral on the dome. Mesh = N quads; uv2.x = seed index n.
// Dot n sits at angle-from-zenith phi = c*sqrt(n), azimuth n*alpha. alpha oscillates
// 137.5 deg +- 1 deg over a 60 s round trip: every dot moves continuously, no popping.
Shader "Kaleido/Phyllotaxis"
{
    Properties
    {
        _Alpha ("Divergence angle (rad)", Float) = 2.39996
        _C ("Radial constant", Float) = 0.028
        _DotSize ("Dot angular size (rad)", Float) = 0.009
        _Radius ("Dome radius", Float) = 4.98
        _Fade ("Fade", Range(0, 1)) = 1
        _EvoTime ("Evolution time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "KaleidoCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
                float _C;
                float _DotSize;
                float _Radius;
                float _Fade;
                float _EvoTime;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float n : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            V Vert(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float n = IN.uv2.x;
                float phi = _C * sqrt(n);
                float theta = n * _Alpha;
                float sp = sin(phi), cp = cos(phi);
                float st = sin(theta), ct = cos(theta);
                float3 dir = float3(sp * st, cp, sp * ct);
                // tangent frame on the sphere
                float3 eTheta = float3(ct, 0.0, -st);
                float3 ePhi = float3(cp * st, -sp, cp * ct);
                float size = _DotSize * (0.7 + 0.3 * smoothstep(0.0, 1.2, phi)) * _Radius;
                float3 pos = dir * _Radius + (IN.uv.x * eTheta + IN.uv.y * ePhi) * size;
                o.positionCS = TransformObjectToHClip(pos);
                o.uv = IN.uv;
                o.n = n;
                return o;
            }

            float4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float r = length(IN.uv);
                float a = 1.0 - smoothstep(0.75, 1.0, r);
                // colour by index (slow hue drift), brightness constant -> no sudden luminance jumps
                float t = IN.n * 0.0011 + _EvoTime * 0.004;
                float3 col = KPalette(t, float3(0.6, 0.5, 0.45), float3(0.4, 0.4, 0.35), float3(1.0, 1.0, 1.0), float3(0.0, 0.15, 0.4));
                col *= 0.85 + 0.15 * (1.0 - r);
                return KaleidoOutput(col, a * _Fade);
            }
            ENDHLSL
        }
    }
}
