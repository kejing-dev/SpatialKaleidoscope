using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist items 1-4: traditional kaleidoscopes (PRD section 4).
    /// A 2D physics chamber ("cell") full of semi-transparent fragments lives far away from the
    /// user on its own layer. Every frame the fragments are drawn as SDF quads (analytic
    /// anti-aliasing, no geometry jaggies) into a texture through a plain command buffer (no helper
    /// camera, no URP pass overhead), and the dome shader (Kaleido/DomeMirror) folds that texture
    /// with exact mirror reflections:
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
            public int FragmentCount = 110;
            public int Style = 0;              // 0 glass, 1 organic petal / feather
            public float TumbleScale = 1f;     // gravity scale (item 3 tumbles slower)
            public Color[] Palette;
            public int Seed = 1;
            public float RodFraction = 0.12f;  // share of long thin rods
            public float MinSize = 0.07f;      // fragment half-size in cell units (cell radius = 1)
            public float MaxSize = 0.17f;
            public float OpacityMin = 0.6f;
            public float OpacityMax = 0.9f;
            public float RoundShapes = 0.2f;   // share of circular beads
            public float PlaneScale = 1.6f;    // triangle mode: plane units per radian (bigger = smaller cells)
        }

        public static class Presets
        {
            public static Config ThreeMirrorGlass() => new Config
            {
                MirrorMode = MirrorMode.Triangle, FragmentCount = 160, Style = 0, Seed = 11, PlaneScale = 2.6f,
                Palette = new[] { new Color(0.95f, 0.12f, 0.18f), new Color(0.15f, 0.75f, 0.30f), new Color(0.20f, 0.45f, 0.95f),
                                  new Color(0.98f, 0.85f, 0.15f), new Color(0.60f, 0.30f, 0.90f), new Color(0.98f, 0.50f, 0.15f), new Color(0.15f, 0.80f, 0.75f) },
                RodFraction = 0.15f, MinSize = 0.06f, MaxSize = 0.13f, OpacityMin = 0.72f, OpacityMax = 0.92f, RoundShapes = 0.15f,
            };

            public static Config TwoMirrorDarkBeads() => new Config
            {
                MirrorMode = MirrorMode.Sector, SectorAngleDeg = 45f, DarkField = true, FragmentCount = 150, Style = 0, Seed = 23,
                Palette = new[] { new Color(0.30f, 0.92f, 1.00f), new Color(0.35f, 0.55f, 1.00f), new Color(0.40f, 0.98f, 0.50f),
                                  new Color(1.00f, 0.60f, 0.25f), new Color(1.00f, 0.45f, 0.78f), new Color(1.00f, 0.92f, 0.40f) },
                RodFraction = 0.06f, MinSize = 0.028f, MaxSize = 0.07f, OpacityMin = 0.8f, OpacityMax = 1.0f, RoundShapes = 0.7f,
            };

            public static Config ThreeMirrorPetals() => new Config
            {
                MirrorMode = MirrorMode.Triangle, FragmentCount = 130, Style = 1, TumbleScale = 0.6f, Seed = 37, PlaneScale = 2.4f,
                Palette = new[] { new Color(0.95f, 0.20f, 0.28f), new Color(0.20f, 0.70f, 0.35f), new Color(0.55f, 0.30f, 0.85f),
                                  new Color(0.20f, 0.50f, 0.95f), new Color(0.10f, 0.65f, 0.60f), new Color(0.75f, 0.85f, 0.15f), new Color(0.98f, 0.55f, 0.15f) },
                RodFraction = 0.05f, MinSize = 0.06f, MaxSize = 0.15f, OpacityMin = 0.65f, OpacityMax = 0.85f, RoundShapes = 0.0f,
            };

            public static Config TwoMirrorHexGlass() => new Config
            {
                MirrorMode = MirrorMode.Sector, SectorAngleDeg = 60f, DarkField = true, Style = 2, FragmentCount = 110, Seed = 41,
                Palette = new[] { new Color(0.30f, 0.95f, 0.75f), new Color(1.00f, 0.55f, 0.20f), new Color(0.60f, 0.45f, 1.00f),
                                  new Color(1.00f, 0.35f, 0.65f), new Color(0.25f, 0.75f, 1.00f), new Color(1.00f, 0.85f, 0.30f), new Color(0.55f, 1.00f, 0.35f) },
                RodFraction = 0.28f, MinSize = 0.035f, MaxSize = 0.09f, OpacityMin = 0.85f, OpacityMax = 1.0f, RoundShapes = 0.15f,
            };
        }

        // Equilateral cell triangle: MUST match the defaults in DomeMirror.shader.
        static readonly Vector2 TriA = new Vector2(-0.85f, -0.55f);
        static readonly Vector2 TriB = new Vector2(0.85f, -0.55f);
        static readonly Vector2 TriC = new Vector2(0f, 0.922f);
        const float DiscRadius = 1f;
        const float CoverageRad = 2.27f;   // pattern reaches this far from the zenith (matches KALEIDO_COVERAGE_RAD)

        struct Fragment
        {
            public Rigidbody2D Body;
            public float HalfW, HalfH;   // quad half extents (local units)
            public float Aspect;         // HalfH / HalfW
            public int Shape;            // SdFragment id
            public Color Color;          // rgb tint, a = opacity
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
        static readonly int WedgeRotationId = Shader.PropertyToID("_WedgeRotation");
        static readonly int WedgeHalfSinId = Shader.PropertyToID("_WedgeHalfSin");
        const int WedgeTextureHeight = 2048;   // texels along the wedge bisector (cell radius 1)

        Config _cfg;
        Vector3 _offset;
        GameObject _cellRoot;
        readonly List<Fragment> _fragments = new List<Fragment>();
        Mesh _mesh;
        RenderTexture _rt;
        Material _cellMat;
        Material _domeMat;
        Camera _cellCam;
        bool _renderedOnce;

        // reused mesh buffers
        readonly List<Vector3> _verts = new List<Vector3>(1024);
        readonly List<Color> _cols = new List<Color>(1024);
        readonly List<Vector2> _uv0 = new List<Vector2>(1024);
        readonly List<Vector2> _uv1 = new List<Vector2>(1024);
        readonly List<Vector2> _uv2 = new List<Vector2>(1024);
        readonly List<int> _tris = new List<int>(1536);

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

            // combined quad mesh, rendered by a small orthographic URP camera into the cell texture
            _mesh = Own(new Mesh { name = "CellMesh", indexFormat = IndexFormat.UInt32 });
            _mesh.MarkDynamic();
            _cellMat = Own(KaleidoRenderUtil.NewMaterial(_cfg.DarkField ? "Kaleido/CellScreen" : "Kaleido/CellMultiply"));
            var mr = CreateMeshObject("CellMesh", transform, _mesh, _cellMat, layer);
            mr.transform.position = Vector3.zero;

            // Two-mirror scopes: the camera frames only the visible wedge (bisector along the texture V
            // axis), so 2048 texels span the cell radius instead of ~768 -> ~2.7x sharper on the dome.
            bool sector = _cfg.MirrorMode == MirrorMode.Sector;
            float halfSin = Mathf.Sin(0.5f * _cfg.SectorAngleDeg * Mathf.Deg2Rad);
            int rtW = sector ? Mathf.Max(64, Mathf.RoundToInt(WedgeTextureHeight * 2f * halfSin)) : (int)KaleidoConfig.CellTextureSize;
            int rtH = sector ? WedgeTextureHeight : (int)KaleidoConfig.CellTextureSize;
            _rt = Own(KaleidoRenderUtil.CreateRT(rtW, rtH, true, "CellRT", RenderTextureFormat.ARGB32));
            _rt.filterMode = FilterMode.Trilinear;
            // A regular (enabled) camera with a target texture is the most robust per-frame RT path on
            // Android/Vulkan: it renders inside the URP frame, before the main camera (lower depth),
            // and mipmaps regenerate automatically.
            Color clear = _cfg.DarkField ? Color.black : Color.white;
            _cellCam = KaleidoRenderUtil.CreateHelperCamera("CellCamera", transform, 1 << layer, clear, true);
            _cellCam.transform.position = _offset + new Vector3(0f, 0f, -10f);
            _cellCam.transform.rotation = Quaternion.identity;
            _cellCam.orthographicSize = sector ? 0.5f : 1f;          // wedge: x in [0,1] along the bisector
            _cellCam.aspect = sector ? 2f * halfSin : 1f;            // wedge: |y| <= sin(w/2) across
            if (sector) FrameWedge(CellPhysics.GravityVectorAngleRad);
            _cellCam.nearClipPlane = 0.1f;
            _cellCam.farClipPlane = 20f;
            _cellCam.targetTexture = _rt;
            _cellCam.depth = -60f;
            _cellCam.enabled = !Ctx.IsEditorCapture;   // in edit-mode capture we render it explicitly per tick

            // dome
            _domeMat = NewDomeMaterial("Kaleido/DomeMirror");
            _domeMat.SetTexture(ShaderIds.CellTex, _rt);
            _domeMat.SetFloat(ModeId, _cfg.MirrorMode == MirrorMode.Sector ? 0f : 1f);
            _domeMat.SetFloat(SectorAngleId, _cfg.SectorAngleDeg * Mathf.Deg2Rad);
            // sector: the disc (radius 1) spans the whole coverage; triangle: PlaneScale plane units per radian
            _domeMat.SetFloat(ScaleId, _cfg.MirrorMode == MirrorMode.Sector ? DiscRadius / CoverageRad : _cfg.PlaneScale);
            _domeMat.SetVector(TriAId, new Vector4(TriA.x, TriA.y, 0f, 0f));
            _domeMat.SetVector(TriBId, new Vector4(TriB.x, TriB.y, 0f, 0f));
            _domeMat.SetVector(TriCId, new Vector4(TriC.x, TriC.y, 0f, 0f));
            _domeMat.SetFloat(DarkFieldId, _cfg.DarkField ? 1f : 0f);
            _domeMat.SetFloat(DiscRadiusId, DiscRadius);
            _domeMat.SetFloat(WedgeHalfSinId, halfSin);
            CreateDome(_domeMat);

            RebuildMesh();
            RenderCell();
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
                RenderCell();
                _renderedOnce = true;
            }
            if (_cfg.MirrorMode == MirrorMode.Sector)
            {
                // keep the visible V centred on the pile (gravity direction); frozen with the physics while paused
                FrameWedge(CellPhysics.GravityVectorAngleRad);
            }
        }

        protected override void OnTeardown()
        {
            _fragments.Clear();
            if (_cellCam != null) { _cellCam.enabled = false; _cellCam.targetTexture = null; }
        }

        /// <summary>Point the cell camera's up axis along the wedge bisector (gravity direction) and frame [0,1] x [-s,s].</summary>
        void FrameWedge(float bisectorRad)
        {
            if (_cellCam == null) return;
            var up = new Vector3(Mathf.Cos(bisectorRad), Mathf.Sin(bisectorRad), 0f);
            _cellCam.transform.rotation = Quaternion.LookRotation(Vector3.forward, up);
            _cellCam.transform.position = _offset + up * 0.5f + new Vector3(0f, 0f, -10f);
        }

        /// <summary>Editor capture only: render the cell camera immediately (in play mode it renders itself).</summary>
        void RenderCell()
        {
            if (_cellCam == null || _rt == null) return;
            if (Ctx.IsEditorCapture) KaleidoRenderUtil.RenderCameraTo(_cellCam, _rt);
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
            edge.edgeRadius = 0.01f;
        }

        // ------------------------------------------------------------------ fragments
        void BuildFragments(int layer)
        {
            var rng = new System.Random(_cfg.Seed);
            var physMat = Own(new PhysicsMaterial2D("Glass") { bounciness = 0.05f, friction = 0.4f });
            int count = Mathf.Clamp(_cfg.FragmentCount, 40, 250);
            for (int i = 0; i < count; i++)
            {
                float size = Mathf.Lerp(_cfg.MinSize, _cfg.MaxSize, (float)rng.NextDouble());
                int style = _cfg.Style;
                ChooseShape(rng, style, ref size, out int shape, out float aspect);
                float halfW = size, halfH = size * aspect;

                var go = new GameObject("F" + i);
                go.layer = layer;
                go.transform.SetParent(_cellRoot.transform, false);
                go.transform.localPosition = RandomPointInCell(rng);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);

                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.gravityScale = _cfg.TumbleScale;
                rb.linearDamping = 2.5f;
                rb.angularDamping = 1.5f;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;   // slow bodies, cheap
                rb.interpolation = RigidbodyInterpolation2D.None;
                rb.sleepMode = RigidbodySleepMode2D.StartAwake;
                rb.useAutoMass = false;
                rb.mass = Mathf.Max(0.05f, halfW * halfH * 30f);

                Collider2D col;
                if (shape == 0)
                {
                    var c = go.AddComponent<CircleCollider2D>();
                    c.radius = size * 0.92f;
                    col = c;
                }
                else
                {
                    var pc = go.AddComponent<PolygonCollider2D>();
                    pc.points = ColliderOutline(shape, halfW, halfH);
                    col = pc;
                }
                col.sharedMaterial = physMat;

                var tint = _cfg.Palette[rng.Next(_cfg.Palette.Length)];
                float opacity = Mathf.Lerp(_cfg.OpacityMin, _cfg.OpacityMax, (float)rng.NextDouble());
                _fragments.Add(new Fragment
                {
                    Body = rb, HalfW = halfW, HalfH = halfH, Aspect = aspect, Shape = shape,
                    Color = new Color(tint.r, tint.g, tint.b, opacity),
                    Seed = (float)rng.NextDouble(), Style = style,
                });
            }
        }

        /// <summary>Pick an SDF shape id (see SdFragment in KaleidoCommon.hlsl) and its aspect.</summary>
        void ChooseShape(System.Random rng, int style, ref float size, out int shape, out float aspect)
        {
            double roll = rng.NextDouble();
            if (style == 1)
            {
                shape = 4;                                                   // petal / leaf / feather
                aspect = Mathf.Lerp(0.30f, 0.55f, (float)rng.NextDouble());
                size *= 1.15f;
                return;
            }
            if (roll < _cfg.RodFraction)
            {
                shape = 2;                                                   // rod
                aspect = Mathf.Lerp(0.18f, 0.32f, (float)rng.NextDouble());
                size *= 1.9f;
                return;
            }
            if (roll < _cfg.RodFraction + _cfg.RoundShapes)
            {
                shape = 0; aspect = 1f;                                      // bead
                return;
            }
            int[] choices = { 1, 3, 5, 6, 1, 3 };
            shape = choices[rng.Next(choices.Length)];
            aspect = shape == 3 ? Mathf.Lerp(0.5f, 0.8f, (float)rng.NextDouble()) : 1f;
        }

        /// <summary>Convex outline for the physics collider matching the SDF shape (local units).</summary>
        static Vector2[] ColliderOutline(int shape, float hw, float hh)
        {
            switch (shape)
            {
                case 1: // hexagon, radius 0.9
                    return RegularPolygon(6, hw * 0.9f, 0f);
                case 2: // rod: rounded box (0.95, aspect)
                    return new[] { new Vector2(-hw * 0.95f, -hh), new Vector2(hw * 0.95f, -hh), new Vector2(hw * 0.95f, hh), new Vector2(-hw * 0.95f, hh) };
                case 3: // rhombus (0.95, aspect)
                    return new[] { new Vector2(-hw * 0.95f, 0f), new Vector2(0f, -hh), new Vector2(hw * 0.95f, 0f), new Vector2(0f, hh) };
                case 4: // petal: ellipse (1, aspect) tapered toward +x
                    {
                        var pts = new Vector2[10];
                        for (int i = 0; i < 10; i++)
                        {
                            float a = (float)i / 10 * Mathf.PI * 2f;
                            float x = Mathf.Cos(a), y = Mathf.Sin(a);
                            float taper = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(x * 0.5f + 0.5f));
                            pts[i] = new Vector2(x * hw, y * hh * taper);
                        }
                        return pts;
                    }
                case 5: // triangle radius 0.85 (+ rounding)
                    return RegularPolygon(3, hw * 0.95f, Mathf.PI / 2f);
                default: // rounded square 0.85
                    return new[] { new Vector2(-hw * 0.85f, -hh * 0.85f), new Vector2(hw * 0.85f, -hh * 0.85f), new Vector2(hw * 0.85f, hh * 0.85f), new Vector2(-hw * 0.85f, hh * 0.85f) };
            }
        }

        static Vector2[] RegularPolygon(int n, float r, float phase)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = phase + (float)i / n * Mathf.PI * 2f;
                pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return pts;
        }

        Vector2 RandomPointInCell(System.Random rng)
        {
            if (_cfg.MirrorMode == MirrorMode.Sector)
            {
                float r = Mathf.Sqrt((float)rng.NextDouble()) * 0.8f;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            Vector2 p = TriA + (TriB - TriA) * u + (TriC - TriA) * v;
            Vector2 c = (TriA + TriB + TriC) / 3f;
            return c + (p - c) * 0.78f;
        }

        // ------------------------------------------------------------------ mesh
        void RebuildMesh()
        {
            _verts.Clear(); _cols.Clear(); _uv0.Clear(); _uv1.Clear(); _uv2.Clear(); _tris.Clear();
            float z = _offset.z;
            for (int f = 0; f < _fragments.Count; f++)
            {
                var fr = _fragments[f];
                if (fr.Body == null) continue;
                Vector2 c = fr.Body.position;
                float rot = fr.Body.rotation * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                // quad larger than the shape: room for the AA edge, and for the analytic halo in dark field
                float pad = _cfg.DarkField ? fr.HalfW * 0.75f : fr.HalfW * 0.08f;
                float hw = fr.HalfW + pad, hh = fr.HalfH + pad;
                int b = _verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    float lx = (k == 1 || k == 2) ? hw : -hw;
                    float ly = (k >= 2) ? hh : -hh;
                    float x = c.x + lx * cs - ly * sn;
                    float y = c.y + lx * sn + ly * cs;
                    _verts.Add(new Vector3(x, y, z));
                    _cols.Add(fr.Color);
                    _uv0.Add(new Vector2(lx / fr.HalfW, ly / fr.HalfW));   // shape space: width = 1, height = aspect
                    _uv1.Add(new Vector2(fr.Shape, fr.Seed));
                    _uv2.Add(new Vector2(fr.Aspect, fr.Style));
                }
                _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 1);
                _tris.Add(b); _tris.Add(b + 3); _tris.Add(b + 2);
            }
            _mesh.Clear(false);
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_cols);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetUVs(2, _uv2);
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(_offset, new Vector3(3f, 3f, 1f));
        }
    }
}
