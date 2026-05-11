#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class QuestBuildTools
{
    private const string DevelopmentBuildPath = "build/quest/VRTemplate-quest-dev.apk";
    private const string SmokeTestBuildPath = "build/quest/VRTemplate-smoke-dev.apk";
    private const string SmokeTestScenePath = "Assets/Scenes/BasicScene.unity";

    [MenuItem("Tools/Quest/Build Development APK")]
    public static void BuildDevelopmentApk()
    {
        BuildApk(GetEnabledScenes(), DevelopmentBuildPath);
    }

    [MenuItem("Tools/Quest/Build Smoke Test APK")]
    public static void BuildSmokeTestApk()
    {
        BuildApk(new[] { SmokeTestScenePath }, SmokeTestBuildPath);
    }

    private static void BuildApk(string[] scenes, string outputPath)
    {
        if (scenes == null || scenes.Length == 0)
        {
            throw new InvalidOperationException("No scenes are available to build.");
        }

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.development = true;
        EditorUserBuildSettings.allowDebugging = true;
        EditorUserBuildSettings.connectProfiler = true;

        string fullOutputPath = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = fullOutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new Exception($"Quest build failed: {report.summary.result}");
        }

        Debug.Log($"[QuestBuildTools] Built APK: {fullOutputPath}");
    }

    private static string[] GetEnabledScenes()
    {
        return EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
    }
}
#endif
