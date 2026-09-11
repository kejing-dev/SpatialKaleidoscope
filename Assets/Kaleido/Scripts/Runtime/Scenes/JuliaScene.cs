using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 7 "Julia set" (PRD section 5).
    ///
    /// The fractal z -> z^2 + c is computed ONCE per frame (not per eye) into a 1024x1024 texture
    /// with a full-screen Graphics.Blit through "Kaleido/ComputeJulia", then shown on the dome by
    /// "Kaleido/DomeTexture" (azimuthal-equidistant projection from the zenith, eye independent).
    ///
    /// Parameter path (90 s closed loop): theta = theta0 + 2*pi*evoTime/90 and
    ///   c = 0.995 * (e^(i theta)/2 - e^(2 i theta)/4)
    /// i.e. the boundary of the main cardioid pulled slightly inside, so the set stays connected
    /// with rich boundary filaments. Angular speed of theta is 4 deg/s (limit: 30 deg/s) and c is
    /// a smooth function of evolution time, so the picture never pops.
    ///
    /// Evolution is driven only by the dt / evoTime handed to OnTick: the texture is recomputed
    /// when dt > 0 (and on the very first tick so it carries the correct phase); while paused
    /// (dt == 0) no GPU work happens and the last texture stays on the dome. Works in play mode
    /// and when ticked by the editor capture tool (no Awake/Start/Update, no coroutines).
    /// </summary>
    public class JuliaScene : KaleidoSceneBase
    {
        // ---- texture / fractal parameters (PRD)
        const int TextureSize = 1024;
        const float LoopSeconds = 90f;          // closed parameter loop
        const double Theta0 = 2.6;              // start phase on the cardioid
        const double CardioidInset = 0.995;     // slightly inside the main cardioid boundary
        const float MaxIterations = 300f;       // matches the [loop] bound inside ComputeJulia
        const float ViewHalfSize = 1.7f;        // _View = (centre.x, centre.y, half-size, 0)
        const float PlaneScale = 0.75f;         // DomeTexture _Scale (plane radius 1 at phi = 1/_Scale rad from the zenith)

        // ---- shader property ids (names match the Properties blocks exactly)
        static readonly int CId = Shader.PropertyToID("_C");             // ComputeJulia
        static readonly int MaxIterId = Shader.PropertyToID("_MaxIter"); // ComputeJulia
        static readonly int ViewId = Shader.PropertyToID("_View");       // ComputeJulia
        static readonly int ScaleId = Shader.PropertyToID("_Scale");     // DomeTexture
        static readonly int TintId = Shader.PropertyToID("_Tint");       // DomeTexture
        // ShaderIds.EvoTime ("_EvoTime") and ShaderIds.MainTex ("_MainTex") come from KaleidoSceneBase.

        RenderTexture _rt;
        Material _compute;
        Material _dome;
        bool _phaseValid;   // false until the first OnTick supplied the real evolution time

        protected override void OnSetup()
        {
            // Fractal target: ARGB32 with mipmaps (DomeTexture samples LOD 6 for the soft border).
            _rt = Own(KaleidoRenderUtil.CreateRT(TextureSize, TextureSize, true, "JuliaRT", RenderTextureFormat.ARGB32));

            // Compute material: not a dome/object material (never drawn by a renderer), so it is
            // only wrapped in Own() and destroyed at Teardown.
            _compute = Own(KaleidoRenderUtil.NewMaterial("Kaleido/ComputeJulia"));
            _compute.SetFloat(MaxIterId, MaxIterations);
            _compute.SetVector(ViewId, new Vector4(0f, 0f, ViewHalfSize, 0f));

            // Dome material: registered through NewDomeMaterial so the base class drives _Fade
            // and the render queue during crossfades.
            _dome = NewDomeMaterial("Kaleido/DomeTexture");
            _dome.SetTexture(ShaderIds.MainTex, _rt);
            _dome.SetFloat(ScaleId, PlaneScale);
            _dome.SetColor(TintId, Color.white);
            CreateDome(_dome);

            // Compute once now so the texture is valid immediately (phase 0); the first OnTick
            // recomputes with the real evolution time before anything is rendered at visible alpha.
            _phaseValid = false;
            Compute(0.0);
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (_rt == null || _compute == null) return;

            bool recompute = dt > 0f || !_phaseValid;
            if (!_rt.IsCreated())
            {
                // GPU resource lost (e.g. app switch on device): the contents are gone, so redraw
                // the very same frame. Same evoTime -> identical image -> still "frozen" if paused.
                _rt.Create();
                recompute = true;
            }
            if (!recompute) return;   // paused: no GPU work, the last texture stays on the dome

            _phaseValid = true;
            Compute(evoTime);
        }

        /// <summary>Set c(evoTime) and draw the Julia set into the texture (one full-screen pass).</summary>
        void Compute(double evoTime)
        {
            // theta advances 2*pi per 90 s = 4 deg/s. Computed in double so long sessions keep precision.
            double theta = Theta0 + 2.0 * System.Math.PI * (evoTime / LoopSeconds);
            // c = inset * (e^(i theta)/2 - e^(2 i theta)/4)
            double re = 0.5 * System.Math.Cos(theta) - 0.25 * System.Math.Cos(2.0 * theta);
            double im = 0.5 * System.Math.Sin(theta) - 0.25 * System.Math.Sin(2.0 * theta);
            _compute.SetVector(CId, new Vector4((float)(CardioidInset * re), (float)(CardioidInset * im), 0f, 0f));
            _compute.SetFloat(ShaderIds.EvoTime, (float)evoTime);

            // Graphics.Blit changes RenderTexture.active; restore it so callers (editor capture
            // tool reads pixels in the same call chain) are not disturbed.
            var prevActive = RenderTexture.active;
            Graphics.Blit(null, _rt, _compute, 0);
            RenderTexture.active = prevActive;
        }
    }
}
