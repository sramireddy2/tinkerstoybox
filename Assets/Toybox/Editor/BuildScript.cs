using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Toybox.EditorTools
{
    /// <summary>
    /// Command-line WebGL build:
    ///   Unity -batchmode -quit -executeMethod Toybox.EditorTools.BuildScript.BuildWebGL
    ///         -toyboxOutput &lt;dir&gt; [-toyboxOpt BuildTimes|RuntimeSpeed|DiskSize] [-toyboxDev]
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("Toybox/Build WebGL")]
        public static void BuildWebGL()
        {
            string output = ToyboxArgs.Get("-toyboxOutput", "Build/WebGL");
            string optimization = ToyboxArgs.Get("-toyboxOpt", "BuildTimes");
            bool development = ToyboxArgs.Has("-toyboxDev");

            if (!File.Exists(ProjectSetup.ScenePath)) ProjectSetup.Run();

            EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", optimization);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Toybox] Build {summary.result}: {summary.totalSize / (1024f * 1024f):F1} MB in " +
                      $"{summary.totalTime.TotalSeconds:F0}s, {summary.totalErrors} errors, output={output}");

            if (summary.result != BuildResult.Succeeded)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw new Exception("WebGL build failed: " + summary.result);
            }
        }
    }
}
