using System.Collections.Generic;
using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist items 1-4: traditional kaleidoscopes (PRD section 4).
    /// A 2D physics chamber ("cell") full of semi-transparent fragments lives far away from the
    /// user on its own layer; a helper orthographic camera renders it into a texture every frame;
    /// the dome shader (Kaleido/DomeMirror) folds that texture with exact mirror reflections:
    ///   Sector   - two mirrors meeting at 60 deg (6 sectors) or 45 deg (8 sectors); the cell is a disc
    ///   Triangle - three mirrors forming an equilateral triangle; the plane is tiled by reflections
    /// Gravity for the cell is driven by CellPhysics (slow virtual tube rotation + head roll).
    /// </summary>
    public class PhysicalKaleidoscopeScene : KaleidoSceneBase
    {
        public enum MirrorMode { Sector, Triangle }

        public class Config
        {
            public MirrorMode MirrorMode = MirrorMode.Triangle;
            public float SectorAngleDeg = 60f;
            public bool DarkField = false;
            public int FragmentCount = 200;
            public int Style = 0;              // 0 glass, 1 organic petal / feather
            public float TumbleScale = 1f;     // gravity scale (item 3 tumbles slower)
            public Color[] Palette;
            public int Seed = 1;
            public float RodFraction = 0.12f;  // share of long thin rods
            public float MinSize = 0.06f;
            public float MaxSize = 0.15f;
            public float OpacityMin = 0.55f;
            public float OpacityMax = 0.85f;
            public float RoundShapes = 0.2f;   // share of near-circular beads
        }

        public static class Presets
        {
            public static Config ThreeMirrorGlass() => new Config
            {
                MirrorMode = MirrorMode.Triangle, FragmentCount = 200, Style = 0, Seed = 11,
                Palette = new[] { new Color(0.90f, 0.15f, 0.20f), new Color(0.20f, 0.70f, 0.30f), new Color(0.25f, 0.50f, 0.90f),
                                  new Color(0.95f, 0.85f, 0.20f), new Color(0.55f, 0.35f, 0.85f), new Color(0.95f, 0.55f, 0.20f), new Color(0.20f, 0.75f, 0.70f) },
                RodFraction = 0.15f, MinSize = 0.045f, MaxSize = 0.12f, OpacityMin = 0.7f, OpacityMax = 0.95f,
            };

            public static Config TwoMirrorDarkBeads() => new Config
            {
                MirrorMode = MirrorMode.Sector, SectorAngleDeg = 45f, DarkField = true, FragmentCount = 140, Style = 0, Seed = 23,
                Palette = new[] { new Color(0.30f, 0.90f, 1.00f), new Color(0.35f, 0.55f, 1.00f), new Color(0.40f, 0.95f, 0.50f),
                                  new Color(1.00f, 0.60f, 0.25f), new Color(1.00f, 0.45f, 0.75f), new Color(1.00f, 0.90f, 0.40f) },
                RodFraction = 0.05f, MinSize = 0.03f, MaxSize = 0.075f, OpacityMin = 0.75f, OpacityMax = 1.0f, RoundShapes = 0.6f,
            };

            public static Config ThreeMirrorPetals() => new Config
            {
                MirrorMode = MirrorMode.Triangle, FragmentCount = 150, Style = 1, TumbleScale = 0.6f, Seed = 37,
                Palette = new[] { new Color(0.90f, 0.20f, 0.25f), new Color(0.20f, 0.65f, 0.35f), new Color(0.50f, 0.30f, 0.80f),
                                  new Color(0.20f, 0.45f, 0.90f), new Color(0.10f, 0.60f, 0.55f), new Color(0.70f, 0.80f, 0.15f), new Color(0.95f, 0.55f, 0.15f) },
                RodFraction = 0.05f, MinSize = 0.06f, MaxSize = 0.16f, OpacityMin = 0.8f, OpacityMax = 0.98f, RoundShapes = 0.1f,
            };

            public static Config TwoMirrorHexGlass() => new Config
            {
                MirrorMode = MirrorMode.Sector, SectorAngleDeg = 60f, FragmentCount = 160, Style = 0, Seed = 41,
                Palette = new[] { new Color(0.55f, 0.85f, 0.30f), new Color(0.95f, 0.60f, 0.25f), new Color(0.60f, 0.50f, 0.90f),
                                  new Color(0.95f, 0.45f, 0.70f), new Color(0.30f, 0.80f, 0.85f), new Color(0.60f, 0.40f, 0.20f) },
                RodFraction = 0.25f, MinSize = 0.035f, MaxSize = 0.085f, OpacityMin = 0.7f, OpacityMax = 0.92f, RoundShapes = 0.2f,
            };
        }

        // Equilateral cell triangle: MUST match the defaults in DomeMirror.shader.
        static readonly Vector2 TriA = new Vector2(-0.85f, -0.55f);
        static readonly Vector2 TriB = new Vector2(0.85f, -0.55f);
        static readonly Vector2 TriC = new Vector2(0f, 0.922f);
        const float DiscRadius = 1f;

        struct Fragment
        {
            public Rigidbody2D Body;
            public Vector2[] Points;   // local polygon
            public float Radius;       // for uv normalisation
            public Color Color;        // rgb tint, a = opacity
            public float Seed;
            public int Style;
        }

        static readonly int ModeId = Shader.PropertyToID("_Mode");
        static readonly int SectorAngleId = Shader.PropertyToID("_SectorAngle");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");
        static readonly int TriAId = Shader.PropertyToID("_TriA");
        static readonly int TriBId = Shader.PropertyToID("_TriB");
        static readonly int TriCId = Shader.PropertyToID("_TriC");
        static readonly int DarkFieldId = Shader.PropertyToID("_DarkField");
        static readonly int DiscRadiusId = Shader.PropertyToID("_DiscRadius");

        Config _cfg;
        Vector3 _offset;
        GameObject _cellRoot;
        readonly List<Fragment> _fragments = new List<Fragment>();
        Mesh _mesh;
        Camera _cellCam;
        RenderTexture _rt;
        Material _cellMat;
        Material _domeMat;
        bool _renderedOnce;

        // reused mesh buffers
        readonly List<Vector3> _verts = new List<Vector3>(2048);
        readonly List<Color> _cols = new List<Color>(2048);
        readonly List<Vector2> _uv0 = new List<Vector2>(2048);
        readonly List<Vector2> _uv1 = new List<Vector2>(2048);
        readonly List<int> _tris = new List<int>(6144);

        protected override void OnSetup()
        {
            _cfg = Definition.Config as Config ?? Presets.ThreeMirrorGlass();
            CellPhysics.EnsureInitialised();
            _offset = Ctx.AllocateCellOffset();
            int layer = Ctx.CellLayer;

            _cellRoot = new GameObject("Cell");
            _cellRoot.layer = layer;
            _cellRoot.transform.SetParent(transform, false);
            _cellRoot.transform.position = _offset;
            BuildWalls();
            BuildFragments(layer);

            // combined mesh renderer (world-space vertices)
            _mesh = Own(new Mesh { name = "CellMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            _mesh.MarkDynamic();
            _cellMat = Own(KaleidoRenderUtil.NewMaterial(_cfg.DarkField ? "Kaleido/CellScreen" : "Kaleido/CellMultiply"));
            var mr = CreateMeshObject("CellMesh", transform, _mesh, _cellMat, layer);
            mr.transform.position = Vector3.zero;

            // helper camera + texture
            Color clear = _cfg.DarkField ? Color.black : Color.white;
            _cellCam = KaleidoRenderUtil.CreateHelperCamera("CellCamera", transform, 1 << layer, clear, true);
            _cellCam.transform.position = _offset + new Vector3(0f, 0f, -10f);
            _cellCam.transform.rotation = Quaternion.identity;
            _cellCam.orthographicSize = 1f;
            _cellCam.aspect = 1f;
            _cellCam.nearClipPlane = 0.1f;
            _cellCam.farClipPlane = 20f;
            int size = (int)KaleidoConfig.CellTextureSize;
            _rt = Own(KaleidoRenderUtil.CreateRT(size, size, true, "CellRT", RenderTextureFormat.ARGB32));

            // dome
            _domeMat = NewDomeMaterial("Kaleido/DomeMirror");
            _domeMat.SetTexture(ShaderIds.CellTex, _rt);
            _domeMat.SetFloat(ModeId, _cfg.MirrorMode == MirrorMode.Sector ? 0f : 1f);
            _domeMat.SetFloat(SectorAngleId, _cfg.SectorAngleDeg * Mathf.Deg2Rad);
            _domeMat.SetFloat(ScaleId, _cfg.MirrorMode == MirrorMode.Sector ? 1f / 1.36f : 2.2f);
            _domeMat.SetVector(TriAId, new Vector4(TriA.x, TriA.y, 0f, 0f));
            _domeMat.SetVector(TriBId, new Vector4(TriB.x, TriB.y, 0f, 0f));
            _domeMat.SetVector(TriCId, new Vector4(TriC.x, TriC.y, 0f, 0f));
            _domeMat.SetFloat(DarkFieldId, _cfg.DarkField ? 1f : 0f);
            _domeMat.SetFloat(DiscRadiusId, DiscRadius);
            CreateDome(_domeMat);

            RebuildMesh();
            KaleidoRenderUtil.RenderCameraTo(_cellCam, _rt);
            _renderedOnce = true;
        }

        protected override void OnTick(float dt, double evoTime)
        {
            if (_rt == null) return;
            bool lost = !_rt.IsCreated();
            if (lost) _rt.Create();
            if (dt > 0f || !_renderedOnce || lost)
            {
                RebuildMesh();
                KaleidoRenderUtil.RenderCameraTo(_cellCam, _rt);
                _renderedOnce = true;
            }
        }

        protected override void OnTeardown()
        {
            _fragments.Clear();
        }

        // ------------------------------------------------------------------ walls
        void BuildWalls()
        {
            var edge = _cellRoot.AddComponent<EdgeCollider2D>();
            var pts = new List<Vector2>();
            if (_cfg.MirrorMode == MirrorMode.Sector)
            {
                const int n = 96;
                for (int i = 0; i <= n; i++)
                {
                    float a = (float)i / n * Mathf.PI * 2f;
                    pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * DiscRadius);
                }
            }
            else
            {
                pts.Add(TriA); pts.Add(TriB); pts.Add(TriC); pts.Add(TriA);
            }
            edge.points = pts.ToArray();
            edge.edgeRadius = 0.005f;
        }

        // ------------------------------------------------------------------ fragments
        void BuildFragments(int layer)
        {
            var rng = new System.Random(_cfg.Seed);
            var physMat = Own(new PhysicsMaterial2D("Glass") { bounciness = 0.08f, friction = 0.35f });
            int count = Mathf.Clamp(_cfg.FragmentCount, 100, 250);
            for (int i = 0; i < count; i++)
            {
                float size = Mathf.Lerp(_cfg.MinSize, _cfg.MaxSize, (float)rng.NextDouble());
                int style = _cfg.Style;
                Vector2[] pts = MakeShape(rng, size, style, out float radius);

                var go = new GameObject("F" + i);
                go.layer = layer;
                go.transform.SetParent(_cellRoot.transform, false);
                go.transform.localPosition = RandomPointInCell(rng);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);

                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.gravityScale = _cfg.TumbleScale;
                rb.linearDamping = 1.6f;
                rb.angularDamping = 1.2f;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rb.interpolation = RigidbodyInterpolation2D.None;
                rb.sleepMode = RigidbodySleepMode2D.StartAwake;
                rb.useAutoMass = false;
                rb.mass = Mathf.Max(0.02f, PolygonArea(pts) * 20f);

                var col = go.AddComponent<PolygonCollider2D>();
                col.points = pts;
                col.sharedMaterial = physMat;

                var tint = _cfg.Palette[rng.Next(_cfg.Palette.Length)];
                float opacity = Mathf.Lerp(_cfg.OpacityMin, _cfg.OpacityMax, (float)rng.NextDouble());
                _fragments.Add(new Fragment
                {
                    Body = rb, Points = pts, Radius = radius,
                    Color = new Color(tint.r, tint.g, tint.b, opacity),
                    Seed = (float)rng.NextDouble(), Style = style,
                });
            }
        }

        Vector2 RandomPointInCell(System.Random rng)
        {
            if (_cfg.MirrorMode == MirrorMode.Sector)
            {
                float r = Mathf.Sqrt((float)rng.NextDouble()) * 0.82f;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            // uniform in triangle, shrunk toward the centroid so shapes start clear of the walls
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            Vector2 p = TriA + (TriB - TriA) * u + (TriC - TriA) * v;
            Vector2 c = (TriA + TriB + TriC) / 3f;
            return c + (p - c) * 0.8f;
        }

        /// <summary>Convex polygon in local space; radius = bounding radius for uv normalisation.</summary>
        Vector2[] MakeShape(System.Random rng, float size, int style, out float radius)
        {
            double roll = rng.NextDouble();
            int n; float aspect;
            if (style == 1)
            {
                // petals / leaves / feathers: elongated ellipse-ish polygons
                n = 10; aspect = Mathf.Lerp(0.28f, 0.55f, (float)rng.NextDouble());
            }
            else if (roll < _cfg.RodFraction)
            {
                n = 4; aspect = Mathf.Lerp(0.18f, 0.3f, (float)rng.NextDouble()); size *= 1.8f;
            }
            else if (roll < _cfg.RodFraction + _cfg.RoundShapes)
            {
                n = 10; aspect = Mathf.Lerp(0.8f, 1f, (float)rng.NextDouble());
            }
            else
            {
                int[] choices = { 3, 4, 5, 6, 6, 8 };
                n = choices[rng.Next(choices.Length)];
                aspect = Mathf.Lerp(0.6f, 1f, (float)rng.NextDouble());
            }
            var pts = new Vector2[n];
            float phase = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = phase + (float)i / n * Mathf.PI * 2f;
                pts[i] = new Vector2(Mathf.Cos(a) * size, Mathf.Sin(a) * size * aspect);
            }
            if (style == 1)
            {
                // taper one end (still convex: scale x of the vertices on one side)
                for (int i = 0; i < n; i++) if (pts[i].x > 0f) pts[i].y *= 0.6f;
            }
            radius = size;
            return pts;
        }

        static float PolygonArea(Vector2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++)
            {
                var q = p[(i + 1) % p.Length];
                a += p[i].x * q.y - q.x * p[i].y;
            }
            return Mathf.Abs(a) * 0.5f;
        }

        // ------------------------------------------------------------------ mesh
        void RebuildMesh()
        {
            _verts.Clear(); _cols.Clear(); _uv0.Clear(); _uv1.Clear(); _tris.Clear();
            float z = _offset.z;
            for (int f = 0; f < _fragments.Count; f++)
            {
                var fr = _fragments[f];
                if (fr.Body == null) continue;
                Vector2 c = fr.Body.position;
                float rot = fr.Body.rotation * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                int baseIndex = _verts.Count;
                var pts = fr.Points;
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = pts[i];
                    float x = c.x + p.x * cs - p.y * sn;
                    float y = c.y + p.x * sn + p.y * cs;
                    _verts.Add(new Vector3(x, y, z));
                    _cols.Add(fr.Color);
                    _uv0.Add(p / fr.Radius);
                    _uv1.Add(new Vector2(fr.Style, fr.Seed));
                }
                for (int i = 1; i < pts.Length - 1; i++)
                {
                    _tris.Add(baseIndex);
                    _tris.Add(baseIndex + i);
                    _tris.Add(baseIndex + i + 1);
                }
            }
            _mesh.Clear(false);
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_cols);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(_offset, new Vector3(3f, 3f, 1f));
        }
    }
}
