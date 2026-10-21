#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class BoardFeedbackLiveValidation
{
    const string Key = "FK.BoardFeedbackLiveValidation";
    static bool clicked;
    static int startedFrame;
    static BoardFeedbackLiveValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated clone only");
        BoardFeedbackTests.Assets();
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "BoardFeedbackLiveValidation20261019";
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
            BoardFeedbackTests.PlayMode(turn.Systems);
            BoardActorAnimationTests.PlayMode(turn.Systems);
            BoardFeedbackPreview.Capture(turn.Systems);
            Debug.Log("[BoardFeedbackLiveValidation] ALL PASSED");
            SessionState.SetBool(Key, false); EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }
}
#endif
