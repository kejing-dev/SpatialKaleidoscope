using System.Collections.Generic;
using UnityEngine;

namespace Kaleido
{
    /// <summary>
    /// Base class for the 12 playlist items. A scene owns its GameObjects, materials and render
    /// textures, is ticked with evolution time (dt == 0 while paused) and exposes two fade channels
    /// used by the 0.6 s crossfade:
    ///   dome fade   - alpha of the dome overlay (outgoing dome stays opaque, incoming ramps 0..1)
    ///   object fade - alpha of 3D objects / dots (outgoing ramps 1..0, incoming 0..1)
    /// Everything must work when ticked manually in edit mode (capture tool) as well as in play mode.
    /// </summary>
    public abstract class KaleidoSceneBase : MonoBehaviour
    {
        public int PlaylistIndex { get; private set; }
        public string DisplayName => Definition != null ? Definition.Title : name;
        public SceneDefinition Definition { get; private set; }
        protected SceneContext Ctx { get; private set; }

        public float DomeFade { get; private set; }
        public float ObjectFade { get; private set; }
        public bool IsIncoming { get; private set; }

        readonly List<Material> _domeMaterials = new List<Material>();
        readonly List<Material> _objectMaterials = new List<Material>();
        readonly List<Object> _owned = new List<Object>();
        Transform _objectRoot;
        float _spinDeg;

        const int DomeQueue = 3000;
        const int ObjectQueue = 3010;

        public void Setup(SceneContext ctx, SceneDefinition def, int index)
        {
            Ctx = ctx;
            Definition = def;
            PlaylistIndex = index;
            DomeFade = 0f;
            ObjectFade = 0f;
            OnSetup();
            ApplyFade(0f, 0f);
        }

        /// <summary>dt is evolution delta (0 when paused). evoTime is the global evolution clock.</summary>
        public void Tick(float dt, double evoTime)
        {
            if (_objectRoot != null && Ctx.ObjectAnchor != null)
            {
                // follow the session anchor (it is placed on the first tracked frame) and self-rotate 6 deg/s
                _spinDeg = Mathf.Repeat(_spinDeg + KaleidoConfig.ObjectSpinDegPerSec * dt, 360f);
                _objectRoot.SetPositionAndRotation(Ctx.ObjectAnchor.position,
                    Ctx.ObjectAnchor.rotation * Quaternion.Euler(0f, _spinDeg, 0f));
            }
            OnTick(dt, evoTime);
        }

        public void ApplyFade(float domeFade, float objectFade)
        {
            DomeFade = Mathf.Clamp01(domeFade);
            ObjectFade = Mathf.Clamp01(objectFade);
            for (int i = 0; i < _domeMaterials.Count; i++) if (_domeMaterials[i] != null) _domeMaterials[i].SetFloat(ShaderIds.Fade, DomeFade);
            for (int i = 0; i < _objectMaterials.Count; i++) if (_objectMaterials[i] != null) _objectMaterials[i].SetFloat(ShaderIds.Fade, ObjectFade);
            OnFade(DomeFade, ObjectFade);
        }

        public void SetIncoming(bool incoming)
        {
            IsIncoming = incoming;
            int bump = incoming ? 1 : 0;
            for (int i = 0; i < _domeMaterials.Count; i++) if (_domeMaterials[i] != null) _domeMaterials[i].renderQueue = DomeQueue + bump;
            for (int i = 0; i < _objectMaterials.Count; i++) if (_objectMaterials[i] != null) _objectMaterials[i].renderQueue = ObjectQueue + bump;
        }

        public void Teardown()
        {
            OnTeardown();
            for (int i = 0; i < _owned.Count; i++)
            {
                var o = _owned[i];
                if (o == null) continue;
                if (o is RenderTexture rt) rt.Release();
                KaleidoRenderUtil.SafeDestroy(o);
            }
            _owned.Clear();
            _domeMaterials.Clear();
            _objectMaterials.Clear();
            KaleidoRenderUtil.SafeDestroy(gameObject);
        }

        /// <summary>Whether a helper-camera render should be stereo (play mode with XR) or mono.</summary>
        protected bool StereoActive => !Ctx.IsEditorCapture && UnityEngine.XR.XRSettings.enabled && Application.isPlaying;

        protected abstract void OnSetup();
        protected abstract void OnTick(float dt, double evoTime);
        protected virtual void OnFade(float domeFade, float objectFade) { }
        protected virtual void OnTeardown() { }

        // ------------------------------------------------------------------ helpers
        /// <summary>Own an object so it is destroyed with the scene.</summary>
        protected T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        protected Material NewDomeMaterial(string shaderName)
        {
            var m = Own(KaleidoRenderUtil.NewMaterial(shaderName));
            m.renderQueue = DomeQueue;
            _domeMaterials.Add(m);
            return m;
        }

        protected Material NewObjectMaterial(string shaderName)
        {
            var m = Own(KaleidoRenderUtil.NewMaterial(shaderName));
            m.renderQueue = ObjectQueue;
            _objectMaterials.Add(m);
            return m;
        }

        /// <summary>Create the dome renderer for this scene (child of the scene object, at world origin).</summary>
        protected MeshRenderer CreateDome(Material material)
        {
            var go = new GameObject("Dome");
            go.transform.SetParent(transform, false);
            go.transform.position = Ctx.DomeRoot.position;
            go.transform.rotation = Quaternion.identity;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = Ctx.DomeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return mr;
        }

        /// <summary>Root for a 3D object: placed at the anchor, self-rotates 6 deg/s (frozen while paused).</summary>
        protected Transform CreateObjectRoot()
        {
            var go = new GameObject("ObjectRoot");
            go.transform.SetParent(transform, false);
            go.transform.position = Ctx.ObjectAnchor.position;
            go.transform.rotation = Ctx.ObjectAnchor.rotation;
            _objectRoot = go.transform;
            return _objectRoot;
        }

        protected MeshRenderer CreateMeshObject(string name, Transform parent, Mesh mesh, Material mat, int layer = 0)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return mr;
        }
    }

    public static class ShaderIds
    {
        public static readonly int Fade = Shader.PropertyToID("_Fade");
        public static readonly int EvoTime = Shader.PropertyToID("_EvoTime");
        public static readonly int Brightness = Shader.PropertyToID("_KaleidoBrightness");
        public static readonly int CellTex = Shader.PropertyToID("_CellTex");
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
    }
}
