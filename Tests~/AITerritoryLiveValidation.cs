#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class AITerritoryLiveValidation
{
    const string Key = "FK.TerritoryLiveValidation";
    static bool clicked;
    static int clickedFrame;
    static int failedStages;
    static void Stage(string name, Action action)
    {
        try { action(); Debug.Log("[AITerritoryStage] PASS " + name); }
        catch (Exception error) { failedStages++; Debug.LogException(error.InnerException ?? error); }
    }
    static AITerritoryLiveValidation() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated validation project required");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "TerritoryReproduction_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key,true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                var button=UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if(button==null)return;
                UnityEngine.Random.InitState(12345);button.onClick.Invoke();clicked=true;clickedFrame=Time.frameCount;return;
            }
            var turn=UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if(turn==null || !(turn.CurrentState is PlayerMove) || Time.frameCount-clickedFrame<20)return;
            string output=Path.GetFullPath(Environment.GetEnvironmentVariable("FK_TERRITORY_OUTPUT")??Path.Combine(Application.dataPath,"../../territory-native"));
            AITerritoryAdvanceValidation.Run(turn.Systems,output);
            if (Environment.GetEnvironmentVariable("FK_TERRITORY_EXTENDED") == "1")
            {
                Stage("intel", () => System.Type.GetType("AITerritoryIntelTests, Assembly-CSharp-Editor",true).GetMethod("Run").Invoke(null,new object[]{turn.Systems}));
                Stage("safety", () => System.Type.GetType("AITerritorySafetyTests, Assembly-CSharp-Editor",true).GetMethod("Run").Invoke(null,new object[]{turn.Systems}));
                Stage("executed-movement", () => AITerritoryExecutionTests.Run(turn.Systems,turn));
                Stage("reconnaissance-request", () => AITerritoryReconTests.Run(turn.Systems));
                Stage("persistence", () => AITerritoryPersistenceTests.Run(turn.Systems,turn));
                Stage("save-compatibility", () => SaveCompatibilityTests.RunAll());
            }
            int exit = failedStages == 0 ? 0 : 1;
            Debug.Log("[AITerritoryLiveValidation] COMPLETE exit=" + exit);
            SessionState.SetBool(Key,false);EditorApplication.Exit(exit);
        }
        catch(Exception error){Debug.LogException(error);SessionState.SetBool(Key,false);EditorApplication.Exit(1);}
    }
}
#endif
