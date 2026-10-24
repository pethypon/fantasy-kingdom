#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AIR2LiveValidation
{
    const string Key = "FK.R2LiveValidation";
    static bool clicked;
    static int frame, failed;
    static readonly System.Collections.Generic.List<string> results = new System.Collections.Generic.List<string>();
    static AIR2LiveValidation() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated clone required");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "AIR2_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key, true); EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Stage(string name, Action run)
    {
        try { run(); results.Add("PASS " + name); Debug.Log("[AIR2Stage] PASS " + name); }
        catch (Exception e) { failed++; results.Add("FAIL " + name + " " + (e.InnerException ?? e).Message); Debug.Log("[AIR2Stage] " + results[results.Count - 1]); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (!clicked)
        {
            var b = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
            if (b == null) return; UnityEngine.Random.InitState(12345); b.onClick.Invoke(); clicked = true; frame = Time.frameCount; return;
        }
        var t = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
        if (t == null || !(t.CurrentState is PlayerMove) || Time.frameCount - frame < 20) return;
        SessionState.SetBool(Key, false);
        Stage("command", () => AIR2CommandTests.Run(t.Systems, t));
        Stage("orientation", () => AIOrientationReconTests.Run(t.Systems, t));
        foreach (string suite in new[] { "AIR2ObjectiveTests", "AIR2RecruitmentTests", "AIR2ExplorationTests" })
        {
            var type = System.Type.GetType(suite + ", Assembly-CSharp-Editor");
            if (type == null) { results.Add("UNVERIFIED " + suite); continue; }
            var method = type.GetMethod("Run");
            Stage(suite, () => method.Invoke(null, method.GetParameters().Length == 2 ? new object[] { t.Systems, t } : new object[] { t.Systems }));
        }
        string output = Environment.GetEnvironmentVariable("FK_R2_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(output)) File.WriteAllLines(output, results);
        Debug.Log("[AIR2LiveValidation] COMPLETE failures=" + failed); EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
#endif
