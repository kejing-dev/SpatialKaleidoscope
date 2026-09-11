using System;
using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 12 "Mandelbrot deep zoom" (dome only). Zooms 1x -> 1e10 into the period-897
    /// nucleus c* = (-0.74364392404504587, 0.13182591993348175) using perturbation rendering:
    /// the reference orbit Z_n = f^n(0) is iterated once on the CPU in double precision and stored
    /// in a 4096 x 1 float texture; Kaleido/ComputeMandelbrot then iterates only the per-pixel
    /// delta in float. Because the nucleus is superattracting the reference never escapes
    /// (|f^897(0)| ~ 1e-11, bounded for every iteration count), so one reference serves the whole dive.
    ///
    /// Cycle (scene-local evolution time, sum of dt so dt == 0 freezes everything, GPU work included):
    ///   A  0 - 60 s  log10(zoom) = 10 * t / 60  (constant exponential speed), iterations 250 -> 4000
    ///   B 60 - 62 s  hold at 1e10
    ///   C 62 - 64 s  cycleFade 1 -> 0, then the cycle restarts at 1x and fades in over 1.5 s.
    /// The first cycle starts fully visible so the 0.6 s playlist crossfade can finish at _Fade = 1.
    /// The dome material's _Fade is domeFade (crossfade from the base class) * cycleFade.
    /// The fractal texture is recomputed only when dt > 0 (plus once at setup), so pausing freezes it.
    /// No Update / Time.* anywhere: works identically in play mode and under the editor capture tool.
    /// </summary>
    public class MandelbrotZoomScene : KaleidoSceneBase
    {
        // ---- zoom centre: period-897 minibrot nucleus (superattracting cycle, non-escaping reference)
        const double CenterRe = -0.74364392404504587;
        const double CenterIm = 0.13182591993348175;
        const int RefCount = 4096;          // reference orbit length; _MaxIter is always < RefCount
        const int TextureSize = 768;        // fractal render target (square, mipmapped for the dome border)

        // ---- zoom cycle (seconds of scene-local evolution time)
        const float ZoomSeconds = 60f;
        const float HoldSeconds = 2f;
        const float FadeOutSeconds = 2f;
        const float FadeInSeconds = 1.5f;
        const float CycleSeconds = ZoomSeconds + HoldSeconds + FadeOutSeconds;   // 64 s
        const float MaxLog10Zoom = 10f;     // 1e10
        const float BaseHalfSize = 1.5f;    // half view size in the complex plane at 1x
        const float MinIterations = 250f;
        const float MaxIterations = 4000f;  // < RefCount (4096)
        const float IterationsPerDecade = 350f;
        const float PlaneScale = 0.75f;     // DomeTexture _Scale

        // Matches the Properties blocks of Kaleido/ComputeMandelbrot and Kaleido/DomeTexture exactly.
        // _EvoTime / _Fade / _MainTex come from ShaderIds.
        static readonly int RefOrbitId = Shader.PropertyToID("_RefOrbit");
        static readonly int RefCountId = Shader.PropertyToID("_RefCount");
        static readonly int HalfSizeId = Shader.PropertyToID("_HalfSize");
        static readonly int MaxIterId = Shader.PropertyToID("_MaxIter");
        static readonly int ZoomLogId = Shader.PropertyToID("_ZoomLog");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");

        Material _computeMaterial;   // Kaleido/ComputeMandelbrot (Own'd, not a dome/object material)
        Material _domeMaterial;      // Kaleido/DomeTexture (registered via NewDomeMaterial)
        RenderTexture _fractalRT;
        Texture2D _refOrbit;
        int _refCount = RefCount;

        double _localTime;           // scene-local evolution time = sum of dt (frozen while paused)
        float _domeFade;             // last crossfade value handed to OnFade by the base class
        float _cycleFade = 1f;       // in-scene fade of the zoom cycle (1 during the first dive)

        // ------------------------------------------------------------------ lifecycle
        protected override void OnSetup()
        {
            _localTime = 0.0;
            _cycleFade = 1f;
            _domeFade = 0f;

            _refOrbit = BuildReferenceOrbit();

            _fractalRT = Own(KaleidoRenderUtil.CreateRT(TextureSize, TextureSize, true, "MandelbrotRT"));

            _computeMaterial = Own(KaleidoRenderUtil.NewMaterial("Kaleido/ComputeMandelbrot"));
            _computeMaterial.SetTexture(RefOrbitId, _refOrbit);
            _computeMaterial.SetFloat(RefCountId, _refCount);

            _domeMaterial = NewDomeMaterial("Kaleido/DomeTexture");
            _domeMaterial.SetTexture(ShaderIds.MainTex, _fractalRT);
            _domeMaterial.SetFloat(ScaleId, PlaneScale);
            CreateDome(_domeMaterial);

            // Initial state (zoom 1x, fully visible) so anything rendered before the first Tick is valid.
            ApplyCycle(_localTime);
            RenderFractal();
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (_computeMaterial == null || _fractalRT == null || _domeMaterial == null) return;

            if (dt > 0f)
            {
                _localTime += dt;
                ApplyCycle(_localTime);
                RenderFractal();
            }
            else if (!_fractalRT.IsCreated())
            {
                // Paused and the render target was lost (e.g. Android app suspend). Restoring the
                // last image is not animation: parameters are unchanged so the result is identical.
                _fractalRT.Create();
                RenderFractal();
            }

            ApplyDomeFade();
        }

        protected override void OnFade(float domeFade, float objectFade)
        {
            _domeFade = domeFade;
            ApplyDomeFade();
        }

        protected override void OnTeardown()
        {
            // Everything is Own'd (texture, RT, both materials); the base class destroys it. Drop references only.
            _computeMaterial = null;
            _domeMaterial = null;
            _fractalRT = null;
            _refOrbit = null;
        }

        // ------------------------------------------------------------------ cycle
        /// <summary>Zoom depth and in-scene fade as continuous functions of scene-local time.</summary>
        static void EvaluateCycle(double localTime, out float log10Zoom, out float cycleFade)
        {
            double cycles = Math.Floor(localTime / CycleSeconds);
            float tc = (float)(localTime - cycles * CycleSeconds);          // [0, CycleSeconds)
            if (tc < 0f) tc = 0f;

            // Phase A: constant exponential zoom speed; phases B/C hold the final depth.
            log10Zoom = MaxLog10Zoom * Mathf.Clamp01(tc / ZoomSeconds);

            float fade = 1f;
            float fadeOutStart = ZoomSeconds + HoldSeconds;
            if (tc >= fadeOutStart)
            {
                // Phase C: smooth fade to black (reaches exactly 0 at the end of the cycle).
                fade = 1f - Mathf.SmoothStep(0f, 1f, (tc - fadeOutStart) / FadeOutSeconds);
            }
            else if (cycles > 0.0 && tc < FadeInSeconds)
            {
                // Restart: fade in from black while already zooming again. The very first cycle
                // skips this so the playlist crossfade ends with the dome fully opaque.
                fade = Mathf.SmoothStep(0f, 1f, tc / FadeInSeconds);
            }
            cycleFade = fade;
        }

        void ApplyCycle(double localTime)
        {
            EvaluateCycle(localTime, out float log10Zoom, out float cycleFade);
            _cycleFade = cycleFade;

            float halfSize = (float)(BaseHalfSize * Math.Pow(10.0, -log10Zoom));      // 1.5 -> 1.5e-10
            float maxIter = Mathf.Clamp(MinIterations + IterationsPerDecade * log10Zoom, MinIterations, MaxIterations);
            if (maxIter > _refCount - 1) maxIter = _refCount - 1;                       // never index past the reference

            _computeMaterial.SetFloat(HalfSizeId, halfSize);
            _computeMaterial.SetFloat(MaxIterId, maxIter);
            _computeMaterial.SetFloat(ZoomLogId, log10Zoom);
            _computeMaterial.SetFloat(ShaderIds.EvoTime, (float)localTime);
        }

        void ApplyDomeFade()
        {
            if (_domeMaterial == null) return;
            _domeMaterial.SetFloat(ShaderIds.Fade, Mathf.Clamp01(_domeFade * _cycleFade));
        }

        // ------------------------------------------------------------------ GPU
        /// <summary>Full-screen pass of Kaleido/ComputeMandelbrot into the fractal RT (mips auto-regenerate).</summary>
        void RenderFractal()
        {
            if (_computeMaterial == null || _fractalRT == null) return;
            var prev = RenderTexture.active;
            Graphics.Blit(null, _fractalRT, _computeMaterial, 0);
            RenderTexture.active = prev;
        }

        // ------------------------------------------------------------------ reference orbit
        /// <summary>
        /// Z_n = f^n(0), f(z) = z^2 + c*, iterated in double and stored as float pairs: texel n = (Re Z_n, Im Z_n),
        /// Z_0 = 0. RGFloat when supported, otherwise RGBAFloat (the shader only reads .xy).
        /// Point filtering + clamp so texel n is fetched exactly; linear (non-sRGB) data.
        /// </summary>
        Texture2D BuildReferenceOrbit()
        {
            int count = Mathf.Clamp(SystemInfo.maxTextureSize, 256, RefCount);
            _refCount = count;

            bool rg = SystemInfo.SupportsTextureFormat(TextureFormat.RGFloat);
            TextureFormat format = rg ? TextureFormat.RGFloat : TextureFormat.RGBAFloat;
            int stride = rg ? 2 : 4;

            var tex = Own(new Texture2D(count, 1, format, false, true));
            tex.name = "MandelbrotRefOrbit";
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            var data = new float[count * stride];   // one-off allocation at setup
            double zr = 0.0, zi = 0.0;
            for (int n = 0; n < count; n++)
            {
                int o = n * stride;
                data[o] = (float)zr;
                data[o + 1] = (float)zi;
                double nr = zr * zr - zi * zi + CenterRe;
                double ni = 2.0 * zr * zi + CenterIm;
                zr = nr;
                zi = ni;
                // The nucleus is superattracting: |Z_n| <= ~1.02 for all n (verified for 200000 iterations).
                // Guard anyway so a corrupted constant can never push NaN/Inf into the texture.
                if (!(zr * zr + zi * zi < 16.0)) { zr = 0.0; zi = 0.0; }
            }
            tex.SetPixelData(data, 0);
            tex.Apply(false);
            return tex;
        }
    }
}
