#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AIClosingLiveValidation
{
    const string Key = "FK.AIClosingLiveValidation";
    static bool clicked;
    static int startedFrame;
    static AIClosingLiveValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated clone only");
        ThreatClearProgressionTests.Run();
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "AIClosingLiveValidation20261019";
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
                UnityEngine.Random.InitState(56192); button.onClick.Invoke(); clicked = true; startedFrame = Time.frameCount; return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || !(turn.CurrentState is PlayerMove) || Time.frameCount - startedFrame < 20) return;
            AIClosingTests.PlayMode(turn.Systems);
            AIClosingObjectiveTests.Run(turn.Systems, turn);
            AIClosingAdvanceTests.PlayMode(turn.Systems);
            System.Type.GetType("AICommanderSafetyTests, Assembly-CSharp-Editor", true).GetMethod("PlayMode").Invoke(null, new object[] { turn.Systems });
            AIEvolutionTests.Run(turn.Systems, turn);
            AssassinAbilityTests.PlayMode(turn.Systems);
            ThreatClearProgressionTests.PlayMode(turn.Systems, turn);
            Debug.Log("[AIClosingLiveValidation] ALL PASSED");
            SessionState.SetBool(Key, false); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }
}
#endif
