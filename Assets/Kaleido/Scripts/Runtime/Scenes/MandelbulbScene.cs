using UnityEngine;

namespace Kaleido.Scenes
{
    /// <summary>
    /// Playlist item 8 "Mandelbulb" (3D object, 1.2 m diameter, 2 m in front at eye height).
    ///
    /// The bulb is sphere-traced by Kaleido/Mandelbulb on a proxy sphere, but NOT in the main XR
    /// pass: two disabled helper cameras (one per eye) render the proxy at HALF the eye resolution
    /// (eyeWidth/2 x eyeHeight/2, PRD) into two render textures, using exactly the main camera's
    /// per-eye view and projection matrices. A second renderer on the same proxy, Kaleido/BulbComposite,
    /// is drawn by the main camera and samples _BulbTexL / _BulbTexR by screen position
    /// (ComputeScreenPos) and unity_StereoEyeIndex, so NDC coincide 1:1 whatever the texture size.
    ///
    /// Evolution: _Power = 8 - 2 cos(2 pi t / 120) (6..10 over a 120 s round trip) is a pure function of
    /// the evolution time handed to OnTick and the object root (base class) spins 6 deg/s with dt, so
    /// pausing (dt == 0, evoTime frozen) freezes the bulb exactly. The helper cameras are re-synchronised
    /// and re-rendered EVERY tick, paused or not, because the head keeps moving.
    /// Works in play mode (stereo) and when ticked by the editor capture tool (mono, no Awake/Start/Update).
    /// </summary>
    public class MandelbulbScene : KaleidoSceneBase
    {
        // Kaleido/Mandelbulb Properties block: _Power, _Fade, _EvoTime (_Fade / _EvoTime come from ShaderIds).
        static readonly int PowerId = Shader.PropertyToID("_Power");
        // Kaleido/BulbComposite Properties block: _BulbTexL, _BulbTexR, _Fade (_Fade is driven by the base class).
        static readonly int BulbTexLId = Shader.PropertyToID("_BulbTexL");
        static readonly int BulbTexRId = Shader.PropertyToID("_BulbTexR");

        // Kaleido/Mandelbulb hard-codes the proxy as a UNIT sphere in object space ("intersect proxy sphere
        // (radius 1)": c = dot(ro, ro) - 1), so the mesh is built with radius 1 and the 1.2 m diameter comes
        // from the renderer transform scale. A 0.6 m mesh would clip the bulb (object-space radius ~0.9) to
        // a 0.6 disc because the fragments would only cover the inner part of the unit sphere.
        const float ProxyMeshRadius = 1f;
        const float ProxyScale = KaleidoConfig.MandelbulbDiameter * 0.5f;   // 0.6 -> world diameter 1.2 m

        const double PowerPeriodSeconds = 120.0;   // 6 -> 10 -> 6 round trip
        const float PowerCentre = 8f;
        const float PowerAmplitude = 2f;
        const double TwoPi = System.Math.PI * 2.0;

        const int FallbackEyeSize = 1024;           // when neither XR nor the main camera reports a size
        const int HelperDepthBits = 16;             // Kaleido/Mandelbulb has ZWrite On

        static readonly Color HelperClear = new Color(0f, 0f, 0f, 0f);   // alpha = coverage for the composite

        Material _domeMaterial;
        Material _bulbMaterial;        // Kaleido/Mandelbulb: rendered by the helper cameras only (bulb layer)
        Material _compositeMaterial;   // Kaleido/BulbComposite: rendered by the main camera (layer 0)
        Mesh _proxyMesh;
        Transform _root;
        Camera _helperL, _helperR;
        RenderTexture _rtL, _rtR;
        int _rtWidth, _rtHeight;

