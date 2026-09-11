using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 9 "fractal tree" (3D object).
    ///
    /// A recursive L-system tree (depth 8, 2-3 children per branch, 20-30 deg branch angle, random roll,
    /// length ratio 0.72, radius ratio 0.68, slight upward bias) is generated on the CPU with a seeded
    /// System.Random and baked into one mesh for the Kaleido/Tree shader. Every branch segment is three
    /// crossed soft-edged quads whose vertices carry the segment base point (TEXCOORD1) and a
    /// (birth, duration) growth window (TEXCOORD2), so the shader grows the whole tree from the trunk to
    /// the leaf tips as a continuous function of a single _Growth value. Leaves are small diamonds at the
    /// tips of depth >= 6 branches.
    ///
    /// Cycle (PRD): grow 30 s (_Growth 0 -> 1, eased), hold 10 s, fade out 2 s, regenerate with a new
    /// seed, fade back in over 2 s while the new tree starts regrowing. The cycle phase is accumulated
    /// only from the dt handed to OnTick, so pausing (dt == 0) freezes growth, fades and regeneration.
    /// The tree material's _Fade is objectFade (scene crossfade, from OnFade) * cycleFade.
    /// No Update/coroutines/Time.* - works identically in play mode and from the editor capture tool.
    /// </summary>
    public class FractalTreeScene : KaleidoSceneBase
    {
        // ------------------------------------------------------------------ cycle timing (PRD)
        const float GrowSeconds = 30f;
        const float HoldSeconds = 10f;
        const float FadeOutSeconds = 2f;
        const float FadeInSeconds = 2f;
        const float CycleSeconds = GrowSeconds + HoldSeconds + FadeOutSeconds;   // 42 s per tree

        // ------------------------------------------------------------------ tree shape
        const int BranchLevels = 8;                       // depth 0 (trunk) .. 7
        const int LeafMinLevel = 5;                       // leaves at the tips of depth >= 6 branches
        const int GrowthSlots = BranchLevels + 1;         // 8 branch levels + 1 leaf slot in normalised growth time
        const float TrunkLength = 0.35f;                  // metres, before the final fit-to-size scale
        const float TrunkRadius = 0.03f;
        const float LengthRatio = 0.72f;
        const float RadiusRatio = 0.68f;
        const float MinRadius = 0.0022f;                  // keeps the finest twigs a few pixels wide at 2 m
        const float MinBranchAngleDeg = 20f;
        const float MaxBranchAngleDeg = 30f;
        const float UpwardBias = 0.16f;                   // phototropism: children lean slightly towards +Y
        const float ThreeChildProbability = 0.72f;   // denser canopy
        const float TreeHeight = KaleidoConfig.ObjectDiameter;   // 1.2 m tall ...
        const float TreeMaxWidth = 1.1f;                          // ... and at most ~1.1 m wide (fit shrinks wide seeds)
        const float BaseOffsetY = -0.5f * KaleidoConfig.ObjectDiameter; // base 0.6 m below the anchor -> centred at eye height
        const int MaxSegments = 3400;                     // worst case 3^8 chain = 3280 segments
        const int MaxVertices = 58000;                    // hard budget, well under 60k
        const int TypicalVertices = 24000;                // initial buffer capacity (typical tree ~18k, max seen ~29k)
        const int FixedSeed = 90210;                      // deterministic sequence of tree seeds

        // Matches the Properties block of Kaleido/Tree and Kaleido/DomeDark exactly. _Fade comes from ShaderIds.
        static readonly int GrowthId = Shader.PropertyToID("_Growth");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");

        // Vertex colours are uploaded raw, so the sRGB design values are converted to linear once.
        static readonly Color TrunkColor = new Color(0.40f, 0.25f, 0.12f, 1f).linear;
        static readonly Color TipColor = new Color(0.45f, 0.85f, 0.40f, 1f).linear;
        static readonly Color LeafColor = new Color(0.62f, 0.95f, 0.50f, 1f).linear;

        struct Branch
        {
            public Vector3 Start;     // segment base (shared by all vertices of the segment as TEXCOORD1)
            public Vector3 End;       // segment tip
            public Vector3 Dir;       // normalised Start -> End
            public float Length;
            public float R0;          // radius at the base
            public float R1;          // radius at the tip (= children's base radius)
            public int Level;         // 0 = trunk
            public float Birth;       // normalised growth time at which the segment starts growing
            public float Duration;    // normalised growth time it takes to reach full length
            public float Shade;       // per-branch brightness jitter
        }

        Material _domeMat;
        Material _treeMat;
        Mesh _mesh;
        Transform _root;
        Transform _treeTransform;
        System.Random _seedRng;

        float _phase;                 // seconds into the current cycle, accumulated from dt only
        float _growth;                // eased _Growth value
        float _cycleFade;             // 0..1 in-scene fade of the current tree
        float _objectFade;            // last objectFade handed to OnFade (scene crossfade)

        // Preallocated, reused for every regeneration (no per-frame allocation in OnTick at all).
        readonly List<Branch> _branches = new List<Branch>(2048);
        readonly List<Vector3> _verts = new List<Vector3>(TypicalVertices);
        readonly List<Vector3> _bases = new List<Vector3>(TypicalVertices);
        readonly List<Vector2> _uv0 = new List<Vector2>(TypicalVertices);
        readonly List<Vector2> _uv2 = new List<Vector2>(TypicalVertices);
        readonly List<Color> _colors = new List<Color>(TypicalVertices);
        readonly List<int> _tris = new List<int>(TypicalVertices * 3 / 2);

        // ------------------------------------------------------------------ lifecycle
        protected override void OnSetup()
        {
            // Quiet dark backdrop (registered as a dome material: base class drives _Fade and the queue).
            _domeMat = NewDomeMaterial("Kaleido/DomeDark");
            _domeMat.SetColor(TopColorId, new Color(0.04f, 0.06f, 0.12f, 1f));
            _domeMat.SetColor(BottomColorId, new Color(0.01f, 0.012f, 0.02f, 1f));
            CreateDome(_domeMat);

            // Tree material (registered as an object material; _Fade is re-applied by this class with cycleFade).
            _treeMat = NewObjectMaterial("Kaleido/Tree");
            _treeMat.SetColor(ColorId, Color.white);   // vertex colours carry the brown -> green gradient
            _treeMat.SetFloat(GrowthId, 0f);
            _treeMat.SetFloat(ShaderIds.Fade, 0f);

            _mesh = Own(new Mesh { name = "FractalTree", indexFormat = IndexFormat.UInt32 });
            _mesh.MarkDynamic();

            _root = CreateObjectRoot();                 // spins 6 deg/s, frozen while paused (base class)
            var renderer = CreateMeshObject("Tree", _root, _mesh, _treeMat);
            _treeTransform = renderer.transform;
            _treeTransform.localPosition = new Vector3(0f, BaseOffsetY, 0f);

            _seedRng = new System.Random(FixedSeed);
            _phase = 0f;
            _growth = 0f;
            _cycleFade = 0f;
            _objectFade = 0f;
            Regenerate();
            ApplyGrowthAndFade();
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (_treeMat == null || _mesh == null) return;

            // Keep the object root on the anchor in case it was placed after this scene was spawned
            // (e.g. app launched directly on this playlist index). Rotation is owned by the base class.
            if (_root != null && Ctx != null && Ctx.ObjectAnchor != null) _root.position = Ctx.ObjectAnchor.position;

            if (dt > 0f)
            {
                _phase += dt;
                while (_phase >= CycleSeconds)
                {
                    // Wrap happens while cycleFade == 0 on both sides, so the new tree never pops in.
                    _phase -= CycleSeconds;
                    Regenerate();
                }
            }

            _growth = EaseGrowth(Mathf.Clamp01(_phase / GrowSeconds));
            _cycleFade = CycleFadeAt(_phase);
            ApplyGrowthAndFade();
        }

        protected override void OnFade(float domeFade, float objectFade)
        {
            // The base class has just written objectFade into _Fade; combine it with the in-scene cycle fade.
            _objectFade = objectFade;
            ApplyTreeFade();
        }

        protected override void OnTeardown()
        {
            // Materials, mesh and GameObjects are owned by the base class; just drop references and buffers.
            _domeMat = null;
            _treeMat = null;
            _mesh = null;
            _root = null;
            _treeTransform = null;
            _branches.Clear();
            ClearGeometryBuffers();
        }

        // ------------------------------------------------------------------ cycle
        /// <summary>Linear/smoothstep blend: continuous, never fully stalls at either end of the 30 s growth.</summary>
        static float EaseGrowth(float t)
        {
            float s = t * t * (3f - 2f * t);
            return Mathf.Lerp(t, s, 0.5f);
        }

        /// <summary>0 at both ends of the cycle (so regeneration at the wrap is invisible), 1 during grow + hold.</summary>
        static float CycleFadeAt(float phase)
        {
            if (phase < FadeInSeconds) return Mathf.SmoothStep(0f, 1f, phase / FadeInSeconds);
            float fadeOutStart = GrowSeconds + HoldSeconds;
            if (phase < fadeOutStart) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (phase - fadeOutStart) / FadeOutSeconds);
        }

        void ApplyGrowthAndFade()
        {
            if (_treeMat == null) return;
            _treeMat.SetFloat(GrowthId, _growth);
            ApplyTreeFade();
        }

        void ApplyTreeFade()
        {
            if (_treeMat == null) return;
            _treeMat.SetFloat(ShaderIds.Fade, Mathf.Clamp01(_objectFade * _cycleFade));
        }

        // ------------------------------------------------------------------ generation
        void Regenerate()
        {
            int seed = _seedRng.Next();
            var rng = new System.Random(seed);
            GenerateBranches(rng);
            FitToTargetSize();
            EmitGeometry(rng);
            UploadMesh();
        }

        static float Range(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        /// <summary>Two unit vectors perpendicular to d (and to each other).</summary>
        static void Perpendicular(Vector3 d, out Vector3 u, out Vector3 v)
        {
            Vector3 helper = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
            u = Vector3.Cross(d, helper);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.Cross(d, Vector3.forward);
            u.Normalize();
            v = Vector3.Cross(d, u).normalized;
        }

        /// <summary>Breadth-first L-system expansion into _branches (unscaled, trunk base at the origin).</summary>
        void GenerateBranches(System.Random rng)
        {
            _branches.Clear();
            float slot = 1f / GrowthSlots;

            // Trunk: almost vertical with a tiny random lean.
            float leanA = Range(rng, 0f, Mathf.PI * 2f);
            Vector3 trunkDir = (Vector3.up + new Vector3(Mathf.Cos(leanA), 0f, Mathf.Sin(leanA)) * Range(rng, 0.0f, 0.06f)).normalized;
            float trunkLen = TrunkLength * Range(rng, 0.95f, 1.05f);
            _branches.Add(new Branch
            {
                Start = Vector3.zero,
                End = trunkDir * trunkLen,
                Dir = trunkDir,
                Length = trunkLen,
                R0 = TrunkRadius,
                R1 = TrunkRadius * RadiusRatio,
                Level = 0,
                Birth = 0f,
                Duration = slot * 0.9f,
                Shade = Range(rng, 0.9f, 1.05f),
            });

            int head = 0;
            while (head < _branches.Count)
            {
                Branch parent = _branches[head++];
                int level = parent.Level + 1;
                if (level >= BranchLevels) continue;
                if (_branches.Count >= MaxSegments) break;

                int childCount = rng.NextDouble() < ThreeChildProbability ? 3 : 2;
                float rollBase = Range(rng, 0f, Mathf.PI * 2f);
                Perpendicular(parent.Dir, out Vector3 u, out Vector3 v);

                for (int c = 0; c < childCount && _branches.Count < MaxSegments; c++)
                {
                    float roll = rollBase + c * (Mathf.PI * 2f / childCount) + Range(rng, -0.5f, 0.5f);
                    float angle = Range(rng, MinBranchAngleDeg, MaxBranchAngleDeg) * Mathf.Deg2Rad;
                    Vector3 side = u * Mathf.Cos(roll) + v * Mathf.Sin(roll);
                    Vector3 dir = parent.Dir * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                    dir = (dir + Vector3.up * UpwardBias).normalized;   // slight upward bias

                    float length = parent.Length * LengthRatio * Range(rng, 0.85f, 1.1f);
                    float r0 = parent.R1;
                    float r1 = Mathf.Max(MinRadius, r0 * RadiusRatio);
                    // Children start growing only once the parent has finished (parent ends at (level) * slot).
                    float birth = level * slot + Range(rng, 0f, 0.1f) * slot;
                    float duration = slot * 0.9f;

                    _branches.Add(new Branch
                    {
                        Start = parent.End,
                        End = parent.End + dir * length,
                        Dir = dir,
                        Length = length,
                        R0 = r0,
                        R1 = r1,
                        Level = level,
                        Birth = birth,
                        Duration = duration,
                        Shade = Range(rng, 0.85f, 1.1f),
                    });
                }
            }
        }

        /// <summary>Uniformly scale the tree so it is exactly TreeHeight tall and at most TreeMaxWidth wide.</summary>
        void FitToTargetSize()
        {
            float maxY = 0f;
            float maxR = 0f;
            for (int i = 0; i < _branches.Count; i++)
            {
                Vector3 e = _branches[i].End;
                if (e.y > maxY) maxY = e.y;
                float r = Mathf.Sqrt(e.x * e.x + e.z * e.z);
                if (r > maxR) maxR = r;
            }
            float scale = Mathf.Min(TreeHeight / Mathf.Max(maxY, 0.05f), TreeMaxWidth / Mathf.Max(2f * maxR, 0.05f));
            for (int i = 0; i < _branches.Count; i++)
            {
                Branch b = _branches[i];
                b.Start *= scale;
                b.End *= scale;
                b.Length *= scale;
                b.R0 = Mathf.Max(MinRadius, b.R0 * scale);
                b.R1 = Mathf.Max(MinRadius, b.R1 * scale);
                _branches[i] = b;
            }
        }

        void ClearGeometryBuffers()
        {
            _verts.Clear();
            _bases.Clear();
            _uv0.Clear();
            _uv2.Clear();
            _colors.Clear();
            _tris.Clear();
        }

        /// <summary>Branches (3 crossed quads each, trunk first so tips composite over it) then leaves.</summary>
        void EmitGeometry(System.Random rng)
        {
            ClearGeometryBuffers();
            float levelDenom = Mathf.Max(1, BranchLevels - 1);

            for (int i = 0; i < _branches.Count; i++)
            {
                if (_verts.Count + 12 > MaxVertices) break;
                Branch b = _branches[i];
                Perpendicular(b.Dir, out Vector3 u, out Vector3 v);
                float t = b.Level / levelDenom;
                Color col = Color.Lerp(TrunkColor, TipColor, t) * b.Shade;
                col.a = 1f;
                var window = new Vector2(b.Birth, b.Duration);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * (Mathf.PI / 3f);            // 0, 60, 120 degrees around the segment axis
                    Vector3 side = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                    AddQuad(b.Start - side * b.R0, b.Start + side * b.R0,
                            b.End + side * b.R1, b.End - side * b.R1,
                            b.Start, window, col);
                }
            }

            float slot = 1f / GrowthSlots;
            for (int i = 0; i < _branches.Count; i++)
            {
                Branch b = _branches[i];
                if (b.Level < LeafMinLevel) continue;
                int leafCount = b.Level >= BranchLevels - 1 ? 3 : 2;
                Perpendicular(b.Dir, out Vector3 u, out Vector3 v);
                for (int n = 0; n < leafCount; n++)
                {
                    if (_verts.Count + 4 > MaxVertices) return;
                    float roll = Range(rng, 0f, Mathf.PI * 2f) + n * Mathf.PI;
                    float tilt = Range(rng, 25f, 55f) * Mathf.Deg2Rad;
                    Vector3 side0 = u * Mathf.Cos(roll) + v * Mathf.Sin(roll);
                    Vector3 leafDir = (b.Dir * Mathf.Cos(tilt) + side0 * Mathf.Sin(tilt)).normalized;
                    Perpendicular(leafDir, out Vector3 lu, out Vector3 lv);
                    float spin = Range(rng, 0f, Mathf.PI * 2f);
                    Vector3 leafSide = lu * Mathf.Cos(spin) + lv * Mathf.Sin(spin);
                    float length = Range(rng, 0.04f, 0.062f);
                    float width = Range(rng, 0.024f, 0.036f);
                    Color col = LeafColor * Range(rng, 0.85f, 1.1f);
                    col.a = 1f;
                    // Leaves sprout once their branch has finished growing (branch ends at (Level + 1) * slot).
                    float birth = (b.Level + 1) * slot + Range(rng, 0f, 0.1f) * slot;
                    var window = new Vector2(birth, slot * 0.9f);
                    AddLeaf(b.End, leafDir, leafSide, length, width, window, col);
                }
            }
        }

        /// <summary>Quad a,b (base edge, uv.y = 0) -> c,d (tip edge, uv.y = 1); uv.x runs 0..1 across.</summary>
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 basePos, Vector2 window, Color col)
        {
            int i0 = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            _uv0.Add(new Vector2(0f, 0f)); _uv0.Add(new Vector2(1f, 0f)); _uv0.Add(new Vector2(1f, 1f)); _uv0.Add(new Vector2(0f, 1f));
            for (int k = 0; k < 4; k++)
            {
                _bases.Add(basePos);
                _uv2.Add(window);
                _colors.Add(col);
            }
            _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2);
            _tris.Add(i0); _tris.Add(i0 + 2); _tris.Add(i0 + 3);
        }

        /// <summary>Diamond leaf: base at the branch tip, widest at 45 % of its length; uv.x = 0.5 on the centre line.</summary>
        void AddLeaf(Vector3 basePos, Vector3 dir, Vector3 side, float length, float width, Vector2 window, Color col)
        {
            int i0 = _verts.Count;
            Vector3 mid = basePos + dir * (length * 0.45f);
            _verts.Add(basePos);
            _verts.Add(mid - side * (width * 0.5f));
            _verts.Add(basePos + dir * length);
            _verts.Add(mid + side * (width * 0.5f));
            _uv0.Add(new Vector2(0.5f, 0f));
            _uv0.Add(new Vector2(0f, 0.45f));
            _uv0.Add(new Vector2(0.5f, 1f));
            _uv0.Add(new Vector2(1f, 0.45f));
            for (int k = 0; k < 4; k++)
            {
                _bases.Add(basePos);
                _uv2.Add(window);
                _colors.Add(col);
            }
            _tris.Add(i0); _tris.Add(i0 + 1); _tris.Add(i0 + 2);
            _tris.Add(i0); _tris.Add(i0 + 2); _tris.Add(i0 + 3);
        }

        void UploadMesh()
        {
            if (_mesh == null) return;
            _mesh.Clear();
            _mesh.indexFormat = IndexFormat.UInt32;      // Clear() may reset the index format; re-assert it
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uv0);                       // (across, along)
            _mesh.SetUVs(1, _bases);                     // TEXCOORD1: segment base point (Vector3)
            _mesh.SetUVs(2, _uv2);                       // TEXCOORD2: (birth, duration)
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0, false);
            // Every animated vertex lies between its base and its final position, so a fixed generous
            // box around the fitted tree is always valid (leaves add at most ~5 cm beyond the fit).
            _mesh.bounds = new Bounds(new Vector3(0f, TreeHeight * 0.5f, 0f),
                                      new Vector3(TreeMaxWidth * 1.3f, TreeHeight * 1.2f, TreeMaxWidth * 1.3f));
        }
    }
}
