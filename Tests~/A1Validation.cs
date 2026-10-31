#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class A1CoreContracts
{
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation") throw new InvalidOperationException("Isolated clone required");
        AITurnBudget.Begin(5000); A1HousingTests.Run();
        AITurnBudget.Begin(5000); A1CoordinationTests.Run();
        AITurnBudget.Begin(5000); A1RetreatTests.Run();
        Debug.Log("[A1Core] COMPLETE");
    }
}

[InitializeOnLoad]
public static class A1LiveValidation
{
    const string Key = "FK.A1Live";
    static bool clicked; static int frame, failures;
    static A1LiveValidation() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation") throw new InvalidOperationException("Isolated clone required");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "A1_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key,true); EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Stage(string name, Action run)
    {
        try { AITurnBudget.Begin(5000); run(); Debug.Log("[A1Live] PASS " + name); }
        catch (Exception e) { failures++; Debug.LogError("[A1Live] FAIL " + name + " " + e); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (!clicked)
        {
            var button = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
            if (button == null) return;
            UnityEngine.Random.InitState(12345); button.onClick.Invoke(); clicked = true; frame = Time.frameCount; return;
        }
        var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
        if (turn == null || !(turn.CurrentState is PlayerMove) || Time.frameCount - frame < 20) return;
        SessionState.SetBool(Key,false);
        Stage("recon",()=>A1ReconTests.Run(turn.Systems));
        Stage("housing",()=>A1HousingTests.Run(turn.Systems));
        Stage("retreat",()=>A1RetreatTests.PlayMode(turn.Systems));
        Stage("engineer-production",EngineerProductionAuthoring.ValidateInstalled);
        Stage("engineer-AI",()=>R2EngineerAIContracts.Run(turn.Systems));
        Debug.Log("[A1Live] COMPLETE failures=" + failures); EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
#endif
