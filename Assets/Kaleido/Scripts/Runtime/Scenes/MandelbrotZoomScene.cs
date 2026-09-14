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
    /// Rendering goes through a <see cref="FractalTexturePipeline"/>: a 1024x1024 image is drawn in
    /// 2 horizontal bands, one band per tick (0.5M texels per tick, plain perturbation iteration,
    /// no derivative term), into the back texture while the dome shows the front one. The zoom
    /// parameters are frozen per image, so the zoom advances once per image (every 2 ticks: a
    /// 0.85 % step at 90 Hz, invisible) and the two bands can never show different depths.
    ///
    /// Cycle (scene-local evolution time, sum of dt so dt == 0 freezes everything, GPU work included):
    ///   A  0 - 60 s  log10(zoom) = 10 * t / 60  (constant exponential speed), iterations 250 -> 1200 (cap)
    ///   B 60 - 62 s  hold at 1e10
    ///   C 62 - 64 s  cycleFade 1 -> 0, then the cycle restarts at 1x and fades in over 1.5 s.
    /// The first cycle starts fully visible so the 0.6 s playlist crossfade can finish at _Fade = 1.
    /// The dome material's _Fade is domeFade (crossfade from the base class) * cycleFade.
    /// Bands are rendered only when dt > 0 (plus a whole image at setup), so pausing freezes the picture.
    /// No Update / Time.* anywhere: works identically in play mode and under the editor capture tool.
    /// </summary>
    public class MandelbrotZoomScene : KaleidoSceneBase
    {
        // ---- zoom centre: period-897 minibrot nucleus (superattracting cycle, non-escaping reference)
        const double CenterRe = -0.74364392404504587;
        const double CenterIm = 0.13182591993348175;
        const int RefCount = 4096;          // reference orbit length; _MaxIter is always < RefCount
        const int TextureSize = 896;       // fractal render target (square, mipmapped + trilinear, colour only)
        const int BandsPerImage = 2;        // one band per tick -> a new image (and zoom step) every 2 ticks

        // ---- zoom cycle (seconds of scene-local evolution time)
        const float ZoomSeconds = 60f;
        const float HoldSeconds = 2f;
        const float FadeOutSeconds = 2f;
        const float FadeInSeconds = 1.5f;
        const float CycleSeconds = ZoomSeconds + HoldSeconds + FadeOutSeconds;   // 64 s
        const float MaxLog10Zoom = 10f;     // 1e10
        const float BaseHalfSize = 1.5f;    // half view size in the complex plane at 1x
        const float MinIterations = 250f;
        const float MaxIterations = 1200f;  // cap (< RefCount 4096, < the 2048 [loop] bound in the shader)
        const float IterationsPerDecade = 120f;
        const float PlaneScale = 0.526f;    // DomeTexture _Scale (_FoldRadius stays at its 1.9 default)

        // Matches the Properties blocks of Kaleido/ComputeMandelbrot and Kaleido/DomeTexture exactly.
        // _EvoTime / _Fade / _MainTex come from ShaderIds; "_Band" is driven by FractalTexturePipeline.
        static readonly int RefOrbitId = Shader.PropertyToID("_RefOrbit");
        static readonly int RefCountId = Shader.PropertyToID("_RefCount");
        static readonly int HalfSizeId = Shader.PropertyToID("_HalfSize");
        static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");
        static readonly int MaxIterId = Shader.PropertyToID("_MaxIter");
        static readonly int ZoomLogId = Shader.PropertyToID("_ZoomLog");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");
        static readonly int LodBiasId = Shader.PropertyToID("_LodBias");

        Material _computeMaterial;   // Kaleido/ComputeMandelbrot (Own'd, not a dome/object material)
        Material _domeMaterial;      // Kaleido/DomeTexture (registered via NewDomeMaterial)
        FractalTexturePipeline _pipe;
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

            _pipe = new FractalTexturePipeline(TextureSize, RenderTextureFormat.ARGB32, true, BandsPerImage, "MandelbrotRT");
            Own(_pipe.Front);
            Own(_pipe.Back);

            _computeMaterial = Own(KaleidoRenderUtil.NewMaterial("Kaleido/ComputeMandelbrot"));
            _computeMaterial.SetFloat(TexelSizeId, 1f / TextureSize);
            _computeMaterial.SetTexture(RefOrbitId, _refOrbit);
            _computeMaterial.SetFloat(RefCountId, _refCount);

            _domeMaterial = NewDomeMaterial("Kaleido/DomeTexture");
            _domeMaterial.SetFloat(LodBiasId, 0.3f);   // slight softening: dense filament texture must not read as noise
            _domeMaterial.SetFloat(ScaleId, PlaneScale);
            CreateDome(_domeMaterial);

            // Initial state (zoom 1x, fully visible): render a whole image so anything drawn before
            // the first Tick is valid.
            EvaluateCycle(_localTime, out _, out _cycleFade);
            ApplyZoomUniforms(_localTime);
            _pipe.RenderAll(_computeMaterial);
            ShowFront();
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (_computeMaterial == null || _domeMaterial == null || _pipe == null || _pipe.Front == null) return;

            if (dt > 0f)
            {
                _localTime += dt;
                EvaluateCycle(_localTime, out _, out _cycleFade);   // the fade is continuous per tick

                if (_pipe.EnsureCreated())
                {
                    // Render target lost (e.g. Android app suspend): redraw a whole image at the current depth.
                    ApplyZoomUniforms(_localTime);
                    _pipe.RenderAll(_computeMaterial);
                    ShowFront();
                }
                else
                {
                    if (!_pipe.InProgress)
                    {
                        // New image: freeze the zoom parameters for all of its bands.
                        ApplyZoomUniforms(_localTime);
                        _pipe.Begin(_computeMaterial);
                    }
                    if (_pipe.Step()) ShowFront();   // last band -> swapped -> show the new depth
                }
            }
            else if (_pipe.EnsureCreated())
            {
                // Paused and the render target was lost. Restoring the image is not animation:
                // the compute uniforms are unchanged, so the result is identical.
                _pipe.RenderAll(_computeMaterial);
                ShowFront();
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
            // Everything is Own'd (orbit texture, both RTs, both materials); the base class destroys it. Drop references only.
            _computeMaterial = null;
            _domeMaterial = null;
            _pipe = null;
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

        /// <summary>
        /// Set the compute uniforms for ONE image (zoom depth, iteration cap, palette clock). Called
        /// only between images: every band of an image is rendered with the same parameters.
        /// </summary>
        void ApplyZoomUniforms(double localTime)
        {
            EvaluateCycle(localTime, out float log10Zoom, out _);

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

        /// <summary>Point the dome at the finished image (its identity changes on every swap).</summary>
        void ShowFront()
        {
            if (_domeMaterial != null && _pipe != null) _domeMaterial.SetTexture(ShaderIds.MainTex, _pipe.Front);
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
