#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
public static class ReleaseValidation
{
    public static void Run()
    {
        if(!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated validation project required");
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ReleaseValidationBuild/FantasyKingdom.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        EditorUserBuildSettings.development=false;
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/SampleScene.unity"}, locationPathName=output,
            target=BuildTarget.StandaloneWindows64, options=BuildOptions.None });
        Debug.Log($"[ReleaseValidation] result={report.summary.result} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} seconds={report.summary.totalTime.TotalSeconds:F1}");
        EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
    }
}
#endif
