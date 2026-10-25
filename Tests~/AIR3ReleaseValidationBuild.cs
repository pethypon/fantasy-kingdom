#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds saved content as-is in the disposable validation project.</summary>
public static class AIR3ReleaseValidationBuild
{
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated clone required");
        string output = Environment.GetEnvironmentVariable("FK_R3_BUILD_OUTPUT");
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output))
            throw new InvalidOperationException("Absolute FK_R3_BUILD_OUTPUT is required");
        Directory.CreateDirectory(output);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = Path.Combine(output, "FantasyKingdom.exe"),
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        string summary = "result=" + report.summary.result + " errors=" + report.summary.totalErrors
            + " warnings=" + report.summary.totalWarnings + " seconds=" + report.summary.totalTime.TotalSeconds;
        File.WriteAllText(Path.Combine(output, "build-result.txt"), summary);
        Debug.Log("[AIR3ReleaseValidationBuild] " + summary);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
#endif
