using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kaleido.Editor
{
    /// <summary>
    /// Multi-angle verification captures of every playlist item, rendered in edit mode through
    /// the same runtime code path as the app (PlaybackController.Step + scene Tick).
    /// Output: Captures/scene{NN}_{view}.png
    /// Batch: -executeMethod Kaleido.Editor.KaleidoCapture.RunAll
    /// </summary>
    public static class KaleidoCapture
    {
        public const string OutputDir = "Captures";
        const int Size = 1024;

        struct View { public string Name; public Vector3 Pos; public Vector3 Euler; public float Fov; }

        static readonly View[] DomeViews =
        {
            new View { Name = "zenith",    Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(-90, 0, 0), Fov = 100 },
            new View { Name = "up45",      Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(-45, 0, 0), Fov = 90 },
            new View { Name = "forward",   Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(0, 0, 0), Fov = 90 },
            new View { Name = "left60up",  Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(-30, -60, 0), Fov = 90 },
            new View { Name = "right60up", Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(-30, 60, 0), Fov = 90 },
            new View { Name = "down",      Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(60, 0, 0), Fov = 90 },
        };

        static readonly View[] ObjectViews =
        {
            new View { Name = "front",     Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(0, 0, 0), Fov = 70 },
            new View { Name = "close",     Pos = new Vector3(0, 1.6f, 0.9f), Euler = new Vector3(0, 0, 0), Fov = 70 },
            new View { Name = "leftside",  Pos = new Vector3(-1.6f, 1.6f, 2.0f), Euler = new Vector3(0, 90, 0), Fov = 70 },
            new View { Name = "rightside", Pos = new Vector3(1.6f, 1.6f, 2.0f), Euler = new Vector3(0, -90, 0), Fov = 70 },
            new View { Name = "above",     Pos = new Vector3(0, 2.6f, 1.2f), Euler = new Vector3(50, 0, 0), Fov = 70 },
            new View { Name = "zenith",    Pos = new Vector3(0, 1.6f, 0), Euler = new Vector3(-90, 0, 0), Fov = 100 },
        };

        [MenuItem("Kaleido/Capture All Scenes")]
        public static void RunAll()
        {
            Run(-1);
        }

        /// <summary>Capture every crossfade half-way (t = 0.3 s of 0.6 s): proves there is no black frame.</summary>
        [MenuItem("Kaleido/Capture Transitions")]
        public static void RunTransitions()
        {
            if (!File.Exists(KaleidoSceneBuilder.ScenePath)) KaleidoSceneBuilder.Build();
            EditorSceneManager.OpenScene(KaleidoSceneBuilder.ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            controller.IsEditorCapture = true;
            controller.Initialize();
            controller.Begin();
            var cam = controller.MainCamera;
            Directory.CreateDirectory(OutputDir);
            var rt = KaleidoRenderUtil.CreateRT(Size, Size, false, "CaptureRT");
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            int count = controller.Playlist.Length;
            for (int i = 0; i < count; i++)
            {
                controller.JumpTo(i);
                for (int s = 0; s < 120; s++) controller.Step(0.05f, s * 0.05);
                controller.RequestStep(+1);                       // start crossfade to i+1 (12 -> 1 included)
                for (int s = 0; s < 6; s++) controller.Step(0.05f, 6.0 + s * 0.05);   // 0.3 s = half way
                cam.transform.position = new Vector3(0, 1.6f, 0);
                cam.transform.rotation = Quaternion.Euler(-40, 0, 0);
                cam.fieldOfView = 95;
                controller.Step(1f / 72f, 6.4);
                KaleidoRenderUtil.RenderCameraTo(cam, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(OutputDir, $"transition{i + 1:00}_to_{(i + 1) % count + 1:00}.png"), tex.EncodeToPNG());
                for (int s = 0; s < 10; s++) controller.Step(0.05f, 7.0 + s * 0.05);   // finish the crossfade
            }
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[Kaleido] transition captures complete");
        }

        public static void Run(int onlyIndex)
        {
            if (!File.Exists(KaleidoSceneBuilder.ScenePath)) KaleidoSceneBuilder.Build();
            EditorSceneManager.OpenScene(KaleidoSceneBuilder.ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            if (controller == null) { Debug.LogError("[Kaleido] PlaybackController not found"); return; }
            controller.IsEditorCapture = true;
            controller.Initialize();
            controller.Begin();
            var cam = controller.MainCamera;
            Directory.CreateDirectory(OutputDir);
            var rt = KaleidoRenderUtil.CreateRT(Size, Size, false, "CaptureRT");
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            int count = controller.Playlist.Length;
            for (int i = 0; i < count; i++)
            {
                if (onlyIndex >= 0 && i != onlyIndex) continue;
                controller.JumpTo(i);
                // let physics / growth / trails evolve for a while (evolution clamps dt to 50 ms per step)
                int settleSteps = 400;
                for (int s = 0; s < settleSteps; s++) controller.Step(0.05f, s * 0.05);
                var def = controller.Playlist[i];
                var views = def.Kind == SceneKind.Object ? ObjectViews : DomeViews;
                foreach (var v in views)
                {
                    cam.transform.position = v.Pos;
                    cam.transform.rotation = Quaternion.Euler(v.Euler);
                    cam.fieldOfView = v.Fov;
                    // one more step so helper cameras (mandelbulb) see the final head pose
                    controller.Step(1f / 72f, settleSteps * 0.05 + 1.0);
                    KaleidoRenderUtil.RenderCameraTo(cam, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string file = Path.Combine(OutputDir, $"scene{i + 1:00}_{def.Id}_{v.Name}.png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                }
                Debug.Log($"[Kaleido] captured scene {i + 1} {def.Title}");
            }
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[Kaleido] capture complete -> " + Path.GetFullPath(OutputDir));
        }
    }
}
