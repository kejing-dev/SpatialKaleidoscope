using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Application-side output brightness control (PRD section 7). Publishes the global shader
    /// uniform _KaleidoBrightness = cap * dimming, rate-limited so no frame can jump in luminance.
    /// It is not the headset brightness and does not touch system settings.
    /// </summary>
    public class BrightnessGovernor : MonoBehaviour
    {
        public float Cap = KaleidoConfig.OutputBrightnessCap;
        public float DimFactor { get; private set; } = 1f;   // 1 = normal, 0.3 = 20-min reminder
        public float Current { get; private set; }
        float _dimTarget = 1f;
        float _dimRate = 1f;

        void Awake()
        {
            Current = Cap;
            Shader.SetGlobalFloat(ShaderIds.Brightness, Current);
        }

        /// <summary>Move dimming to <paramref name="target"/> over <paramref name="seconds"/>.</summary>
        public void SetDimming(float target, float seconds)
        {
            _dimTarget = Mathf.Clamp01(target);
            _dimRate = Mathf.Abs(_dimTarget - DimFactor) / Mathf.Max(seconds, 0.05f);
        }

        public void Step(float realDt)
        {
            DimFactor = Mathf.MoveTowards(DimFactor, _dimTarget, _dimRate * realDt);
            float target = Cap * DimFactor;
            float maxStep = KaleidoConfig.MaxBrightnessStepPerSecond * realDt;
            Current = Mathf.MoveTowards(Current, target, maxStep);
            Shader.SetGlobalFloat(ShaderIds.Brightness, Current);
        }

        public void ForceImmediate()
        {
            Current = Cap * DimFactor;
            Shader.SetGlobalFloat(ShaderIds.Brightness, Current);
        }
    }
}
