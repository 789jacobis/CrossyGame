#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildAutomation
{
    public static void BuildWebProduction()
    {
        BuildWeb("Release", BuildOptions.None);
    }

    public static void BuildWebDevelopment()
    {
        BuildWeb(
            "Development",
            BuildOptions.Development | BuildOptions.AllowDebugging);
    }

    private static void BuildWeb(
        string outputDirectory,
        BuildOptions options)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException(
                "No enabled scenes are configured for the Web build.");
        }

        string outputPath = Path.GetFullPath(
            Path.Combine(
                Application.dataPath,
                "..",
                "Builds",
                outputDirectory));
        Directory.CreateDirectory(outputPath);

        BuildReport report = BuildPipeline.BuildPlayer(
            new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = options
            });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Web build failed: {report.summary.result}");
        }

        Console.WriteLine(
            $"Web build succeeded: {report.summary.totalSize} bytes");
    }
}
#endif
