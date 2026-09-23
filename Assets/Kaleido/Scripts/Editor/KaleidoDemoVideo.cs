using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kaleido.Editor
{
    /// <summary>
    /// Renders the showcase demo as JPEG frame sequences (one folder per playlist item) through the same
    /// runtime code path as the app (PlaybackController.Step + scene Tick), so a smooth 30 fps tour of all
    /// scenes can be assembled with ffmpeg without a headset or the emulator (whose display idles at 1 Hz).
    /// Each scene evolves for KALEIDO_DEMO_SETTLE seconds first (per-scene overrides in ShotFor), then
    /// KALEIDO_DEMO_SECONDS are rendered with a slow head move; crossfades between scenes are added in post
    /// (tools/assemble_demo.sh: ffmpeg xfade, 0.6 s like the app).
    /// Env: KALEIDO_DEMO_OUT (default Captures/demo) · KALEIDO_DEMO_SECONDS (4.2) · KALEIDO_DEMO_FPS (30)
    ///      KALEIDO_DEMO_SIZE (1080) · KALEIDO_DEMO_SETTLE (20) · KALEIDO_DEMO_SCENES ("" = all, or "1,6,10")
    /// Batch: -executeMethod Kaleido.Editor.KaleidoDemoVideo.Run
    /// </summary>
    public static class KaleidoDemoVideo
    {
        struct Shot
        {
            public Vector3 PosA, PosB;        // head position start / end
            public Vector3 EulerA, EulerB;    // dome scenes: head rotation start / end (x < 0 looks up)
            public bool LookAtObject;         // object scenes: always look at the object anchor
            public float Fov;
            public float Settle;              // evolution seconds before the first frame (0 = KALEIDO_DEMO_SETTLE)
        }

        static Shot WithSettle(Shot s, float settle) { s.Settle = settle; return s; }   // per-scene override, e.g. WithSettle(Dome(...), 30f)

        static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);

        static Shot Dome(float pitchA, float yawA, float pitchB, float yawB, float fov = 90f) => new Shot
        {
            PosA = Eye, PosB = Eye, EulerA = new Vector3(pitchA, yawA, 0f), EulerB = new Vector3(pitchB, yawB, 0f), Fov = fov,
        };

        /// <summary>Orbit around the object (2 m ahead of the start pose) from angle a to b (degrees, 0 = in front) at radius r.</summary>
        static Shot Orbit(Vector3 target, float a, float b, float r, float dy = 0f, float fov = 70f)
        {
            Vector3 P(float deg)
            {
                float rad = deg * Mathf.Deg2Rad;
                return target + new Vector3(Mathf.Sin(rad) * r, dy, -Mathf.Cos(rad) * r);
            }
            return new Shot { PosA = P(a), PosB = P(b), LookAtObject = true, Fov = fov };
        }

        static Shot ShotFor(int index, SceneKind kind, Vector3 target)
        {
            switch (index)
            {
                case 0: return Dome(-15f, -10f, -50f, 10f);          // 1 two-mirror dark beads: tilt up toward the zenith
                case 1: return Dome(-30f, 15f, -60f, -15f);          // 2 two-mirror hex glass
                case 2: return Dome(-40f, 0f, -75f, 20f);            // 3 procedural: toward the zenith centre
                case 3: return Dome(-60f, 0f, -85f, 30f);            // 4 mandala: roll around the zenith
                case 4: return Dome(-20f, -20f, -45f, 20f);          // 5 Julia (20 s in has more filigree than 50 s)
                case 5: return Orbit(target, -25f, 20f, 1.9f);        // 6 Mandelbulb: slow orbit (1.9 m keeps it whole in frame)
                case 6: return Orbit(target, 20f, -30f, 1.9f, 0.1f);  // 7 fractal tree
                case 7: return Orbit(target, -70f, -20f, 1.8f);       // 8 Lorenz: from the side toward the front
                case 8: return Dome(-30f, 0f, -70f, -25f);           // 9 phyllotaxis
                default: return Dome(-25f, 10f, -60f, -10f);         // 10 Mandelbrot (20 s in shows bold spirals; 34 s is plainer)
            }
        }

        static string Env(string name, string fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        static float EnvF(string name, float fallback)
        {
            return float.TryParse(Env(name, ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;
        }

        [MenuItem("Kaleido/Render Demo Frames")]
        public static void Run()
        {
            string outDir = Env("KALEIDO_DEMO_OUT", "Captures/demo");
            float seconds = EnvF("KALEIDO_DEMO_SECONDS", 4.2f);
            int fps = Mathf.Max(1, (int)EnvF("KALEIDO_DEMO_FPS", 30f));
            int size = Mathf.Max(64, (int)EnvF("KALEIDO_DEMO_SIZE", 1080f));
            float settle = EnvF("KALEIDO_DEMO_SETTLE", 20f);
            string only = Env("KALEIDO_DEMO_SCENES", "");

            if (!File.Exists(KaleidoSceneBuilder.ScenePath)) KaleidoSceneBuilder.Build();
            EditorSceneManager.OpenScene(KaleidoSceneBuilder.ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<PlaybackController>();
            if (controller == null) { Debug.LogError("[Kaleido] PlaybackController not found"); return; }
            controller.IsEditorCapture = true;
            controller.Initialize();
            controller.Begin();
            var cam = controller.MainCamera;
            // The object anchor is placed 2 m in front of the head once tracking is valid, or after a 1 s fallback
            // wait without XR: hold the canonical pose for 1.25 s so it lands at (0, 1.6, 2) before any scene is shot.
            cam.transform.position = Eye;
            cam.transform.rotation = Quaternion.identity;
            cam.fieldOfView = 90f;
            for (int s = 0; s < 25; s++) controller.Step(0.05f, s * 0.05);
            Vector3 target = controller.ObjectAnchor != null ? controller.ObjectAnchor.position : Eye + Vector3.forward * KaleidoConfig.ObjectDistance;
            Debug.Log($"[Kaleido] demo object anchor at {target}");

            Directory.CreateDirectory(outDir);
            var rt = KaleidoRenderUtil.CreateRT(size, size, false, "DemoRT");
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            int count = controller.Playlist.Length;
            int frames = Mathf.Max(1, Mathf.RoundToInt(seconds * fps));
            float dt = 1f / fps;
            var wanted = only.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                if (wanted.Length > 0 && Array.IndexOf(wanted, (i + 1).ToString()) < 0) continue;
                var def = controller.Playlist[i];
                string dir = Path.Combine(outDir, $"scene{i + 1:00}");
                Directory.CreateDirectory(dir);
                controller.JumpTo(i);
                var shot = ShotFor(i, def.Kind, target);
                // evolve (physics / growth / trails / zoom) before the first frame; dt is clamped to 100 ms per step
                double t = 1.0;
                int settleSteps = Mathf.RoundToInt((shot.Settle > 0f ? shot.Settle : settle) / 0.05f);
                cam.transform.position = shot.PosA;
                cam.transform.rotation = shot.LookAtObject ? Quaternion.LookRotation(target - shot.PosA, Vector3.up) : Quaternion.Euler(shot.EulerA);
                cam.fieldOfView = shot.Fov;
                for (int s = 0; s < settleSteps; s++) { controller.Step(0.05f, t); t += 0.05; }
                for (int f = 0; f < frames; f++)
                {
                    float u = frames > 1 ? (float)f / (frames - 1) : 0f;
                    float e = u * u * (3f - 2f * u);   // smoothstep: ease in and out of the head move
                    var pos = Vector3.Lerp(shot.PosA, shot.PosB, e);
                    cam.transform.position = pos;
                    cam.transform.rotation = shot.LookAtObject
                        ? Quaternion.LookRotation(target - pos, Vector3.up)
                        : Quaternion.Slerp(Quaternion.Euler(shot.EulerA), Quaternion.Euler(shot.EulerB), e);
                    cam.fieldOfView = shot.Fov;
                    controller.Step(dt, t); t += dt;
                    KaleidoRenderUtil.RenderCameraTo(cam, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, $"f{f:0000}.jpg"), tex.EncodeToJPG(92));
                }
                Debug.Log($"[Kaleido] demo scene {i + 1} {def.Title}: {frames} frames -> {dir} ({sw.Elapsed.TotalSeconds:0}s)");
            }
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[Kaleido] demo frames complete -> " + Path.GetFullPath(outDir));
        }
    }
}
