using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Kaleido.Editor
{
    /// <summary>
    /// APK build entry point.
    /// Batch: Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod Kaleido.Editor.KaleidoBuild.BuildAndroid
    /// </summary>
    public static class KaleidoBuild
    {
        public const string OutputPath = "Builds/SpatialKaleidoscope.apk";

        [MenuItem("Kaleido/Build Android APK")]
        public static void BuildAndroid()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(KaleidoSceneBuilder.ScenePath, true) };
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            // KALEIDO_DEBUG=1 compiles the TCP debug channel used by the automated emulator loop
            bool debug = Environment.GetEnvironmentVariable("KALEIDO_DEBUG") == "1";
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            var list = new System.Collections.Generic.List<string>(defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            list.Remove("KALEIDO_DEBUG");
            if (debug) list.Add("KALEIDO_DEBUG");
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", list));
            KaleidoSceneBuilder.Build();            // scene always regenerated so the debug flag matches this build

            string version = Environment.GetEnvironmentVariable("KALEIDO_VERSION");
            if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
            if (int.TryParse(Environment.GetEnvironmentVariable("KALEIDO_VERSION_CODE"), out int code)) PlayerSettings.Android.bundleVersionCode = code;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { KaleidoSceneBuilder.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[Kaleido] Build {s.result}: {s.outputPath} size={s.totalSize / (1024 * 1024)} MB errors={s.totalErrors} warnings={s.totalWarnings} time={s.totalTime}");
            if (s.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception) Debug.LogError(step.name + ": " + msg.content);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw new Exception("Build failed");
            }
        }
    }
}
