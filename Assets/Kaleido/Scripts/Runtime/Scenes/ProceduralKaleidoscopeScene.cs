using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 5 "procedural kaleidoscope" (dome only).
    /// One dome renderer with Kaleido/DomeProcedural: a 12-sector polar fold whose phase turns once
    /// every 60 s (6 deg/s) over a slowly drifting procedural noise field. Every shader input is a
    /// closed-form function of the evolution time handed to OnTick, so pausing (dt == 0, evoTime
    /// frozen) freezes the image exactly and nothing is accumulated, allocated or reset per frame.
    /// Behaves identically in play mode and when ticked by the editor capture tool.
    ///
    /// GPU budget (PRD section 5: scene 5 target &lt;= 1 ms, whole frame &lt;= 8 ms at 90 Hz; the PRD
    /// states these are unmeasured targets). Kaleido/DomeProcedural evaluates two 4-octave 3D
    /// value-noise fbms per fragment for every dome pixel of both eyes, which is likely over the
    /// 1 ms row on Swan. This class cannot change that cost (the shader has no quality knobs);
    /// the planned mitigation is a shader-side change: render the folded wedge (r, a) once per
    /// tick into a small RT and let the dome shader do only the fold + one texture sample. Until
    /// the device measurement exists, treat this scene as a budget risk, not a defect.
    /// </summary>
    public class ProceduralKaleidoscopeScene : KaleidoSceneBase
    {
        // Must match the Properties block of Assets/Kaleido/Resources/Shaders/DomeProcedural.shader.
        // _Fade is driven by KaleidoSceneBase through NewDomeMaterial.
        static readonly int SectorsId = Shader.PropertyToID("_Sectors");
        static readonly int PhaseId = Shader.PropertyToID("_Phase");
        static readonly int DriftId = Shader.PropertyToID("_Drift");

        const float Sectors = 12f;
        const double TurnSeconds = 60.0;                  // one full phase turn per 60 s = 6 deg/s
        const double TwoPi = System.Math.PI * 2.0;

        Material _material;

        protected override void OnSetup()
        {
            _material = NewDomeMaterial("Kaleido/DomeProcedural"); // owned + _Fade + queue by the base
            CreateDome(_material);
            Apply(0.0); // sane initial state; the controller always ticks before the first render
        }

        protected override void OnTick(float dt, double evoTime)
        {
            // No integration: everything is a pure function of evoTime, and EvolutionClock stops
            // advancing evoTime while paused (dt == 0), so the picture freezes exactly.
            Apply(evoTime);
        }

        void Apply(double evoTime)
        {
            if (_material == null) return;

            // Phase: 2*pi/60 rad per second. Wrapped to one turn in double precision before the float
            // cast. This is exact for the shader: FoldSector reduces modulo 2w = 2*pi/_Sectors and
            // 2*pi is an integer multiple of that, so the wrap is invisible and float precision of the
            // phase never degrades over long sessions.
            float phase = (float)(Wrap(evoTime, TurnSeconds) * (TwoPi / TurnSeconds));

            // Drift: xy translate the noise field, z is the noise "time" slice, w offsets the ring
            // crests. xyz feed non-periodic noise and must keep growing to remain continuous; w is only
            // used inside cos(), so it is wrapped to 2*pi exactly.
            var drift = new Vector4(
                (float)(evoTime * 0.02),
                (float)(evoTime * 0.013),
                (float)evoTime,
                (float)Wrap(evoTime * 0.03, TwoPi));

            _material.SetFloat(SectorsId, Sectors);
            _material.SetFloat(PhaseId, phase);
            _material.SetVector(DriftId, drift);
        }

        /// <summary>x modulo period into [0, period); correct for negative x as well.</summary>
        static double Wrap(double x, double period)
        {
            double r = x - System.Math.Floor(x / period) * period;
            return r < period ? r : 0.0; // rounding can land exactly on 'period'; 0 is equivalent
        }
    }
}
