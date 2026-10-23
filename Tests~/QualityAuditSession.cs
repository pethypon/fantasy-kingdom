#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runs native observations in a separate validation identity, without changing production rules.</summary>
[InitializeOnLoad]
public static class QualityAuditSession
{
    const string Key = "FK.QualityAuditSession";
    static readonly Stack<IEnumerator> pending = new Stack<IEnumerator>();
    static int clickedFrame = -1, steppedFrame = -1;
    static bool started;
    static GameSystems observedSystems;
    static string outputDirectory;
    static double began;
    static QualityAuditSession() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated validation clone required");
        SessionState.SetString(Key + ".Company", PlayerSettings.companyName);
        SessionState.SetString(Key + ".Product", PlayerSettings.productName);
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "QualityAudit_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (began == 0) began = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - began > 300) throw new TimeoutException("Quality observation exceeded five minutes");
            if (clickedFrame < 0)
            {
                var button = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) return;
                UnityEngine.Random.InitState(12345); button.onClick.Invoke(); clickedFrame = Time.frameCount; return;
            }
            if (!started)
            {
                var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
                if (turn == null || !(turn.CurrentState is PlayerMove) || Time.frameCount - clickedFrame < 20) return;
                outputDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("FK_QUALITY_OUTPUT")
                    ?? Path.Combine(Application.dataPath, "../../quality-audit-20261023"));
                Directory.CreateDirectory(outputDirectory);
                observedSystems = turn.Systems;
                pending.Push(UIQualityAudit.Run(turn.Systems, Path.Combine(outputDirectory, "ui")));
                started = true;
            }
            if (steppedFrame == Time.frameCount) return;
            steppedFrame = Time.frameCount;
            while (pending.Count > 0)
            {
                var current = pending.Peek();
                if (!current.MoveNext()) { pending.Pop(); continue; }
                if (current.Current is IEnumerator nested) { pending.Push(nested); continue; }
                return;
            }
            RoleBalanceAudit.Run(observedSystems, Path.Combine(outputDirectory, "roles"));
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
        SessionState.SetBool(Key, false);
        PlayerSettings.companyName = SessionState.GetString(Key + ".Company", "CodexValidation");
        PlayerSettings.productName = SessionState.GetString(Key + ".Product", "UnityValidation");
        Debug.Log("[QualityAuditSession] COMPLETE exit=" + code);
        EditorApplication.Exit(code);
    }
}
#endif
