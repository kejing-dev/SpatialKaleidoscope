using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Visible controller rays (one per tracked motion controller): a thin glowing ribbon from the
    /// controller aim pose to the point it hits (a nav button) or 2.5 m out, with a small end dot.
    /// Drawn with the stereo-safe Kaleido/GlowLine ribbon shader. Hands do not get a ray (the
    /// system eye-hand cursor is used there).
    /// </summary>
    public class PointerRayVisual : MonoBehaviour
    {
        public const float DefaultLength = 2.5f;
        const int Count = 2;
        static readonly Color IdleColor = new Color(0.55f, 0.85f, 1.0f, 1f);
        static readonly Color HoverColor = new Color(1.0f, 1.0f, 1.0f, 1f);

        readonly Mesh[] _meshes = new Mesh[Count];
        readonly Material[] _mats = new Material[Count];
        readonly MeshRenderer[] _renderers = new MeshRenderer[Count];
        readonly Vector3[] _verts = new Vector3[8];
        readonly Vector3[] _normals = new Vector3[8];
        readonly Vector2[] _uvs = new Vector2[8];
        static readonly int WidthId = Shader.PropertyToID("_Width");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        void Awake() { Build(); }

        public void Build()
        {
            if (_meshes[0] != null) return;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject(i == 0 ? "RayLeft" : "RayRight");
                go.transform.SetParent(transform, false);
                _meshes[i] = new Mesh { name = "PointerRay" };
                _meshes[i].MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = _meshes[i];
                var mr = go.AddComponent<MeshRenderer>();
                _mats[i] = KaleidoRenderUtil.NewMaterial("Kaleido/GlowLine");
                _mats[i].SetFloat(WidthId, 0.0035f);
                _mats[i].SetColor(ColorId, IdleColor);
                _mats[i].SetFloat(IntensityId, 1.2f);
                _mats[i].SetFloat(FadeId, 1f);
                _mats[i].renderQueue = 3020;
                mr.sharedMaterial = _mats[i];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _renderers[i] = mr;
                mr.enabled = false;
            }
        }

        /// <summary>Update one ray (index 0 = left, 1 = right). Pass null to hide it.</summary>
        public void Set(int index, Ray? ray, float length, bool hover)
        {
            if (index < 0 || index >= Count || _renderers[index] == null) return;
            if (!ray.HasValue)
            {
                _renderers[index].enabled = false;
                return;
            }
            Vector3 o = ray.Value.origin;
            Vector3 d = ray.Value.direction.normalized;
            Vector3 e = o + d * Mathf.Max(0.05f, length);
            // ribbon: 2 vertices per point; NORMAL = tangent, uv.x = side, uv.y = age (0 head .. 1 tail)
            _verts[0] = o; _verts[1] = o; _verts[2] = e; _verts[3] = e;
            for (int i = 0; i < 4; i++) _normals[i] = d;
            _uvs[0] = new Vector2(-1f, 0.15f); _uvs[1] = new Vector2(1f, 0.15f);
            _uvs[2] = new Vector2(-1f, 0.55f); _uvs[3] = new Vector2(1f, 0.55f);
            // end dot: a short, wider segment across the ray end
            Vector3 up = Vector3.Cross(d, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(d, Vector3.up).normalized : Vector3.right;
            Vector3 dotA = e - up * 0.012f, dotB = e + up * 0.012f;
            _verts[4] = dotA; _verts[5] = dotA; _verts[6] = dotB; _verts[7] = dotB;
            for (int i = 4; i < 8; i++) _normals[i] = up;
            _uvs[4] = new Vector2(-1f, 0f); _uvs[5] = new Vector2(1f, 0f);
            _uvs[6] = new Vector2(-1f, 0f); _uvs[7] = new Vector2(1f, 0f);
            var m = _meshes[index];
            m.Clear(false);
            m.vertices = _verts;
            m.normals = _normals;
            m.uv = _uvs;
            m.SetIndices(new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 }, MeshTopology.Triangles, 0, false);
            m.bounds = new Bounds((o + e) * 0.5f, new Vector3(Mathf.Abs(e.x - o.x) + 0.2f, Mathf.Abs(e.y - o.y) + 0.2f, Mathf.Abs(e.z - o.z) + 0.2f));
            _mats[index].SetColor(ColorId, hover ? HoverColor : IdleColor);
            _mats[index].SetFloat(IntensityId, hover ? 2.0f : 1.2f);
            _renderers[index].enabled = true;
        }

        void OnDestroy()
        {
            for (int i = 0; i < Count; i++)
            {
                KaleidoRenderUtil.SafeDestroy(_mats[i]);
                KaleidoRenderUtil.SafeDestroy(_meshes[i]);
            }
        }
    }
}
