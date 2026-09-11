using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kaleido
{
    /// <summary>
    /// On-demand rendering helpers. All helper cameras in the app are disabled components that
    /// are rendered explicitly from a scene's Tick(), so they never depend on URP camera ordering
    /// and they work both in play mode and from editor capture tools.
    /// </summary>
    public static class KaleidoRenderUtil
    {
        /// <remarks>Always carries a depth buffer: URP's render graph on Vulkan refuses camera targets
        /// without one ("Fake or uninitialized surface is not supported for attachment 0").</remarks>
        public static RenderTexture CreateRT(int width, int height, bool mips, string name, RenderTextureFormat format = RenderTextureFormat.ARGB32, int depthBits = 24)
        {
            var rt = new RenderTexture(width, height, depthBits, format)
            {
                name = name,
                depthStencilFormat = depthBits > 0 ? SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.DepthStencil) : UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                useMipMap = mips,
                autoGenerateMips = mips,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
            };
            rt.Create();
            Debug.Log($"[Kaleido] RT {name} {width}x{height} color={rt.graphicsFormat} depth={rt.depthStencilFormat} mips={mips}");
            return rt;
        }

        /// <summary>Render <paramref name="cam"/> into <paramref name="target"/> right now.</summary>
        public static void RenderCameraTo(Camera cam, RenderTexture target)
        {
            if (cam == null || target == null) return;
            var request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = target,
                mipLevel = 0,
                face = CubemapFace.Unknown,
                slice = 0,
            };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                var prev = cam.targetTexture;
                cam.targetTexture = target;
                cam.Render();
                cam.targetTexture = prev;
            }
        }

        /// <summary>Create a disabled helper camera that only renders <paramref name="cullingMask"/>.</summary>
        public static Camera CreateHelperCamera(string name, Transform parent, int cullingMask, Color clear, bool orthographic)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = cullingMask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.orthographic = orthographic;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.useOcclusionCulling = false;
            cam.depth = -50;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.allowXRRendering = false;      // helper cameras render mono into their own texture
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            return cam;
        }

        public static int LayerMaskFor(string layerName, int fallbackLayer)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) layer = fallbackLayer;
            return 1 << layer;
        }

        public static int LayerFor(string layerName, int fallbackLayer)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer < 0 ? fallbackLayer : layer;
        }

        public static Material NewMaterial(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[Kaleido] Missing shader " + shaderName);
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            var m = new Material(shader) { name = shaderName };
            return m;
        }

        public static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
