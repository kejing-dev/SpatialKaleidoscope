using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 11 "phyllotaxis spiral" (dome). A quiet dark backdrop (Kaleido/DomeDark) plus
    /// 2200 dots laid out by Vogel's model on the inside of the dome: dot n sits at angle-from-zenith
    /// phi = c * sqrt(n) and azimuth n * alpha. The whole layout is computed in the vertex shader of
    /// Kaleido/Phyllotaxis from an indexed quad mesh (uv2.x = n), so this class only feeds uniforms.
    ///
    /// Evolution: the divergence angle alpha breathes 137.5 deg +- 0.13 deg with a 60 s round trip as
    /// a pure sine of evolution time, and the hue drift inside the shader reads _EvoTime. Both are
    /// continuous functions of evoTime; while paused (dt == 0) evoTime is frozen and the image holds
    /// still. Brightness is constant by design (PRD: no sudden luminance changes). No Update(),
    /// coroutines or Time.* are used, so the scene works identically in play mode and when the editor
    /// capture tool ticks it manually.
    ///
    /// Why +- 0.13 deg and not the +- 1 deg of the original spec: dot n has azimuth n * alpha, so its
    /// azimuth rate is n * d(alpha)/dt and the rim (n = 2199) is 2199x faster than d(alpha)/dt. With a
    /// 1 deg swing the rim would peak at ~230 deg/s of azimuth (~210 deg/s on the sphere, ~25 cm per
    /// frame at 72 Hz against a ~8 cm dot), i.e. a strobing shimmer that breaks the PRD limit of
    /// KaleidoConfig.MaxEvolutionDegPerSec (30 deg/s). 0.13 deg keeps the rim at 29.9 deg/s of
    /// azimuth (27.3 deg/s on-sphere, ~3.3 cm per frame) while dot 2199 still sweeps +- 286 deg of
    /// azimuth per half cycle, so the parastichies visibly reorganise every minute.
    /// </summary>
    public class PhyllotaxisScene : KaleidoSceneBase
    {
        // ---- layout (shader inputs)
        const int DotCount = 2200;
        const float MeshBoundsRadius = 5.5f;                 // generous: vertices are placed by the shader
        const float DotRadius = 4.98f;                        // just inside the 5 m dome
        const float DotSize = 0.008f;                         // dot angular size (rad)
        const float RadialScale = 1.15f;                      // _C = RadialScale / sqrt(N) -> outermost dot at ~66 deg from zenith

        // ---- evolution
        const float BaseDivergenceDeg = 137.5f;               // golden angle
        const float DivergenceSwingDeg = 0.13f;               // +- swing (see the class summary for the derivation)
        const double DivergencePeriodSeconds = 60.0;          // full round trip
        // Rate budget: dot n has azimuth n * alpha, so its azimuth rate is n * d(alpha)/dt, which peaks at
        // swing * 2*pi / period = 0.0136 deg/s. Rim (n = 2199): 2199 * 0.0136 = 29.9 deg/s of azimuth,
        // 27.3 deg/s on-sphere (sin(66 deg)), just under KaleidoConfig.MaxEvolutionDegPerSec = 30.
        // Keep (DotCount - 1) * DivergenceSwingDeg * 2*pi / DivergencePeriodSeconds <= 30 when retuning
        // (e.g. a +- 1 deg swing would need a period of >= 461 s).

        // Property names match the Properties block of Kaleido/Phyllotaxis exactly.
        // _Fade and _EvoTime are shared ids from ShaderIds; _Fade is driven by the base class.
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int CId = Shader.PropertyToID("_C");
        static readonly int DotSizeId = Shader.PropertyToID("_DotSize");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");

        Material _domeMaterial;
        Material _dotMaterial;
        Mesh _dotMesh;

        protected override void OnSetup()
        {
            // Background: dark gradient + static (non-twinkling) stars. Registered as a dome material so
            // the base class owns it, drives _Fade and assigns the dome render queue.
            _domeMaterial = NewDomeMaterial("Kaleido/DomeDark");
            CreateDome(_domeMaterial);

            // Dots: one indexed quad per seed; positions are computed in the vertex shader from uv2.x.
            _dotMaterial = NewObjectMaterial("Kaleido/Phyllotaxis");
            _dotMesh = Own(MeshUtil.IndexedQuads(DotCount, MeshBoundsRadius));
            var dots = CreateMeshObject("PhyllotaxisDots", transform, _dotMesh, _dotMaterial, 0);
            // Same placement as CreateDome: dome root (world origin), identity rotation - the dome never spins.
            dots.transform.position = Ctx.DomeRoot.position;
            dots.transform.rotation = Quaternion.identity;

            // Static shader inputs.
            _dotMaterial.SetFloat(CId, RadialScale / Mathf.Sqrt(DotCount));
            _dotMaterial.SetFloat(DotSizeId, DotSize);
            _dotMaterial.SetFloat(RadiusId, DotRadius);

            // Seed the time-dependent inputs from the shared evolution clock so anything rendered
            // between Setup and the first Tick (e.g. an editor JumpTo + capture) shows the same phase
            // the first Tick will produce - no one-frame jump.
            ApplyEvolution(EvolutionClock.Time);
        }

        protected override void OnTick(float dt, double evoTime)
        {
            // dt is intentionally unused: every visual quantity is a pure function of evoTime, which
            // the controller freezes while paused, so pausing holds the pattern exactly.
            if (_dotMaterial == null) return;
            ApplyEvolution(evoTime);
        }

        protected override void OnTeardown()
        {
            // Materials and the mesh are owned by the base class (NewDomeMaterial / NewObjectMaterial /
            // Own) and destroyed in Teardown(); just drop the references.
            _domeMaterial = null;
            _dotMaterial = null;
            _dotMesh = null;
        }

        void ApplyEvolution(double evoTime)
        {
            // alpha(t) = 137.5 + 0.13 * sin(2*pi*t/60) degrees, converted to radians for the shader.
            double phase = 2.0 * System.Math.PI * evoTime / DivergencePeriodSeconds;
            float alphaDeg = BaseDivergenceDeg + DivergenceSwingDeg * (float)System.Math.Sin(phase);
            _dotMaterial.SetFloat(AlphaId, alphaDeg * Mathf.Deg2Rad);
            _dotMaterial.SetFloat(ShaderIds.EvoTime, (float)evoTime);
        }
    }
}
