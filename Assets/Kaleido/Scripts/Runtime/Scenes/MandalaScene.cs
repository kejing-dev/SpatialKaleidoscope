using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 6 "mandala" (dome only). Concentric rings flow outward from the zenith with
    /// 8-fold radial symmetry. All motion lives inside Kaleido/DomeMandala as smooth periodic
    /// functions of evolution time, so this class only forwards the evolution clock to the
    /// material every tick. With dt == 0 (paused) evoTime does not advance and the image freezes.
    /// Works identically in play mode and when ticked by the editor capture tool.
    /// </summary>
    public class MandalaScene : KaleidoSceneBase
    {
        /// <summary>Base flow speed (1x per the PRD). At 1x every angular term in the shader stays far below 30 deg/s.</summary>
        const float FlowSpeed = 1f;

        // Matches the Properties block of Kaleido/DomeMandala exactly. _EvoTime / _Fade come from ShaderIds.
        static readonly int SpeedId = Shader.PropertyToID("_Speed");

        Material _domeMaterial;

        protected override void OnSetup()
        {
            // NewDomeMaterial registers the material with the base class (Own + _Fade + render queue).
            _domeMaterial = NewDomeMaterial("Kaleido/DomeMandala");
            _domeMaterial.SetFloat(SpeedId, FlowSpeed);
            // Seed with the current evolution clock so nothing rendered between Setup and the first
            // Tick (e.g. an editor JumpTo + capture) shows a different phase than the first Tick.
            _domeMaterial.SetFloat(ShaderIds.EvoTime, (float)EvolutionClock.Time);
            CreateDome(_domeMaterial);
        }

        protected override void OnTick(float dt, double evoTime)
        {
            // dt is intentionally unused: the shader is a pure function of evoTime, which is frozen while paused.
            if (_domeMaterial == null) return;
            _domeMaterial.SetFloat(ShaderIds.EvoTime, (float)evoTime);
            _domeMaterial.SetFloat(SpeedId, FlowSpeed);
        }

        protected override void OnTeardown()
        {
            // The dome material is owned by the base class and destroyed in Teardown(); just drop the reference.
            _domeMaterial = null;
        }
    }
}
