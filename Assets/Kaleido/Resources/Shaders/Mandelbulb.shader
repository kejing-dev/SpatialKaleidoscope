// Scene 8: Mandelbulb, sphere-traced inside a proxy sphere (object space radius 1).
// Rendered by two helper cameras (one per eye) into half-resolution textures; the
// BulbComposite shader then draws them on the proxy sphere in the main XR pass.
// Power varies 6..10 over a 120 s round trip; <= 64 marching steps.
Shader "Kaleido/Mandelbulb"
{
    Properties
    {
        _Power ("Power", Float) = 8
        _Fade ("Fade", Range(0, 1)) = 1
        _EvoTime ("Evolution time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend One Zero
            ZWrite On
            Cull Front           // draw back faces so the object is visible even if the eye is inside the proxy
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "KaleidoCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Power;
                float _Fade;
                float _EvoTime;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            V Vert(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.positionOS = IN.positionOS.xyz;
                return o;
            }

            // Distance estimator for the Mandelbulb (object space, bulb radius ~1.15 before scale).
            float BulbDE(float3 p, out float trap)
            {
                float3 z = p;
                float dr = 1.0;
                float r = 0.0;
                trap = 1e9;
                const int ITER = 7;
                for (int i = 0; i < ITER; i++)
                {
                    r = length(z);
                    if (r > 2.5) break;
                    float theta = acos(clamp(z.z / max(r, 1e-6), -1.0, 1.0));
                    float phi = atan2(z.y, z.x);
                    dr = pow(r, _Power - 1.0) * _Power * dr + 1.0;
                    float zr = pow(r, _Power);
                    theta *= _Power;
                    phi *= _Power;
                    z = zr * float3(sin(theta) * cos(phi), sin(phi) * sin(theta), cos(theta));
                    z += p;
                    trap = min(trap, r);
                }
                return 0.5 * log(max(r, 1e-6)) * r / dr;
            }

            float3 BulbNormal(float3 p)
            {
                float t;
                const float e = 0.002;
                float dx = BulbDE(p + float3(e, 0, 0), t) - BulbDE(p - float3(e, 0, 0), t);
                float dy = BulbDE(p + float3(0, e, 0), t) - BulbDE(p - float3(0, e, 0), t);
                float dz = BulbDE(p + float3(0, 0, e), t) - BulbDE(p - float3(0, 0, e), t);
                return normalize(float3(dx, dy, dz));
            }

            float4 Frag(V IN) : SV_Target
            {
                // Ray in object space. The proxy sphere has radius 1; the bulb lives in radius ~1.2/1.3 space.
                float3 camOS = TransformWorldToObject(GetCameraPositionWS());
                float3 ro = camOS;
                float3 rd = normalize(IN.positionOS - camOS);

                // intersect proxy sphere (radius 1)
                float b = dot(ro, rd);
                float c = dot(ro, ro) - 1.0;
                float h = b * b - c;
                if (h < 0.0) discard;
                h = sqrt(h);
                float tNear = max(-b - h, 0.0);
                float tFar = -b + h;

                // march in "bulb space" = object space * 1.3 so the bulb fills the proxy
                const float S = 1.12;
                float t = tNear;
                float trap = 0.0;
                bool hit = false;
                float3 p = 0;
                [loop]
                int steps = 0;
                for (int i = 0; i < 96; i++)
                {
                    p = (ro + rd * t) * S;
                    float dist = BulbDE(p, trap) / S;
                    if (dist < 0.0009 * (1.0 + t)) { hit = true; break; }
                    t += dist * 0.85;
                    steps = i;
                    if (t > tFar) break;
                }
                if (!hit) discard;
                // step-count ambient occlusion (crevices need many steps)
                float ao = 1.0 - 0.55 * saturate((float)steps / 80.0);

                float3 nrm = BulbNormal(p);
                float3 nWS = normalize(mul((float3x3)unity_ObjectToWorld, nrm));
                float3 lightDir = normalize(float3(0.4, 0.8, -0.35));
                float diff = saturate(dot(nWS, lightDir)) * 0.7 + 0.3;
                float3 lightDir2 = normalize(float3(-0.5, 0.2, 0.6));
                diff += 0.25 * saturate(dot(nWS, lightDir2));
                // low-frequency colour from the orbit trap (smooth, no speckle)
                float3 base = KPalette(trap * 0.22 + 0.1, float3(0.45, 0.5, 0.55), float3(0.4, 0.35, 0.3), float3(1.0, 1.0, 1.0), float3(0.05, 0.25, 0.5));
                float3 viewWS = normalize(GetCameraPositionWS() - TransformObjectToWorld(p / S));
                float fres = pow(1.0 - saturate(dot(nWS, viewWS)), 3.0);
                float3 col = (base * diff + fres * 0.2) * ao;
                float4 o = KaleidoOutput(col, 1.0);
                o.a = 1.0;
                return o;
            }
            ENDHLSL
        }
    }
}
