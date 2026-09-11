using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Two resident symbolic buttons below the horizon: left = previous, right = next.
    /// Placed once, together with the object anchor, relative to the initial view point.
    /// Hit-testing is done against the button rectangles in world space (no physics needed).
    /// </summary>
    public class NavButtons : MonoBehaviour
    {
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int PressId = Shader.PropertyToID("_Press");
        static readonly int DirId = Shader.PropertyToID("_Dir");

        readonly Transform[] _buttons = new Transform[2];   // 0 = previous (left), 1 = next (right)
        readonly Material[] _mats = new Material[2];
        readonly float[] _hover = new float[2];
        readonly float[] _press = new float[2];
        Mesh _quad;
        bool _placed;

        public bool Placed => _placed;

        void Awake() { Build(); }

        public void Build()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "NavButtonQuad" };
            float h = 0.5f, v = 0.5f * 0.68f;
            _quad.vertices = new[] { new Vector3(-h, -v, 0), new Vector3(h, -v, 0), new Vector3(h, v, 0), new Vector3(-h, v, 0) };
            _quad.uv = new[] { new Vector2(0, 0.16f), new Vector2(1, 0.16f), new Vector2(1, 0.84f), new Vector2(0, 0.84f) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds();
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "NavPrev" : "NavNext");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quad;
                var mr = go.AddComponent<MeshRenderer>();
                _mats[i] = KaleidoRenderUtil.NewMaterial("Kaleido/NavButton");
                _mats[i].SetFloat(DirId, i == 0 ? -1f : 1f);
                mr.sharedMaterial = _mats[i];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.transform.localScale = Vector3.one * KaleidoConfig.NavButtonSize;
                _buttons[i] = go.transform;
                go.SetActive(false);
            }
        }

        /// <summary>Place both buttons in front of and below the initial view point, facing it.</summary>
        public void Place(Vector3 eye, Vector3 flatForward)
        {
            Build();
            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            Vector3 centre = eye + flatForward * KaleidoConfig.NavButtonForward - Vector3.up * KaleidoConfig.NavButtonDrop;
            for (int i = 0; i < 2; i++)
            {
                Vector3 pos = centre + right * (i == 0 ? -KaleidoConfig.NavButtonSpread : KaleidoConfig.NavButtonSpread);
                Vector3 toEye = (eye - pos).normalized;
                _buttons[i].position = pos;
                _buttons[i].rotation = Quaternion.LookRotation(-toEye, Vector3.up);   // quad +Z faces away from the eye
                _buttons[i].gameObject.SetActive(true);
            }
            _placed = true;
        }

        /// <summary>Which button the ray hits: -1 none, 0 previous, 1 next.</summary>
        public int HitTest(Ray ray)
        {
            if (!_placed) return -1;
            int best = -1; float bestT = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                var t = _buttons[i];
                Vector3 n = t.forward;
                float denom = Vector3.Dot(n, ray.direction);
                if (Mathf.Abs(denom) < 1e-4f) continue;
                float dist = Vector3.Dot(t.position - ray.origin, n) / denom;
                if (dist < 0.05f || dist > 6f) continue;
                Vector3 p = ray.origin + ray.direction * dist;
                Vector3 local = t.InverseTransformPoint(p);      // scale applied: local extents are +-0.5 / +-0.34
                // generous hit box (a little larger than the drawn pill)
                if (Mathf.Abs(local.x) <= 0.62f && Mathf.Abs(local.y) <= 0.48f && dist < bestT) { best = i; bestT = dist; }
            }
            return best;
        }

        public void SetHover(int index)
        {
            for (int i = 0; i < 2; i++) _hover[i] = i == index ? 1f : 0f;
        }

        public void Flash(int index)
        {
            if (index >= 0 && index < 2) _press[index] = 1f;
        }

        /// <summary>Animate hover / press state (call once per frame with real delta time).</summary>
        public void Step(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                if (_mats[i] == null) continue;
                float h = _mats[i].GetFloat(HoverId);
                h = Mathf.MoveTowards(h, _hover[i], dt * 6f);
                _mats[i].SetFloat(HoverId, h);
                _press[i] = Mathf.MoveTowards(_press[i], 0f, dt * 3f);
                _mats[i].SetFloat(PressId, _press[i]);
            }
        }

        void OnDestroy()
        {
            foreach (var m in _mats) KaleidoRenderUtil.SafeDestroy(m);
            KaleidoRenderUtil.SafeDestroy(_quad);
        }
    }
}