        protected override void OnSetup()
        {
            // ---- background
            _domeMaterial = NewDomeMaterial("Kaleido/DomeDark");   // owned + _Fade + queue by the base
            CreateDome(_domeMaterial);

            // ---- proxy geometry: both renderers share one mesh and sit under the self-rotating root
            _root = CreateObjectRoot();
            _proxyMesh = Own(MeshUtil.Sphere(ProxyMeshRadius));

            _bulbMaterial = Own(KaleidoRenderUtil.NewMaterial("Kaleido/Mandelbulb"));
            _bulbMaterial.SetFloat(ShaderIds.Fade, 1f);   // the bulb pass writes opaque coverage; the composite fades
            ApplyBulbParams(EvolutionClock.Time);        // same phase as the first Tick (edit-mode JumpTo + capture)
            var proxy = CreateMeshObject("BulbProxy", _root, _proxyMesh, _bulbMaterial, Ctx.BulbLayer);
            proxy.transform.localScale = Vector3.one * ProxyScale;

            // KNOWN RISK - vertical flip: the composite looks the textures up with ComputeScreenPos, which already
            // accounts for the main pass' _ProjectionParams.x, and Unity un-flips camera renders into render
            // textures, so in theory no correction is needed on any graphics API. If captures or the device show
            // the bulb upside down, correct it in BulbComposite.shader with `uv.y = 1.0 - uv.y` (ideally guarded by
            // `if (_ProjectionParams.x < 0)`); nothing in this class needs to change for that.
            _compositeMaterial = NewObjectMaterial("Kaleido/BulbComposite");   // owned + _Fade + queue by the base
            var composite = CreateMeshObject("BulbComposite", _root, _proxyMesh, _compositeMaterial, 0);
            composite.transform.localScale = Vector3.one * ProxyScale;

            // ---- helper cameras: children of the scene object (NOT of the spinning root); moved onto the eyes every tick.
            // Disabled cameras, rendered explicitly from OnTick via KaleidoRenderUtil.RenderCameraTo. Because URP's
            // render request assigns camera.targetTexture before rendering, XRLayout gives them a mono (non-XR) pass.
            int mask = 1 << Ctx.BulbLayer;
            _helperL = KaleidoRenderUtil.CreateHelperCamera("BulbHelperL", transform, mask, HelperClear, false);
            _helperR = KaleidoRenderUtil.CreateHelperCamera("BulbHelperR", transform, mask, HelperClear, false);

            EnsureRenderTextures();   // also binds _BulbTexL / _BulbTexR on the composite material
        }

        protected override void OnTick(float dt, double evoTime)
        {
            // dt is intentionally unused here: the bulb is a pure function of evoTime (frozen while paused) and
            // the 6 deg/s self-rotation is applied to the object root by the base class with the same dt.
            ApplyBulbParams(evoTime);

            // Every tick, paused or not: the head keeps moving, so the per-eye textures must follow the eyes.
            RenderHelpers();
        }

        protected override void OnTeardown()
        {
            // Materials, mesh and render textures are Own()ed and destroyed by the base class; the helper cameras
            // are children of the scene object, which the base class destroys as well. Just drop the references.
            _domeMaterial = null;
            _bulbMaterial = null;
            _compositeMaterial = null;
            _proxyMesh = null;
            _root = null;
            _helperL = null;
            _helperR = null;
            _rtL = null;
            _rtR = null;
        }

        // ------------------------------------------------------------------ evolution
        void ApplyBulbParams(double evoTime)
        {
            if (_bulbMaterial == null) return;
            // Wrapped in double precision before the float cast so long sessions never lose phase precision.
            // Power changes by at most 2 * 2pi/120 = 0.105 per second: no angular quantity, no discontinuity.
            double phase = Wrap(evoTime, PowerPeriodSeconds) * (TwoPi / PowerPeriodSeconds);
            float power = PowerCentre - PowerAmplitude * (float)System.Math.Cos(phase);   // 6 at t = 0, 10 at t = 60 s
            _bulbMaterial.SetFloat(PowerId, power);
            _bulbMaterial.SetFloat(ShaderIds.EvoTime, (float)evoTime);
        }

        /// <summary>x modulo period into [0, period); correct for negative x as well.</summary>
        static double Wrap(double x, double period)
        {
            double r = x - System.Math.Floor(x / period) * period;
            return r < period ? r : 0.0;   // rounding can land exactly on 'period'; 0 is equivalent
        }

        // ------------------------------------------------------------------ per-eye helper rendering
        void RenderHelpers()
        {
            var mainCam = Ctx != null ? Ctx.MainCamera : null;
            if (mainCam == null || _helperL == null || _helperR == null) return;

            EnsureRenderTextures();   // re-creates the textures when the eye texture size changes

            if (StereoActive && mainCam.stereoEnabled)
            {
                // Exactly the matrices the main XR pass uses per eye (position offset, asymmetric frustum included).
                SyncHelper(_helperL, mainCam,
                    mainCam.GetStereoViewMatrix(Camera.StereoscopicEye.Left),
                    mainCam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left));
                SyncHelper(_helperR, mainCam,
                    mainCam.GetStereoViewMatrix(Camera.StereoscopicEye.Right),
                    mainCam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right));
            }
            else
            {
                // Mono (editor capture / no XR): both helpers see the main camera; the composite samples eye 0.
                Matrix4x4 view = mainCam.worldToCameraMatrix;
                Matrix4x4 proj = mainCam.projectionMatrix;
                SyncHelper(_helperL, mainCam, view, proj);
                SyncHelper(_helperR, mainCam, view, proj);
            }

