// Scene 8: Mandelbulb, sphere-traced inside a proxy sphere (object space radius 1).
// Rendered by two helper cameras (one per eye) into 0.6x-eye-resolution textures; the
// BulbComposite shader then draws them on the proxy sphere in the main XR pass.
// Power varies 6.5..8.5 over a 120 s round trip; <= 128 marching steps.
//
// Anti-noise design (device review: the bulb must read as a smooth, polished solid, no
// sparkle, no crawling pixels when the head moves):
//   * hit threshold and normal epsilon scale with the PIXEL FOOTPRINT at the hit distance
//     (helper camera pixel angle x march distance), so sub-pixel fractal detail is never resolved
//   * step factor 0.75 + bisection refinement between the last two samples, so thin structures
//     are hit consistently by neighbouring pixels (no inconsistent overshoot hits)
//   * rays that run out of steps while still inside the proxy and within a few pixels of the
//     surface (deep grooves, grazing silhouettes) are ACCEPTED at their last sample instead of
//     discarded, so no flickering alpha-0 holes appear in the crevices as the head moves
//   * tetrahedral 4-tap normal over a ~1.5 px wide epsilon (no per-pixel normal noise)
//   * smooth 3-tap ambient occlusion along the normal whose tap radii are never smaller than a
//     few pixel footprints (no step-count AO, no pixel-scale occlusion detail)
//   * colour only from low-frequency terms: hemisphere two-tone lighting + an orbit trap taken
//     from the first TWO orbit points only and passed through a wide smoothstep
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

            // march in "bulb space" = object space * BULB_SCALE so the bulb fills the unit proxy sphere
            #define BULB_SCALE 1.12
            #define BULB_ITER 7
            #define MAX_STEPS 128
            #define STEP_FACTOR 0.75
            #define MIN_HIT_EPS 0.0004         // object space; floor for the footprint-based hit threshold
            #define MIN_NORMAL_EPS 0.0025      // object space; floor for the footprint-based normal epsilon

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            V Vert(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.positionOS = IN.positionOS.xyz;
                return o;
            }

            // Angular size of one helper-camera pixel (radians). The helper renders mono into its own
            // render texture, so _ScreenParams.y is that texture's height (camera.pixelHeight) and
            // UNITY_MATRIX_P._m11 is 1 / tan(fovY / 2) (abs: the GPU projection may be y-flipped).
            // Clamped so a degenerate value can never blow the footprint up or shrink it to zero.
            float PixelAngle()
            {
                float focal = abs(UNITY_MATRIX_P._m11);
                float px = 2.0 / max(focal * _ScreenParams.y, 1.0);
                return clamp(px, 0.0008, 0.006);
            }

            // Mandelbulb distance estimator (bulb space, bulb radius ~1.15 before scale).
            // trap = min |z| over the FIRST TWO orbit points only (p and the first iterate z1 = p^n + p):
            // |z1| has gradient <= n r^(n-1) (~8 per bulb unit where it is the minimum), i.e. well under
            // 0.1 per helper pixel, whereas the second iterate already has gradient ~n^2 and speckles at
            // the helper resolution.
            float BulbDE(float3 p, out float trap)
            {
                float3 z = p;
                float dr = 1.0;
                float r = length(z);
                trap = r;
                for (int i = 0; i < BULB_ITER; i++)
                {
                    r = length(z);
                    if (r > 2.5) break;
                    if (i < 2) trap = min(trap, r);
                    float theta = acos(clamp(z.z / max(r, 1e-6), -1.0, 1.0));
                    float phi = atan2(z.y, z.x);
                    float rp = pow(r, _Power - 1.0);
                    dr = rp * _Power * dr + 1.0;
                    float zr = rp * r;
                    theta *= _Power;
                    phi *= _Power;
                    z = zr * float3(sin(theta) * cos(phi), sin(phi) * sin(theta), cos(theta));
                    z += p;
                }
                return 0.5 * log(max(r, 1e-6)) * r / dr;
            }

            float BulbDist(float3 p)
            {
                float trap;
                return BulbDE(p, trap);
            }

            // Tetrahedral 4-tap normal. e is the sampling radius in bulb space (about 1.5 pixel footprints
            // at the hit distance), so the gradient is averaged over the pixel instead of picking up
            // sub-pixel fractal detail. Falls back to the given direction if the gradient degenerates
            // (a zero-length normalize would produce NaN speckle).
            float3 BulbNormal(float3 p, float e, float3 fallback)
            {
                const float2 k = float2(1.0, -1.0);
                float3 g = k.xyy * BulbDist(p + k.xyy * e)
                         + k.yyx * BulbDist(p + k.yyx * e)
                         + k.yxy * BulbDist(p + k.yxy * e)
                         + k.xxx * BulbDist(p + k.xxx * e);
                float len = length(g);
                return len > 1e-7 ? g / len : fallback;
            }

            // Smooth 3-tap ambient occlusion along the normal. Tap radii are at least 0.02 / 0.05 / 0.10
            // in bulb space AND at least 4 / 10 / 20 pixel footprints (fp = bulb-space size of one helper
            // pixel at the hit distance): the DE is Lipschitz-1, so a tap only a few pixels above a
            // fractal surface could swing tens of percent between neighbouring pixels and re-introduce
            // the grain the averaged normal removed. Tied to the footprint, the AO can never resolve
            // pixel-scale detail; the nearest (finest) tap has the smallest weight.
            float BulbAO(float3 p, float3 n, float fp)
            {
                float h0 = max(0.02, 4.0 * fp);
                float h1 = max(0.05, 10.0 * fp);
                float h2 = max(0.10, 20.0 * fp);
                float o0 = saturate(1.0 - BulbDist(p + n * h0) / (h0 * 0.6));
                float o1 = saturate(1.0 - BulbDist(p + n * h1) / (h1 * 0.6));
                float o2 = saturate(1.0 - BulbDist(p + n * h2) / (h2 * 0.6));
                float occ = 0.20 * o0 + 0.35 * o1 + 0.45 * o2;
                return 1.0 - 0.5 * occ;
            }

            float4 Frag(V IN) : SV_Target
            {
                // Ray in object space. The proxy sphere has radius 1; the bulb lives in radius ~1.2 space.
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

                // Pixel footprint (object space) at distance t along the ray = t * pixAng: the proxy scale
                // is uniform, so angles are the same in world and object space and t is in object units.
                float pixAng = PixelAngle();
                const float S = BULB_SCALE;

                // ---- sphere tracing: hit when the DE drops below half a pixel footprint at that distance
                float t = tNear;
                float tPrev = tNear;
                float dist = 0.0;                 // DE at the last evaluated sample (tPrev after a step)
                float eps = MIN_HIT_EPS;          // hit threshold at that sample
                bool hit = false;
                [loop]
                for (int i = 0; i < MAX_STEPS; i++)
                {
                    float3 q = (ro + rd * t) * S;
                    dist = BulbDist(q) / S;                             // object-space distance estimate
                    eps = max(MIN_HIT_EPS, 0.5 * t * pixAng);
                    if (dist < eps) { hit = true; break; }
                    tPrev = t;
                    t += dist * STEP_FACTOR;
                    if (t > tFar) break;
                }
                // Out of steps while still inside the proxy and within a few pixels of the surface (a ray
                // converging geometrically down a narrow groove, or grazing the silhouette): accept the last
                // evaluated sample instead of punching an alpha-0 hole whose position changes every frame.
                // Zero extra DE cost; the bisection below then has tA == tB and is a no-op for these pixels.
                if (!hit && t <= tFar && dist < 4.0 * eps) { hit = true; t = tPrev; }
                if (!hit) discard;

                // ---- bisection between the last outside sample (tPrev) and the hit (t): locates the
                // DE == eps iso-surface consistently between neighbouring pixels even where the estimator
                // overshoots on thin structures. 3 iterations = 3 extra DE evaluations.
                float tA = tPrev;
                float tB = t;
                for (int j = 0; j < 3; j++)
                {
                    float tm = 0.5 * (tA + tB);
                    float dm = BulbDist((ro + rd * tm) * S) / S;
                    float em = max(MIN_HIT_EPS, 0.5 * tm * pixAng);
                    if (dm < em) tB = tm; else tA = tm;
                }
                t = tB;
                float3 p = (ro + rd * t) * S;

                // ---- normal over ~1.5 pixel footprints (bulb space), never narrower than MIN_NORMAL_EPS
                float e = max(MIN_NORMAL_EPS, 1.5 * t * pixAng) * S;
                float3 nOS = BulbNormal(p, e, -rd);
                float3 nWS = normalize(mul((float3x3)unity_ObjectToWorld, nOS));

                // ---- low-frequency colour: two-point orbit trap through a wide smoothstep (min(|p|, |z1|)
                // spans a slightly narrower band than the old three-point trap, so the ramp is tightened a
                // little to keep the full palette range; still <= ~0.1 of the ramp per helper pixel)
                float trap;
                BulbDE(p, trap);
                float trapT = smoothstep(0.3, 1.2, trap);
                float3 base = KPalette(0.1 + 0.3 * trapT,
                                       float3(0.45, 0.5, 0.55), float3(0.4, 0.35, 0.3),
                                       float3(1.0, 1.0, 1.0), float3(0.05, 0.25, 0.5));

                // ---- two-tone lighting: wrapped key light (no hard terminator), fill, sky/ground hemisphere
                float3 keyDir = normalize(float3(0.4, 0.8, -0.35));
                float3 fillDir = normalize(float3(-0.5, 0.2, 0.6));
                float wrap = saturate(dot(nWS, keyDir) * 0.5 + 0.5);
                float diff = wrap * wrap * 0.75 + 0.25;
                diff += 0.2 * saturate(dot(nWS, fillDir));
                float hemi = nWS.y * 0.5 + 0.5;
                float3 sky = lerp(float3(0.70, 0.75, 0.95), float3(1.10, 1.02, 0.92), hemi);

                float3 viewWS = normalize(GetCameraPositionWS() - TransformObjectToWorld(p / S));
                float fres = pow(1.0 - saturate(dot(nWS, viewWS)), 3.0);
                float fp = t * pixAng * S;        // one helper pixel footprint at the hit, in bulb space
                float ao = BulbAO(p, nOS, fp);

                float3 col = (base * diff * sky + fres * 0.18) * ao;
                float4 o = KaleidoOutput(col, 1.0);
                o.a = 1.0;   // coverage: the helper target is cleared to (0,0,0,0)
                return o;
            }
            ENDHLSL
        }
    }
}
