#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Ordinary player/enemy/third-faction turn flow using the real developer player AI.</summary>
[InitializeOnLoad]
public static class BalanceAutoplayRunner
{
    const string Running = "FK.BalanceAutoplay.Running";
    static TurnGenerator turn;
    static int recordedTurn;
    static double started;
    static readonly StringBuilder rows = new StringBuilder();
    static BalanceAutoplayRunner() { EditorApplication.update += Tick; }
    public static void Run() => RunWithTurns(20);
    public static void Run60() => RunWithTurns(60);
    static void RunWithTurns(int turns)
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated validation only");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "FantasyKingdomR1Validation";
        SessionState.SetInt("FK.BalanceAutoplay.Turns", turns);
        SessionState.SetBool(Running, true); EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (turn == null)
            {
                var game = UnityEngine.Object.FindFirstObjectByType<GameGenerator>();
                if (game == null || TitleScreenUI.Instance == null) return;
                typeof(GameGenerator).GetField("_waitingForTitle", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, false);
                UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);
                UnityEngine.Random.InitState(56192);
                typeof(GameGenerator).GetMethod("StartGameInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { -1 });
                turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(); turn.MarkDeveloperMatch();
                var threat = typeof(AICommander).GetField("_threatLevel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(turn.Systems.AICommander);
                typeof(AIThreatLevel).GetProperty("Level").SetValue(threat, 15);
                turn.PlayerAI.SetEnabled(true, false); started = EditorApplication.timeSinceStartup;
                rows.AppendLine("turn,playerWood,playerStone,playerWater,playerIron,playerBread,playerBuildings,enemyWood,enemyStone,enemyWater,enemyIron,enemyBread,enemyBuildings,playerUnits,playerUnpaid,playerMines,enemyUnits,enemyUnpaid,enemyMines");
            }
            if (EditorApplication.timeSinceStartup - started > 600) throw new TimeoutException("Ordinary autoplay exceeded ten minutes");
            if (turn.Context.Turn != recordedTurn && turn.CurrentState is PlayerMove)
            {
                recordedTurn = turn.Context.Turn; var s = turn.Systems;
                var p = s.FactionState.PlayerResources; var e = s.FactionState.EnemyResources;
                rows.Append(recordedTurn).Append(',').Append(p.Wood).Append(',').Append(p.Stone).Append(',').Append(p.Water).Append(',').Append(p.Iron).Append(',').Append(p.Bread)
                    .Append(',').Append(s.BuildSystem.PlayerBuildingParent.childCount).Append(',').Append(e.Wood).Append(',').Append(e.Stone).Append(',').Append(e.Water).Append(',').Append(e.Iron)
                    .Append(',').Append(e.Bread).Append(',').Append(s.BuildSystem.EnemyBuildingParent.childCount);
                AppendArmy(s.UnitSetting.PlayerUnit);
                rows.Append(',').Append(s.BuildSystem.GetBuildingCount(Team.Player, FacilityKind.Mine));
                AppendArmy(s.UnitSetting.EnemyUnit);
                rows.Append(',').Append(s.BuildSystem.GetBuildingCount(Team.Enemy, FacilityKind.Mine)).AppendLine();
                Debug.Log("[BalanceAutoplay] turn=" + recordedTurn + " playerBuildings=" + s.BuildSystem.PlayerBuildingParent.childCount + " enemyBuildings=" + s.BuildSystem.EnemyBuildingParent.childCount);
                if (p.Wood < 0 || p.Stone < 0 || p.Water < 0 || e.Wood < 0 || e.Stone < 0 || e.Water < 0) throw new InvalidOperationException("Negative material stock");
            }
            if (recordedTurn >= SessionState.GetInt("FK.BalanceAutoplay.Turns", 20) || turn.IsGameOver)
            {
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../balance-autoplay.csv")), rows.ToString());
                Debug.Log("[BalanceAutoplay] COMPLETED turns=" + recordedTurn + " gameOver=" + turn.IsGameOver + " seconds=" + (EditorApplication.timeSinceStartup - started).ToString("F1"));
                SessionState.SetBool(Running, false); EditorApplication.Exit(0);
            }
        }
        catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Running, false); EditorApplication.Exit(1); }
    }
    static void AppendArmy(Transform parent)
    {
        int alive = 0, unpaid = 0;
        foreach (var unit in parent.GetComponentsInChildren<Status>())
            if (unit.IsAlive && unit.type == Type.Unit) { alive++; if (unit.UpkeepUnpaidTurns > 0) unpaid++; }
        rows.Append(',').Append(alive).Append(',').Append(unpaid);
    }
}
#endif
