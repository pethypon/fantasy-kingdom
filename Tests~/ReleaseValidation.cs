#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
[InitializeOnLoad]
public static class ReleaseValidation
{
    const string IdleKey = "FK.ReleaseValidation.Idle.";
    const double IdleSeconds = 3;

    static ReleaseValidation() { EditorApplication.update += TickPendingExit; }

    // Keep the original immediate-shutdown control for comparisons.
    public static void Run() => BuildAndExit(false);

    // Omit -quit: the editor update callback below owns shutdown after BuildPlayer returns.
    public static void RunGraceful() => BuildAndExit(true);
    public static void RunWithIdle() => RunGraceful();

    static void BuildAndExit(bool waitForEditorUpdates)
    {
        if(!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated validation project required");
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Build validation requires edit mode");
        SessionState.SetBool(IdleKey + "Pending", false);
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ReleaseValidationBuild/FantasyKingdom.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        EditorUserBuildSettings.development=false;
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/SampleScene.unity"}, locationPathName=output,
            target=BuildTarget.StandaloneWindows64, options=BuildOptions.None });
        string mode = waitForEditorUpdates ? "editor-idle" : "immediate";
        int exitCode = report.summary.result==BuildResult.Succeeded?0:1;
        Debug.Log($"[ReleaseValidation] result={report.summary.result} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} seconds={report.summary.totalTime.TotalSeconds:F1} mode={mode} requestedIdleSeconds={(waitForEditorUpdates ? IdleSeconds : 0):F1}");
        if (!waitForEditorUpdates) { EditorApplication.Exit(exitCode); return; }

        // SessionState preserves the exit request if an editor domain reload occurs during idle.
        // Let ordinary editor updates run; no Play, sleep, forced GC, or log filtering.
        SessionState.SetString(IdleKey + "Started", EditorApplication.timeSinceStartup.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetInt(IdleKey + "Updates", 0);
        SessionState.SetInt(IdleKey + "ExitCode", exitCode);
        SessionState.SetBool(IdleKey + "Pending", true);
    }

    static void TickPendingExit()
    {
        if (!SessionState.GetBool(IdleKey + "Pending", false)) return;
        int updates = SessionState.GetInt(IdleKey + "Updates", 0) + 1;
        SessionState.SetInt(IdleKey + "Updates", updates);
        double started = double.Parse(SessionState.GetString(IdleKey + "Started", "0"), System.Globalization.CultureInfo.InvariantCulture);
        double elapsed = EditorApplication.timeSinceStartup - started;
        if (elapsed < IdleSeconds) return;
        SessionState.SetBool(IdleKey + "Pending", false);
        Debug.Log($"[ReleaseValidation] idle_completed mode=editor-idle requestedIdleSeconds={IdleSeconds:F1} actualIdleSeconds={elapsed:F3} editorUpdates={updates}");
        EditorApplication.Exit(SessionState.GetInt(IdleKey + "ExitCode", 1));
    }
}
#endif
