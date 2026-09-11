using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 10 "Lorenz attractor" (3D object).
    ///
    /// The Lorenz system (sigma 10, rho 28, beta 8/3) is integrated with RK4 at fixed 1/240 s
    /// sub-steps of simulation time, 0.5 simulation seconds per evolution second. Lorenz (x, y, z)
    /// maps to local (x, z - 27, y) * (1.2 / 55) so the attractor fits the 1.2 m object.
    ///
    /// Two view-facing ribbons (Kaleido/GlowLine, additive):
    ///   Trail - the last 3.0 evolution seconds of the trajectory, one point per sub-step in a ring
    ///           buffer, rebuilt into a dynamic mesh only when dt is positive. The head vertex is an
    ///           uncommitted partial RK4 step of the accumulator remainder, so the head position and
    ///           every age value are continuous functions of evolution time (no sub-step stutter).
    ///   Ghost - a static ribbon of 60 simulation seconds (14 400 points) with age 0, starting at
    ///           the trail's oldest point. Same integrator, same step, same start state, so the trail
    ///           rides exactly on the ghost for 120 evolution seconds (longer than the 90 s dwell).
    ///
    /// All animation comes from the dt passed to OnTick; dt == 0 freezes integration and the mesh.
    /// Works in edit mode (capture tool): nothing depends on Awake/Start/Update or Time.*.
    /// The scene is fully deterministic (fixed start state, fixed step), so no random source is needed.
    /// </summary>
    public sealed class LorenzScene : KaleidoSceneBase
    {
        // ---- Lorenz system
        const double Sigma = 10.0;
        const double Rho = 28.0;
        const double Beta = 8.0 / 3.0;
        const double CentreZ = 27.0;

        // ---- timing (all step counts are exact integers derived from SubStepsPerSimSecond)
        const int SubStepsPerSimSecond = 240;                       // RK4 sub-steps per simulation second
        const float SubStep = 1f / SubStepsPerSimSecond;            // simulation seconds per RK4 step
        const float SimSpeed = 0.5f;                                // simulation seconds per evolution second (calm)
        const int PreRunSteps = 5 * SubStepsPerSimSecond;           // 5 s of simulation before the trail starts (1200)
        const int TrailPoints = 3 * SubStepsPerSimSecond / 2;       // 3.0 evolution s * 0.5 sim/evo * 240 (360)
        const int GhostPoints = 60 * SubStepsPerSimSecond;          // 60 s of simulation as the static ghost (14400)
        const int MaxSubStepsPerTick = 32;                          // never spiral after a hitch (dt is clamped upstream anyway)

        // ---- geometry
        const float Scale = 1.2f / 55f;         // Lorenz units -> metres
        const float BoundsSize = 1.5f;          // fixed local bounds cube (attractor spans ~1.2 m)

        // ---- looks
        const float TrailWidth = 0.012f;
        const float TrailIntensity = 1.0f;
        static readonly Color TrailColor = new Color(0.45f, 0.9f, 1f, 1f);      // cyan-white
        const float GhostWidth = 0.004f;
        const float GhostIntensity = 0.15f;
        static readonly Color GhostColor = new Color(0.55f, 0.25f, 0.9f, 1f);   // purple

        // ---- shader property ids (must match Kaleido/GlowLine Properties block; _Fade is driven by the base class)
        static readonly int WidthId = Shader.PropertyToID("_Width");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        // ---- integrator state (double precision; committed state at the last completed sub-step)
        double _x, _y, _z;
        float _accum;                            // simulation seconds not yet integrated (< SubStep)

        // ---- trail ring buffer of local-space points, newest at _head
        Vector3[] _ring;
        int _ringCapacity;
        int _head = -1;
        int _count;

        // ---- trail ribbon (reused lists, fixed vertex count = (capacity + 1) * 2)
        Mesh _trailMesh;
        List<Vector3> _verts;
        List<Vector3> _normals;
        List<Vector2> _uvs;
        Vector3 _headTangent = Vector3.forward;

        // ------------------------------------------------------------------ lifecycle
        protected override void OnSetup()
        {
            // background
            var domeMat = NewDomeMaterial("Kaleido/DomeDark");
            CreateDome(domeMat);

            var root = CreateObjectRoot();

            // materials
            var trailMat = NewObjectMaterial("Kaleido/GlowLine");
            trailMat.SetFloat(WidthId, TrailWidth);
            trailMat.SetColor(ColorId, TrailColor);
            trailMat.SetFloat(IntensityId, TrailIntensity);

            var ghostMat = NewObjectMaterial("Kaleido/GlowLine");
            ghostMat.SetFloat(WidthId, GhostWidth);
            ghostMat.SetColor(ColorId, GhostColor);
            ghostMat.SetFloat(IntensityId, GhostIntensity);

            // ring buffer holding the last 3.0 evolution seconds of trail points, one per sub-step
            _ringCapacity = TrailPoints;
            _ring = new Vector3[_ringCapacity];
            _head = -1;
            _count = 0;
            _accum = 0f;

            // pre-run so the trail begins on the attractor. The ghost starts where the trail's oldest
            // point will be, so the visible tail lies on the ghost from the very first frame.
            _x = 1.0; _y = 1.0; _z = 1.0;
            int stepsBeforeRing = Mathf.Max(0, PreRunSteps - _ringCapacity);
            for (int i = 0; i < stepsBeforeRing; i++) Rk4(ref _x, ref _y, ref _z, SubStep);
            double gx = _x, gy = _y, gz = _z;
            for (int i = 0; i < _ringCapacity; i++)
            {
                Rk4(ref _x, ref _y, ref _z, SubStep);
                Push(ToLocal(_x, _y, _z));
            }

            // ghost ribbon (static)
            var ghostMesh = Own(BuildGhostMesh(gx, gy, gz, GhostPoints));
            CreateMeshObject("Ghost", root, ghostMesh, ghostMat);

            // trail ribbon (dynamic)
            int pairs = _ringCapacity + 1;                     // interpolated head + every ring point
            int vertexCount = pairs * 2;
            _verts = new List<Vector3>(vertexCount);
            _normals = new List<Vector3>(vertexCount);
            _uvs = new List<Vector2>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                _verts.Add(Vector3.zero);
                _normals.Add(Vector3.forward);
                _uvs.Add(Vector2.zero);
            }
            var tris = new List<int>((pairs - 1) * 6);
            for (int i = 0; i < pairs - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
            _trailMesh = Own(new Mesh { name = "LorenzTrail" });
            _trailMesh.MarkDynamic();
            FillTrailLists();
            _trailMesh.SetVertices(_verts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _trailMesh.SetNormals(_normals, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _trailMesh.SetUVs(0, _uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _trailMesh.SetTriangles(tris, 0, false);
            _trailMesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            CreateMeshObject("Trail", root, _trailMesh, trailMat);
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (dt <= 0f || _trailMesh == null) return;   // paused: integration and mesh freeze

            _accum += dt * SimSpeed;
            int steps = 0;
            while (_accum >= SubStep && steps < MaxSubStepsPerTick)
            {
                Rk4(ref _x, ref _y, ref _z, SubStep);
                Push(ToLocal(_x, _y, _z));
                _accum -= SubStep;
                steps++;
            }
            if (steps >= MaxSubStepsPerTick) _accum = 0f;   // drop the remainder after a huge hitch

            FillTrailLists();
            int n = _verts.Count;
            _trailMesh.SetVertices(_verts, 0, n, MeshUpdateFlags.DontRecalculateBounds);
            _trailMesh.SetNormals(_normals, 0, n, MeshUpdateFlags.DontRecalculateBounds);
            _trailMesh.SetUVs(0, _uvs, 0, n, MeshUpdateFlags.DontRecalculateBounds);
        }

        protected override void OnTeardown()
        {
            _trailMesh = null;   // owned objects are destroyed by the base class
            _ring = null;
            _verts = null;
            _normals = null;
            _uvs = null;
        }

        // ------------------------------------------------------------------ trail ribbon
        /// <summary>
        /// Writes the trail ribbon into the reused lists. Pair 0 is the interpolated head (age 0),
        /// pair k + 1 is the k-th newest ring point with age (k + frac) / capacity where
        /// frac = accumulator / sub-step. When a sub-step commits, the head becomes ring point 0 at
        /// frac 0 and the oldest point (age -> 1, life 0) drops out, so nothing pops.
        /// </summary>
        void FillTrailLists()
        {
            int cap = _ringCapacity;
            float frac = Mathf.Clamp01(_accum / SubStep);
            float invCap = 1f / cap;

            // head: uncommitted partial step of the remainder; tangent = Lorenz velocity direction
            double hx = _x, hy = _y, hz = _z;
            if (_accum > 0f) Rk4(ref hx, ref hy, ref hz, _accum);
            Vector3 head = ToLocal(hx, hy, hz);
            Deriv(hx, hy, hz, out double dx, out double dy, out double dz);
            var vel = new Vector3((float)dx, (float)dz, (float)dy);   // same axis mapping as ToLocal
            if (vel.sqrMagnitude > 1e-12f) _headTangent = vel.normalized;
            Vector3 tangent = _headTangent;
            WritePair(0, head, tangent, 0f);

            // ring points, newest first; tangent = direction of motion (newer - older), never flips sign
            Vector3 newer = head;
            for (int k = 0; k < cap; k++)
            {
                int pair = k + 1;
                if (k < _count)
                {
                    Vector3 p = RingAt(k);
                    Vector3 older = k + 1 < _count ? RingAt(k + 1) : p;
                    Vector3 t = newer - older;
                    if (t.sqrMagnitude > 1e-12f) tangent = t.normalized;
                    WritePair(pair, p, tangent, (k + frac) * invCap);
                    newer = p;
                }
                else
                {
                    // ring not yet full (cannot happen after the pre-run): collapse onto the last point, invisible
                    WritePair(pair, newer, tangent, 1f);
                }
            }
        }

        void WritePair(int pair, Vector3 p, Vector3 tangent, float age)
        {
            int i = pair * 2;
            _verts[i] = p;
            _verts[i + 1] = p;
            _normals[i] = tangent;
            _normals[i + 1] = tangent;
            _uvs[i] = new Vector2(-1f, age);
            _uvs[i + 1] = new Vector2(1f, age);
        }

        // ------------------------------------------------------------------ ghost ribbon
        static Mesh BuildGhostMesh(double x, double y, double z, int points)
        {
            int vertexCount = points * 2;
            var verts = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var uvs = new List<Vector2>(vertexCount);
            var tris = new List<int>((points - 1) * 6);

            Vector3 tangent = Vector3.forward;
            Vector3 p = ToLocal(x, y, z);
            for (int i = 0; i < points; i++)
            {
                Rk4(ref x, ref y, ref z, SubStep);
                Vector3 next = ToLocal(x, y, z);
                Vector3 t = next - p;
                if (t.sqrMagnitude > 1e-12f) tangent = t.normalized;
                verts.Add(p); verts.Add(p);
                normals.Add(tangent); normals.Add(tangent);
                uvs.Add(new Vector2(-1f, 0f)); uvs.Add(new Vector2(1f, 0f));   // age 0: no fade, full width
                p = next;
            }
            for (int i = 0; i < points - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            var mesh = new Mesh { name = "LorenzGhost", indexFormat = IndexFormat.UInt32 };   // 28 800 vertices
            mesh.SetVertices(verts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetNormals(normals, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetUVs(0, uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            mesh.SetTriangles(tris, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            return mesh;
        }

        // ------------------------------------------------------------------ ring buffer
        void Push(Vector3 p)
        {
            _head = (_head + 1) % _ringCapacity;
            _ring[_head] = p;
            if (_count < _ringCapacity) _count++;
        }

        /// <summary>k = 0 is the newest point.</summary>
        Vector3 RingAt(int k)
        {
            int i = _head - k;
            if (i < 0) i += _ringCapacity;
            return _ring[i];
        }

        // ------------------------------------------------------------------ Lorenz maths
        /// <summary>Lorenz (x, y, z) -> local metres (x, z - 27, y) * s.</summary>
        static Vector3 ToLocal(double x, double y, double z)
        {
            return new Vector3((float)(x * Scale), (float)((z - CentreZ) * Scale), (float)(y * Scale));
        }

        static void Deriv(double x, double y, double z, out double dx, out double dy, out double dz)
        {
            dx = Sigma * (y - x);
            dy = x * (Rho - z) - y;
            dz = x * y - Beta * z;
        }

        /// <summary>Classic fourth-order Runge-Kutta step of length h (simulation seconds).</summary>
        static void Rk4(ref double x, ref double y, ref double z, double h)
        {
            Deriv(x, y, z, out double k1x, out double k1y, out double k1z);
            double hh = 0.5 * h;
            Deriv(x + hh * k1x, y + hh * k1y, z + hh * k1z, out double k2x, out double k2y, out double k2z);
            Deriv(x + hh * k2x, y + hh * k2y, z + hh * k2z, out double k3x, out double k3y, out double k3z);
            Deriv(x + h * k3x, y + h * k3y, z + h * k3z, out double k4x, out double k4y, out double k4z);
            double w = h / 6.0;
            x += w * (k1x + 2.0 * k2x + 2.0 * k3x + k4x);
            y += w * (k1y + 2.0 * k2y + 2.0 * k3y + k4y);
            z += w * (k1z + 2.0 * k2z + 2.0 * k3z + k4z);
        }
    }
}
