using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kaleido.Editor
{
    /// <summary>Headless logic checks that need no device: button placement + hit testing, click routing.</summary>
    public static class KaleidoSelfTest
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(KaleidoSceneBuilder.ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            controller.IsEditorCapture = true;
            controller.Initialize();
            controller.Begin();
            var cam = controller.MainCamera;
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            cam.transform.rotation = Quaternion.identity;
            for (int i = 0; i < 90; i++) controller.Step(1f / 60f, i / 60.0);   // start fade completes, anchor + buttons placed

            var buttons = controller.Buttons;
            Vector3 eye = cam.transform.position;
            Vector3 prevPos = buttons.transform.Find("NavPrev").position;
            Vector3 nextPos = buttons.transform.Find("NavNext").position;
            int hPrev = buttons.HitTest(new Ray(eye, (prevPos - eye).normalized));
            int hNext = buttons.HitTest(new Ray(eye, (nextPos - eye).normalized));
            int hFwd = buttons.HitTest(new Ray(eye, Vector3.forward));
            int hMiss = buttons.HitTest(new Ray(eye, (nextPos - eye + Vector3.right * 0.5f).normalized));
            Debug.Log($"[SelfTest] placed={buttons.Placed} prev@{prevPos:F2} next@{nextPos:F2} hitPrev={hPrev} hitNext={hNext} hitForward={hFwd} hitMissRight={hMiss}");
            bool ok = buttons.Placed && hPrev == 0 && hNext == 1 && hFwd == -1 && hMiss == -1;

            // click routing: look at the previous button and "click" -> index goes 0 -> 11
            int before = controller.CurrentIndex;
            cam.transform.rotation = Quaternion.LookRotation((prevPos - eye).normalized, Vector3.up);
            controller.RequestStep(buttons.HitTest(new Ray(eye, cam.transform.forward)) == 0 ? -1 : +1);
            for (int i = 0; i < 60; i++) controller.Step(1f / 60f, 2.0 + i / 60.0);
            int afterPrev = controller.CurrentIndex;
            cam.transform.rotation = Quaternion.identity;
            controller.RequestStep(buttons.HitTest(new Ray(eye, cam.transform.forward)) == 0 ? -1 : +1);
            for (int i = 0; i < 60; i++) controller.Step(1f / 60f, 3.0 + i / 60.0);
            int afterNext = controller.CurrentIndex;
            Debug.Log($"[SelfTest] index before={before + 1} afterPrevClick={afterPrev + 1} afterPlainClick={afterNext + 1}");
            ok &= afterPrev == (before + 11) % 12 && afterNext == before;
            Debug.Log(ok ? "[SelfTest] PASS" : "[SelfTest] FAIL");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!ok && Application.isBatchMode) EditorApplication.Exit(2);
        }
    }
}
