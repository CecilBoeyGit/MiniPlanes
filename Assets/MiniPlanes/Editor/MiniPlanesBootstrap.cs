using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniPlanes.Editor
{
    /// <summary>Entry points for batchmode: -executeMethod MiniPlanes.Editor.MiniPlanesBootstrap.SetupAll / BuildAndroid.</summary>
    public static class MiniPlanesBootstrap
    {
        [MenuItem("MiniPlanes/0. Setup Everything")]
        public static void SetupAll()
        {
            MiniPlanesProjectSetup.Run();
            MiniPlanesSceneBuilder.Build();
            Debug.Log("[MiniPlanes] SetupAll finished.");
        }

        /// <summary>Headless convenience: regenerate settings and scene, then build the APK in one editor session.</summary>
        public static void SetupAndBuild()
        {
            SetupAll();
            BuildAndroid();
        }

        [MenuItem("MiniPlanes/3. Build Android APK")]
        public static void BuildAndroid()
        {
            string output = "Builds/MiniPlanes.apk";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MiniPlanesSceneBuilder.ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log("[MiniPlanes] Build " + summary.result + " -> " + summary.outputPath +
                      " (" + (summary.totalSize / (1024 * 1024)) + " MB, errors " + summary.totalErrors + ")");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
