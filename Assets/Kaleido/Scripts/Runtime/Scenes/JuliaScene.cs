using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 7 "Julia set" (PRD section 5).
    ///
    /// The fractal z -> z^2 + c is computed ONCE (not per eye) into a 2048x2048 texture with
    /// "Kaleido/ComputeJulia" through a <see cref="FractalTexturePipeline"/>: two render textures,
    /// the finished one is shown on the dome by "Kaleido/DomeTexture" (azimuthal-equidistant
    /// projection from the zenith, eye independent) while the other is filled in 2 horizontal bands,
    /// one band per tick. Each tick therefore shades 2048*2048/2 = 2.1M single-sample texels (the
    /// distance-estimate colouring is continuous, so it needs no supersampling), about half of the
    /// previous 1024x1024 x 4 samples, for a 4x sharper texture (the dome samples it through mips,
    /// so the extra resolution acts as supersampling on screen).
    ///
    /// Parameter path (90 s closed loop): theta = theta0 + 2*pi*evoTime/90 and
    ///   c = 0.995 * (e^(i theta)/2 - e^(2 i theta)/4)
    /// i.e. the boundary of the main cardioid pulled slightly inside, so the set stays connected
    /// with rich boundary filaments. Angular speed of theta is 4 deg/s (limit: 30 deg/s) and c is
    /// a smooth function of evolution time, so the picture never pops. c is sampled at the start of
    /// every image (every 2 ticks) and all bands of one image share it: no seams.
    ///
    /// Evolution is driven only by the dt / evoTime handed to OnTick: a band is rendered only when
    /// dt > 0; while paused (dt == 0) no GPU work happens and the front texture stays on the dome.
    /// Works in play mode and when ticked by the editor capture tool (no Awake/Start/Update, no
    /// coroutines): OnSetup renders a complete image for the current evolution time.
    /// </summary>
    public class JuliaScene : KaleidoSceneBase
    {
        // ---- texture / fractal parameters (PRD)
        const int TextureSize = 2048;
        const int BandsPerImage = 2;            // one band per tick -> a new image every 2 ticks
        const float LoopSeconds = 90f;          // closed parameter loop
        const double Theta0 = 2.6;              // start phase on the cardioid
        const double CardioidInset = 0.995;     // slightly inside the main cardioid boundary
        const float MaxIterations = 160f;       // matches the [loop] bound inside ComputeJulia
        const float ViewHalfSize = 1.7f;        // _View = (centre.x, centre.y, half-size, 0)
        const float PlaneScale = 0.526f;        // DomeTexture _Scale (plane radius 1 at phi = 1/_Scale rad from the zenith); _FoldRadius stays at its 1.9 default

        // ---- shader property ids (names match the Properties blocks exactly)
        static readonly int CId = Shader.PropertyToID("_C");                   // ComputeJulia
        static readonly int MaxIterId = Shader.PropertyToID("_MaxIter");       // ComputeJulia
        static readonly int ViewId = Shader.PropertyToID("_View");             // ComputeJulia
        static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");   // ComputeJulia
        static readonly int ScaleId = Shader.PropertyToID("_Scale");           // DomeTexture
        static readonly int TintId = Shader.PropertyToID("_Tint");             // DomeTexture
        // ShaderIds.EvoTime ("_EvoTime") and ShaderIds.MainTex ("_MainTex") come from KaleidoSceneBase.
        // "_Band" (ComputeJulia) is driven by FractalTexturePipeline.

        FractalTexturePipeline _pipe;
        Material _compute;
        Material _dome;

        protected override void OnSetup()
        {
            // Fractal targets: ARGB32 with mipmaps, colour only, trilinear (DomeTexture picks the mip
            // matching the screen footprint; trilinear blending keeps the mip 0 -> 1 transition below
            // the horizon invisible). Both textures are owned here.
            _pipe = new FractalTexturePipeline(TextureSize, RenderTextureFormat.ARGB32, true, BandsPerImage, "JuliaRT");
            Own(_pipe.Front);
            Own(_pipe.Back);

            // Compute material: not a dome/object material (never drawn by a renderer), so it is
            // only wrapped in Own() and destroyed at Teardown.
            _compute = Own(KaleidoRenderUtil.NewMaterial("Kaleido/ComputeJulia"));
            _compute.SetFloat(MaxIterId, MaxIterations);
            _compute.SetFloat(TexelSizeId, 1f / TextureSize);
            _compute.SetVector(ViewId, new Vector4(0f, 0f, ViewHalfSize, 0f));

            // Dome material: registered through NewDomeMaterial so the base class drives _Fade
            // and the render queue during crossfades.
            _dome = NewDomeMaterial("Kaleido/DomeTexture");
            _dome.SetFloat(ScaleId, PlaneScale);
            _dome.SetColor(TintId, Color.white);
            CreateDome(_dome);

            // Render a complete image now for the current evolution time (the same value the first
            // OnTick receives, in play mode and under the editor capture tool alike), so the very
            // first frame - even a paused one - shows the correct phase.
            SetFractalParams(EvolutionClock.Time);
            _pipe.RenderAll(_compute);
            ShowFront();
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (_pipe == null || _compute == null || _dome == null || _pipe.Front == null) return;

            if (_pipe.EnsureCreated())
            {
                // GPU resources lost (e.g. app switch on device): the contents are gone, so redraw a
                // whole image right away. While paused the uniforms are unchanged -> identical image
                // -> still "frozen"; otherwise take the current evolution time.
                if (dt > 0f) SetFractalParams(evoTime);
                _pipe.RenderAll(_compute);
                ShowFront();
                return;
            }

            if (dt <= 0f) return;   // paused: no GPU work, the front texture stays on the dome

            if (!_pipe.InProgress)
            {
                // new image: sample c for the current evolution time; the uniforms stay untouched
                // until every band of this image has been rendered
                SetFractalParams(evoTime);
                _pipe.Begin(_compute);
            }
            if (_pipe.Step()) ShowFront();   // last band -> buffers swapped -> show the new image
        }

        protected override void OnTeardown()
        {
            // Both textures and both materials are Own'd; the base class destroys them. Drop references only.
            _pipe = null;
            _compute = null;
            _dome = null;
        }

        /// <summary>Point the dome at the finished image (its identity changes on every swap).</summary>
        void ShowFront()
        {
            if (_dome != null && _pipe != null) _dome.SetTexture(ShaderIds.MainTex, _pipe.Front);
        }

        /// <summary>Set c(evoTime) and the palette clock on the compute material (call only between images).</summary>
        void SetFractalParams(double evoTime)
        {
            // theta advances 2*pi per 90 s = 4 deg/s. Computed in double so long sessions keep precision.
            double theta = Theta0 + 2.0 * System.Math.PI * (evoTime / LoopSeconds);
            // c = inset * (e^(i theta)/2 - e^(2 i theta)/4)
            double re = 0.5 * System.Math.Cos(theta) - 0.25 * System.Math.Cos(2.0 * theta);
            double im = 0.5 * System.Math.Sin(theta) - 0.25 * System.Math.Sin(2.0 * theta);
            _compute.SetVector(CId, new Vector4((float)(CardioidInset * re), (float)(CardioidInset * im), 0f, 0f));
            _compute.SetFloat(ShaderIds.EvoTime, (float)evoTime);
        }
    }
}
