using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Head roll -> kaleidoscope cell gravity mapping (PRD section 4): smoothed, dead-zoned and
    /// clamped. Can be switched off at build time via KaleidoConfig.HeadTiltGravityEnabled.
    /// </summary>
    public class HeadTilt : MonoBehaviour
    {
        public Camera Source;
        public float SmoothedRollDeg { get; private set; }
        public float RawRollDeg { get; private set; }

        public void Step(float realDt)
        {
            if (!KaleidoConfig.HeadTiltGravityEnabled || Source == null)
            {
                SmoothedRollDeg = 0f;
                return;
            }
            // Roll about the view axis, independent of yaw/pitch: angle between camera-right and the horizontal plane.
            Vector3 right = Source.transform.right;
            Vector3 fwd = Source.transform.forward;
            Vector3 flatRight = Vector3.Cross(Vector3.up, fwd);
            if (flatRight.sqrMagnitude < 1e-4f) { RawRollDeg = 0f; }
            else
            {
                flatRight.Normalize();
                RawRollDeg = Mathf.Atan2(Vector3.Dot(right, Vector3.up), Vector3.Dot(right, flatRight)) * Mathf.Rad2Deg;
            }
            float dz = KaleidoConfig.HeadTiltDeadZoneDeg;
            float mapped = Mathf.Abs(RawRollDeg) <= dz ? 0f : Mathf.Sign(RawRollDeg) * (Mathf.Abs(RawRollDeg) - dz);
            mapped = Mathf.Clamp(mapped, -KaleidoConfig.HeadTiltMaxDeg, KaleidoConfig.HeadTiltMaxDeg);
            float k = 1f - Mathf.Exp(-KaleidoConfig.HeadTiltSmoothing * Mathf.Max(realDt, 0f));
            SmoothedRollDeg = Mathf.Lerp(SmoothedRollDeg, mapped, k);
        }
    }
}
