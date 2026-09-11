using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Build-time constants from the PRD (v0.6). No settings UI exists; everything that the PRD
    /// allows to be switched "in the build configuration" lives here.
    /// </summary>
    public static class KaleidoConfig
    {
        // ---- world layout (PRD section 2)
        public const float DomeRadius = 5f;            // inward sphere, pattern centre at zenith, never rotates
        public const float ObjectDistance = 2f;        // 3D objects: 2 m in front of the initial view point
        public const float ObjectDiameter = 1.2f;      // 3D objects: 1.2 m diameter
        public const float ObjectSpinDegPerSec = 6f;   // 3D objects self-rotate at 6 deg/s
        public const float MaxEvolutionDegPerSec = 30f;// visual limit for any angular evolution

        // ---- timing (PRD sections 2 & 3)
        public const float StartFadeInSeconds = 1.0f;
        public const float TransitionSeconds = 0.6f;
        public const float AutoAdvanceSeconds = 90f;
        public const float LongPinchSeconds = 1.0f;
        public const float FirstLaunchHintSeconds = 3f;
        public const float ReminderMinutes = 20f;       // after 20 min of continuous use, dim slowly...
        public const float ReminderDimTarget = 0.30f;   // ...down to 30 % of normal content brightness
        public const float ReminderDimSeconds = 30f;    // over 30 s (slow, never a flash)

        // ---- brightness governor (PRD section 7): applied after tone mapping, in-app only
        public const float OutputBrightnessCap = 0.60f;
        public const float MaxBrightnessStepPerSecond = 1.5f; // limits frame-to-frame change of the governor

        // ---- physical kaleidoscope (PRD section 4)
        public const bool HeadTiltGravityEnabled = true; // map head roll to cell gravity (build-time switch)
        public const float HeadTiltDeadZoneDeg = 4f;
        public const float HeadTiltMaxDeg = 35f;
        public const float HeadTiltSmoothing = 2.5f;    // 1/s, exponential smoothing
        public const float CellTextureSize = 768f;

        // ---- persistence
        public const string PrefsIndexKey = "kaleido.lastIndex";
        public const string PrefsFirstLaunchKey = "kaleido.firstLaunchDone";
        public const int SceneCount = 12;

        // ---- layers (set up by the scene builder; falls back gracefully if missing)
        public const string CellLayerName = "KaleidoCell";
        public const string BulbLayerName = "KaleidoBulb";
    }
}
