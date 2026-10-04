#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerAutoplayRunner
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static TurnGenerator turn;
    static int startRound;
    static double started;
    static bool initialized;
    static PlayerAutoplayRunner() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Isolated project required");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "FantasyKingdomR1Validation";
        SessionState.SetBool(nameof(PlayerAutoplayRunner), true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Check(string label, bool value)
    { if (!value) throw new Exception("[PlayerAutoplay] FAIL " + label); Debug.Log("[PlayerAutoplay] PASS " + label); }
    sealed class FaultIterator : IEnumerator, IDisposable
    {
        public int Disposals;
        public bool Throw;
        public object Current => null;
        public bool MoveNext() { if (Throw) throw new Exception("[PlayerAutoplay] injected iterator failure"); return true; }
        public void Dispose() { Disposals++; }
        public void Reset() { }
    }
    static void InitialChecks()
    {
        var s = turn.Systems; var ai = turn.PlayerAI; var move = (PlayerMove)turn.CurrentState;
        Check("starts disabled", !ai.Enabled && !ai.WasUsed);
        Check("default key is F8", (UnityEngine.InputSystem.Key)typeof(TurnGenerator).GetField("playerAIToggleKey", Private).GetValue(turn) == UnityEngine.InputSystem.Key.F8);
        s.FactionState.SetAP(Team.Player, 12); s.FactionState.SetAP(Team.Enemy, 17);
        float timer = s.TimerSystem.TurnTimeRemaining;
        ai.SetEnabled(true, false); ai.Tick(0);
        Check("same commander at threat 15 uses Player", ai.Commander.ActorTeam == Team.Player && ai.Commander.ThreatLevel.Level == 15);
        Check("learning disabled for autoplay", !ai.Commander.Learning.IsActive && ai.Commander.MLIntegration == null);
        ai.PauseForMenu(); double budget = AITurnBudget.RemainingMs;
        System.Threading.Thread.Sleep(20);
        Check("menu freezes AI wall-time budget", Math.Abs(AITurnBudget.RemainingMs - budget) < 2);
        ai.SetEnabled(false, false);
        Check("OFF preserves remaining AP and timer", s.APSystem.GetAP(Team.Player) == 12 && s.APSystem.GetAP(Team.Enemy) == 17 && s.TimerSystem.TurnTimeRemaining == timer);
        Check("OFF retains player turn", turn.CurrentState == move && !ai.Enabled);
        ai.SetEnabled(true, false);
        s.BuildSystem.StartBuildMode(FacilityKind.House); s.SummonSystem.StartSummonMode(Kind.Knight);
        Check("manual build and summon modes blocked", !s.BuildSystem.IsActive && !s.SummonSystem.IsActive);
        ai.Tick(0);
        var faults = new FaultIterator { Throw = true };
        ((IDisposable)typeof(DeveloperPlayerAIController).GetField("steps", Private).GetValue(ai)).Dispose();
        typeof(DeveloperPlayerAIController).GetField("steps", Private).SetValue(ai, faults);
        ai.Tick(0);
        Check("iterator failure returns manual control", !ai.Enabled && turn.CurrentState == move && faults.Disposals == 1);
        var player = s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(u => u.IsAlive);
        move.SelectedUnit = player; move.SelectedUnitPosition = player.transform.position; turn.Context.SelectUnit = player;
        turn.ChangeState(new PlayerAttack(turn, move, PlayerMove.AttackMode.Normal));
        ai.SetEnabled(true, false);
        Check("attack mode safely returns to movement owner", turn.CurrentState == move && move.SelectedUnit == null && turn.Context.SelectUnit == null);
        var board = new AIBoardState(s.MoveGenerator, s.AttackGenerator, s.APSystem, s.UnitSetting,
            s.CrystalSystem, s.VisionGenerator, s.BuildSystem, s.SummonSystem, s.FactionState,
            s.SubCrystalSystem, turn.Context.Turn, null, Team.Player);
        Check("board uses actual Player AP and resources", board.EnemyAP == 12 && ReferenceEquals(board.EnemyResources, s.FactionState.PlayerResources));
        Check("own player roster contains no real enemy units", board.AliveEnemyUnits.All(u => u.team == Team.Player));
        Check("opponents respect player vision", board.AlivePlayerUnits.All(u => s.VisionGenerator.IsInVisionXZ(Team.Player, u.transform.position)));
        var sim = SimBoardState.CreateFromGame(board, s.MoveGenerator, s.UnitSetting, s.CrystalSystem, s.APSystem);
        Check("simulation maps player to own side", sim.RealActorTeam == Team.Player && sim.ToSimulationTeam(Team.Player) == Team.Enemy && sim.ToSimulationTeam(Team.Enemy) == Team.Player);
        int mapped = (int)typeof(AIMinimaxEngine).GetMethod("FindSimUnitId", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { player, sim });
        Check("minimax real player unit ID resolves", mapped >= 0);
        Check("real teams remain intact", player.team == Team.Player);
        ai.SetEnabled(false, false);
        ai.SetEnabled(true, false); ai.Tick(0);
        var timeout = new FaultIterator();
        ((IDisposable)typeof(DeveloperPlayerAIController).GetField("steps", Private).GetValue(ai)).Dispose();
        typeof(DeveloperPlayerAIController).GetField("steps", Private).SetValue(ai, timeout);
        typeof(DeveloperPlayerAIController).GetField("activeSeconds", Private).SetValue(ai, 12f);
        ai.Tick(0);
        Check("timeout ends ordinary player turn once", turn.CurrentState is EnemyMove && timeout.Disposals == 1);
        Check("autoplay stays enabled while enemy acts", ai.Enabled && !ai.Tick(0));
        // Real-time run from this enemy turn, then two complete player/enemy cycles.
        s.AICommander.TurnThinkingBudgetMs = 350;
        s.TimerSystem.PlayerTotalTime = s.TimerSystem.EnemyTotalTime = 999;
        Check("used flag persists in game save", SaveSystem.CollectGameState(turn, s.FactionState).DeveloperAutoplayUsed);
        startRound = turn.Context.Turn;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(nameof(PlayerAutoplayRunner), false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!initialized)
            {
                var game = UnityEngine.Object.FindFirstObjectByType<GameGenerator>(); if (game == null || TitleScreenUI.Instance == null) return;
                typeof(GameGenerator).GetField("_waitingForTitle", Private).SetValue(game, false);
                UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject); UnityEngine.Random.InitState(12345);
                typeof(GameGenerator).GetMethod("StartGameInit", Private).Invoke(game, new object[] { -1 });
                turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
                InitialChecks(); initialized = true; started = EditorApplication.timeSinceStartup; return;
            }
            if (turn.IsGameOver) throw new Exception("Unexpected early game over");
            if (turn.Context.Turn >= startRound + 3 && turn.CurrentState is PlayerMove)
            {
                turn.PlayerAI.SetEnabled(false, false);
                Check("autoplay completed multiple whole turn cycles", turn.Context.Turn >= startRound + 3);
                Check("manual player timer and banner restored", turn.Systems.TimerSystem.CurrentTeam == Team.Player && !EnemyTurnBannerUI.IsShowing);
                Check("player autoplay thinks in bounded slices", turn.PlayerAI.Commander.LastSearchMaxSliceMs < 80);
                Debug.Log("[PlayerAutoplay] ALL PASSED"); SessionState.SetBool(nameof(PlayerAutoplayRunner), false); EditorApplication.Exit(0);
            }
            if (EditorApplication.timeSinceStartup - started > 35) throw new Exception("Autoplay turn cycle stalled in " + turn.CurrentState.GetType().Name);
        }
        catch (Exception error)
        { Debug.LogException(error); SessionState.SetBool(nameof(PlayerAutoplayRunner), false); EditorApplication.Exit(1); }
    }
}
#endif
