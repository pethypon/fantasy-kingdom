#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class BarrierLiveValidation
{
    const string Key = "FK.BarrierLiveValidation";
    static bool clicked;
    static int start;
    static BarrierLiveValidation() => EditorApplication.update += Tick;
    public static void Prepare()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated clone only");
        BarrierCombatInstaller.InstallTrial();
        DarkEnemyVisualInstaller.Install(); BoardVisualThemeInstaller.Install();
        R1UnitVisualInstaller.ValidateAssets(); R1ProductionContentTests.Run();
        AuthoringEditorTests.EditMode(); R1ProductionAIRecruitmentTests.Run();
    }
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated clone only");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "BarrierLiveValidation20261020";
        SessionState.SetBool(Key, true); EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                if (UIBuilder.ScreenCanvas == null || TitleScreenUI.Instance == null) return;
                var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) throw new Exception("Actual title button missing");
                UnityEngine.Random.InitState(56192); button.onClick.Invoke(); clicked = true; start = Time.frameCount; return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || !(turn.CurrentState is PlayerMove) || Time.frameCount - start < 20) return;
            BarrierSystemTests.Run(turn.Systems);
            BarrierAITests.Run(turn.Systems);
            ProtectedTargetAITests.Run(turn.Systems);
            BasicResourceEconomyTests.PlayMode(turn.Systems);
            AIKnownBaseTests.PlayMode(turn.Systems);
            var config = ScriptableObject.CreateInstance<BarrierCombatSettings>(); config.PopulateTrialValues(); config.enabledForPlay = true;
            try { using (BarrierCombatSettings.UseForValidation(config)) StatusUIRegressionTests.Run(turn.Systems, turn); }
            finally { UnityEngine.Object.DestroyImmediate(config); }
            AssassinAbilityTests.PlayMode(turn.Systems);
            R1ProductionAIRecruitmentTests.PlayMode(turn.Systems);
            Debug.Log("[BarrierLiveValidation] ALL PASSED (temporary rule fixture; production activation unchanged)");
            SessionState.SetBool(Key, false); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }
}
#endif
