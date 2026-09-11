using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Independent evolution time that drives every effect. Pausing freezes this clock only;
    /// XR tracking, rendering and system interaction keep running on Unity's own time.
    /// </summary>
    public static class EvolutionClock
    {
        public static bool Paused { get; private set; }
        public static double Time { get; private set; }
        public static float DeltaTime { get; private set; }

        public static void SetPaused(bool paused) => Paused = paused;

        /// <summary>Advance by the real frame delta (clamped so a hitch never becomes a jump).</summary>
        public static void Advance(float realDelta)
        {
            float dt = Paused ? 0f : Mathf.Clamp(realDelta, 0f, 0.05f);
            DeltaTime = dt;
            Time += dt;
        }

        public static void Reset()
        {
            Time = 0;
            DeltaTime = 0;
            Paused = false;
        }
    }
}