            KaleidoRenderUtil.RenderCameraTo(_helperL, _rtL);
            KaleidoRenderUtil.RenderCameraTo(_helperR, _rtR);
        }

        /// <summary>
        /// Make <paramref name="helper"/> see exactly what one eye of the main camera sees. The explicit matrices
        /// drive rendering and culling; the transform is ALSO moved onto the eye because URP fills
        /// _WorldSpaceCameraPos from camera.transform.position (UniversalRenderPipeline.InitializeCameraData) and
        /// Kaleido/Mandelbulb starts its rays at GetCameraPositionWS().
        /// </summary>
        static void SyncHelper(Camera helper, Camera mainCam, Matrix4x4 view, Matrix4x4 proj)
        {
            helper.nearClipPlane = mainCam.nearClipPlane;
            helper.farClipPlane = mainCam.farClipPlane;

            Matrix4x4 camToWorld = view.inverse;
            Vector4 c1 = camToWorld.GetColumn(1);
            Vector4 c2 = camToWorld.GetColumn(2);
            Vector4 c3 = camToWorld.GetColumn(3);
            var pos = new Vector3(c3.x, c3.y, c3.z);
            var up = new Vector3(c1.x, c1.y, c1.z);
            var fwd = new Vector3(-c2.x, -c2.y, -c2.z);   // Unity view space looks down -Z
            if (fwd.sqrMagnitude > 1e-8f && up.sqrMagnitude > 1e-8f)
                helper.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));

            // Explicit matrices override the transform for rendering and culling until Reset*Matrix() is called;
            // they are re-assigned every tick so the helper never drifts from the main camera.
            helper.worldToCameraMatrix = view;
            helper.projectionMatrix = proj;
        }

        // ------------------------------------------------------------------ render textures
        void EnsureRenderTextures()
        {
            ComputeHalfEyeSize(out int w, out int h);
            if (_rtL != null && _rtR != null && _rtWidth == w && _rtHeight == h) return;

            ReleaseRenderTextures();
            _rtL = Own(CreateHelperRT(w, h, "BulbRT_L"));
            _rtR = Own(CreateHelperRT(w, h, "BulbRT_R"));
            _rtWidth = w;
            _rtHeight = h;

            if (_compositeMaterial != null)
            {
                _compositeMaterial.SetTexture(BulbTexLId, _rtL);
                _compositeMaterial.SetTexture(BulbTexRId, _rtR);
            }
        }

        void ReleaseRenderTextures()
        {
            // Own() keeps the references for Teardown (which skips already-destroyed entries); releasing here frees
            // the GPU memory immediately when the eye texture size changes instead of waiting for the scene to end.
            if (_rtL != null) { _rtL.Release(); KaleidoRenderUtil.SafeDestroy(_rtL); }
            if (_rtR != null) { _rtR.Release(); KaleidoRenderUtil.SafeDestroy(_rtR); }
            _rtL = null;
            _rtR = null;
        }

        /// <summary>Half the per-eye resolution (PRD: each eye at 1/2 width and 1/2 height).</summary>
        void ComputeHalfEyeSize(out int width, out int height)
        {
            int eyeW = 0, eyeH = 0;
            if (StereoActive)
            {
                eyeW = UnityEngine.XR.XRSettings.eyeTextureWidth;
                eyeH = UnityEngine.XR.XRSettings.eyeTextureHeight;
            }
            else if (Ctx != null && Ctx.MainCamera != null)
            {
                eyeW = Ctx.MainCamera.pixelWidth;
                eyeH = Ctx.MainCamera.pixelHeight;
            }
            if (eyeW <= 0 || eyeH <= 0)
            {
                eyeW = FallbackEyeSize;
                eyeH = FallbackEyeSize;
            }
            width = Mathf.Max(1, (int)(eyeW * 0.6f));   // 0.6 per axis (device review: less aliasing than 1/2)
            height = Mathf.Max(1, (int)(eyeH * 0.6f));
        }

        static RenderTexture CreateHelperRT(int width, int height, string name)
        {
            // Same settings as KaleidoRenderUtil.CreateRT plus a 16-bit depth buffer (Kaleido/Mandelbulb has ZWrite On).
            // ARGB32 in Linear colour space is an sRGB-encoded target; the composite decodes it on sampling.
            var rt = new RenderTexture(width, height, HelperDepthBits, RenderTextureFormat.ARGB32)
            {
                name = name,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
            };
            rt.Create();
            return rt;
        }
    }
}
