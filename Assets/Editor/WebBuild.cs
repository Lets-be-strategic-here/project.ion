using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ion.EditorTools
{
    /// <summary>
    /// Web (WebGL) player builds.
    ///
    /// Batch mode (exits non-zero on failure):
    ///   Unity -batchmode -nographics -quit -projectPath . -buildTarget WebGL -executeMethod Ion.EditorTools.WebBuild.Build -logFile -
    ///
    /// Optional command-line argument: -ionOutput &lt;dir&gt; (default Build/Web). GameCI's -customBuildPath
    /// is ignored on purpose so the output location is always predictable.
    /// </summary>
    public static class WebBuild
    {
        public const string DefaultOutput = "Build/Web";

        [MenuItem("Ion/Build Web", priority = 20)]
        public static void Build() => Run(BuildOptions.None);

        [MenuItem("Ion/Build Web (Development)", priority = 21)]
        public static void BuildDev() => Run(BuildOptions.Development);

        static void Run(BuildOptions options)
        {
            bool ok = false;
            try
            {
                ok = BuildInternal(options);
            }
            catch (Exception e)
            {
                Debug.LogError("[Ion] Web build threw: " + e);
            }

            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool BuildInternal(BuildOptions options)
        {
            ProjectSetup.EnsureConfigured();

            if (Type.GetType(ProjectSetup.BootstrapTypeName) == null)
            {
                Debug.LogError($"[Ion] {ProjectSetup.BootstrapTypeName} not found; the build would be an empty scene. Aborting.");
                return false;
            }
            if (!ProjectSetup.MainSceneHasBootstrap())
            {
                Debug.LogWarning("[Ion] Main.unity has no GameBootstrap component; regenerating the project setup.");
                ProjectSetup.RunSetup();
                if (!ProjectSetup.MainSceneHasBootstrap())
                {
                    Debug.LogError("[Ion] Main.unity still lacks GameBootstrap. Aborting.");
                    return false;
                }
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[Ion] Switching active build target to WebGL (pass -buildTarget WebGL to skip this).");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[Ion] Could not switch to WebGL. Is the Web Build Support module installed?");
                    return false;
                }
            }

            string output = GetArg("-ionOutput") ?? DefaultOutput;
            Directory.CreateDirectory(output);

            var bpo = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = options,
            };

            Debug.Log($"[Ion] Building Web player to '{output}' ({options}).");
            BuildReport report = BuildPipeline.BuildPlayer(bpo);
            BuildSummary s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Ion] Web build {s.result}: {s.totalErrors} error(s), {s.totalWarnings} warning(s).");
                return false;
            }

            Debug.Log($"[Ion] Web build succeeded: {s.totalSize / (1024f * 1024f):0.0} MB in {s.totalTime.TotalSeconds:0}s -> {Path.GetFullPath(output)}");
            return true;
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
