using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Kaleido.Editor
{
    /// <summary>
    /// Generates the single resident scene (Assets/Kaleido/Scenes/Main.unity): XR Origin + camera,
    /// PXR_Manager, playback controller, input router, brightness governor, ground ring and the
    /// first-launch hint. Also registers the layers and enables PICO hand tracking.
    /// Menu: Kaleido > Build Main Scene. Batch: -executeMethod Kaleido.Editor.KaleidoSceneBuilder.Build
    /// </summary>
    public static class KaleidoSceneBuilder
    {
        public const string ScenePath = "Assets/Kaleido/Scenes/Main.unity";
        const string MaterialDir = "Assets/Kaleido/Materials";

        [MenuItem("Kaleido/Build Main Scene")]
        public static void Build()
        {
            EnsureLayers();
            ConfigurePicoProjectSettings();
            ConfigurePlayerSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- XR rig
            var originGO = new GameObject("XR Origin");
            var origin = originGO.AddComponent<XROrigin>();
            var offsetGO = new GameObject("Camera Offset");
            offsetGO.transform.SetParent(originGO.transform, false);
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            camGO.transform.SetParent(offsetGO.transform, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.fieldOfView = 90f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            int cellLayer = LayerMask.NameToLayer(KaleidoConfig.CellLayerName);
            int bulbLayer = LayerMask.NameToLayer(KaleidoConfig.BulbLayerName);
            cam.cullingMask = ~((1 << cellLayer) | (1 << bulbLayer));
            camGO.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderShadows = false;
            camData.requiresColorOption = CameraOverrideOption.Off;
            camData.requiresDepthOption = CameraOverrideOption.Off;

            var tpd = camGO.AddComponent<TrackedPoseDriver>();
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            var posAction = new InputAction("HeadPosition", InputActionType.Value, "<XRHMD>/centerEyePosition");
            posAction.AddBinding("<XRHMD>/devicePosition");
            var rotAction = new InputAction("HeadRotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
            rotAction.AddBinding("<XRHMD>/deviceRotation");
            tpd.positionInput = new InputActionProperty(posAction);
            tpd.rotationInput = new InputActionProperty(rotAction);

            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offsetGO;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 1.6f;
#if ENABLE_PICO_XR_SDK
            var pxr = originGO.AddComponent<PXR_Manager>();
            pxr.eyeTracking = false;            // PRD: no eye-track data collection; pinch comes from hand tracking
            pxr.screenFade = false;
            pxr.openMRC = false;
#endif

            // ---- app root
            var root = new GameObject("Kaleido");
            var domeRoot = new GameObject("DomeRoot").transform; domeRoot.SetParent(root.transform, false);
            var anchor = new GameObject("ObjectAnchor").transform; anchor.SetParent(root.transform, false);
            anchor.position = new Vector3(0f, 1.6f, KaleidoConfig.ObjectDistance);
            anchor.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            var input = root.AddComponent<KaleidoInput>();
            input.EnableKeyboardMouse = false;     // keyboard/mouse only in the editor (KaleidoInput enables it there)
            input.Head = cam;
            input.TrackingSpace = offsetGO.transform;
            var buttonsGO = new GameObject("NavButtons");
            buttonsGO.transform.SetParent(root.transform, false);
            var buttons = buttonsGO.AddComponent<NavButtons>();
            var raysGO = new GameObject("PointerRays");
            raysGO.transform.SetParent(root.transform, false);
            var rays = raysGO.AddComponent<PointerRayVisual>();
            var governor = root.AddComponent<BrightnessGovernor>();
            var tilt = root.AddComponent<HeadTilt>();
            tilt.Source = cam;
            var controller = root.AddComponent<PlaybackController>();
            controller.MainCamera = cam;
            controller.Input = input;
            controller.Governor = governor;
            controller.HeadTilt = tilt;
            controller.DomeRoot = domeRoot;
            controller.ObjectAnchor = anchor;
            controller.Buttons = buttons;
            controller.Rays = rays;
            // dev-only TCP command channel; enabled only when the scene is built with KALEIDO_DEBUG=1
            var dbg = root.AddComponent<KaleidoDebugServer>();
            dbg.Controller = controller;
            dbg.RigRoot = originGO.transform;
            dbg.enabled = System.Environment.GetEnvironmentVariable("KALEIDO_DEBUG") == "1";
            Debug.Log("[Kaleido] debug server enabled=" + dbg.enabled);

            // ---- ground ring (static visual reference)
            Directory.CreateDirectory(MaterialDir);
            var ringMat = LoadOrCreateMaterial("Kaleido/GroundRing", MaterialDir + "/GroundRing.mat");
            var ringMesh = MeshUtil.Ring(1.1f, 1.45f);
            var ringMeshPath = "Assets/Kaleido/Materials/GroundRing.asset";
            AssetDatabase.CreateAsset(ringMesh, ringMeshPath);
            var ringGO = new GameObject("GroundRing");
            ringGO.transform.SetParent(root.transform, false);
            ringGO.transform.position = new Vector3(0f, 0.01f, 0f);
            ringGO.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ringMeshPath);
            var ringMr = ringGO.AddComponent<MeshRenderer>();
            ringMr.sharedMaterial = ringMat;
            ringMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringMr.receiveShadows = false;

            // ---- first-launch hint (3 s), world-locked in front of the initial view
            var hint = new GameObject("FirstLaunchHint");
            hint.transform.SetParent(root.transform, false);
            hint.transform.position = new Vector3(0f, 1.45f, 1.6f);
            hint.transform.rotation = Quaternion.identity;
            var tm = hint.AddComponent<TextMesh>();
            tm.text = "Trigger / pinch : next     Grip : previous\nA B X Y / hold pinch 1 s : pause\nLook down : < previous   next >";
            tm.fontSize = 48;
            tm.characterSize = 0.012f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.85f, 0.88f, 0.95f, 1f) * KaleidoConfig.OutputBrightnessCap;   // legacy text shader bypasses the governor
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { tm.font = font; hint.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            hint.SetActive(false);
            controller.FirstLaunchHint = hint;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Kaleido] Main scene built at " + ScenePath);
        }

        static Material LoadOrCreateMaterial(string shaderName, string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            SetLayer(layers, 30, KaleidoConfig.CellLayerName);
            SetLayer(layers, 31, KaleidoConfig.BulbLayerName);
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue != name) p.stringValue = name;
        }

        static void ConfigurePicoProjectSettings()
        {
#if ENABLE_PICO_XR_SDK
            var cfg = PXR_ProjectSetting.GetProjectConfig();
            if (cfg != null)
            {
                cfg.handTracking = true;          // eye-hand pinch "Select" comes from the system hand tracker
                cfg.eyeTracking = false;          // no eye data collection (PRD section 2)
                cfg.openMRC = false;
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
                Debug.Log("[Kaleido] PXR_ProjectSetting: handTracking=on, eyeTracking=off");
            }
#endif
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.productName = "SpatialKaleidoscope";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.kejing.spatialkaleidoscope");
            PlayerSettings.Android.startInFullscreen = true;
            QualitySettings.vSyncCount = 0;
        }
    }
}
