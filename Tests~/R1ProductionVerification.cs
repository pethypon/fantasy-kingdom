#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit production setup and wiring checks in the isolated validation project.</summary>
[InitializeOnLoad]
public static class R1ProductionVerification
{
    const string SceneKey = "FK.R1ProductionVerification.Scene";
    static bool clicked;
    static int frames;
    static R1ProductionVerification() { EditorApplication.update += Tick; }
    public static void InstallAndReview()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated clone only");
        R1ProductionContentTests.Run();
        ThreatClearProgressionTests.Run();
        R1ProductionAIRecruitmentTests.Run();
        TerrainEnvironmentProductionInstaller.Install();
        TerrainEnvironmentProductionInstaller.ValidateAssets();
        TerrainEnvironmentProductionInstaller.RenderPreview();
        Debug.Log("[R1ProductionVerification] EDITOR PASSED");
    }

    public static void RunScene()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated clone only");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "R1ProductionVerification";
        SessionState.SetBool(SceneKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(SceneKey, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                if (UIBuilder.ScreenCanvas == null || TitleScreenUI.Instance == null) return;
                var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) throw new Exception("Actual title button missing");
                UnityEngine.Random.InitState(56192); button.onClick.Invoke(); clicked = true; return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || !(turn.CurrentState is PlayerMove) || ++frames < 20) return;
            var systems = turn.Systems;
            if (!systems.MapCreate.Environment.GeneratedAtStart || systems.MapCreate.Environment.Count == 0)
                throw new Exception("Actual new game did not generate environment objects");
            Debug.Log("[R1ProductionVerification] ACTUAL START environmentCells=" + systems.MapCreate.Environment.Count);
            EnvironmentGenerationTests.PlayMode(systems);
            R1ProductionAIRecruitmentTests.PlayMode(systems);
            SpecialSkillTargetingTests.PlayMode(systems);
            R1AIOwnWallLegalityTests.PlayMode(systems);
            AIOperationLifetimeTests.PlayMode(systems);
            ThreatClearProgressionTests.Run();
            Debug.Log("[R1ProductionVerification] SCENE PASSED");
            SessionState.SetBool(SceneKey, false); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); SessionState.SetBool(SceneKey, false); EditorApplication.Exit(1); }
    }
}
#endif
