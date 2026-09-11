using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Shared 2D physics driver for the fragment cells. Stepped exactly once per frame by the
    /// PlaybackController with evolution time, so pausing freezes every cell. Gravity direction =
    /// slow virtual tube rotation (keeps fragments tumbling, like turning a real kaleidoscope) plus
    /// the smoothed head roll.
    /// </summary>
    public static class CellPhysics
    {
        public const float FixedStep = 1f / 120f;
        public const float TubeRotationDegPerSec = 8f;   // full turn every 45 s
        public const float GravityStrength = 0.55f;      // cell units / s^2 (cell radius = 1)

        static float _accumulator;
        static float _tubeAngleDeg;
        static bool _initialised;

        public static float GravityAngleDeg { get; private set; }

        public static void EnsureInitialised()
        {
            if (_initialised) return;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(0f, -GravityStrength);
            _initialised = true;
        }

        public static void Step(float evoDt, float headRollDeg)
        {
            EnsureInitialised();
            if (evoDt <= 0f) return;
            _tubeAngleDeg = Mathf.Repeat(_tubeAngleDeg + TubeRotationDegPerSec * evoDt, 360f);
            GravityAngleDeg = _tubeAngleDeg + headRollDeg;
            float a = GravityAngleDeg * Mathf.Deg2Rad;
            // gravity starts pointing "down" (-Y) and rotates with the tube / head
            Physics2D.gravity = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * GravityStrength;

            _accumulator += evoDt;
            while (_accumulator >= FixedStep)
            {
                Physics2D.Simulate(FixedStep);
                _accumulator -= FixedStep;
            }
            if (_accumulator > 1e-5f)
            {
                // finish with the remainder so the rendered cell state lands exactly on evolution time (no 0-8 ms jitter)
                Physics2D.Simulate(_accumulator);
                _accumulator = 0f;
            }
        }

        public static void Reset()
        {
            _accumulator = 0f;
        }
    }
}
