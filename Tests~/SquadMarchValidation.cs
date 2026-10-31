#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Controlled subsystem Play Mode test on real terrain, with real legal
/// movement and AP payment. This is not an ordinary campaign or victory test.</summary>
[InitializeOnLoad]
public static class SquadMarchValidation
{
    const string Key = "FK.SquadMarch";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static bool clicked;
    static int frame;
    static SquadMarchValidation() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation") throw new InvalidOperationException("Isolated clone required");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "SquadMarch_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key, true); EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[SquadMarch] FAIL " + name); Debug.Log("[SquadMarch] PASS " + name); }
    static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                var button = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) return;
                UnityEngine.Random.InitState(161803); button.onClick.Invoke(); clicked = true; frame = Time.frameCount; return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || Time.frameCount - frame < 20) return;
            SessionState.SetBool(Key, false);
            foreach (var side in new[] { Team.Player, Team.Enemy }) March(turn.Systems, side);
            Debug.Log("[SquadMarch] COMPLETE real Play Mode frames=" + (Time.frameCount - frame)); EditorApplication.Exit(0);
        }
        catch (Exception e) { SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void March(GameSystems live, Team side)
    {
        using var effects = BoardActorAnimation.SuppressEffects();
        using var f = new R2ConstructionEconomyTests.Fixture(live, side);
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        var actor = f.Engineer; actor.kind = Kind.Knight; actor.GrowthData.kind = Kind.Knight;
        actor.ATK = 20; actor.HP = actor.MaxHP = 100; actor.direction = Direction.N;
        var profile = actor.GrowthData.actionProfile; profile.actionType = ActorActionType.All;
        profile.movement.useCustom = profile.movement.directionIndependent = true;
        profile.attack.useCustom = profile.attack.directionIndependent = true;
        for (int x = 2; x <= 4; x++) for (int z = 2; z <= 4; z++)
            if (x != 3 || z != 3) { profile.movement.SetCell(x, z, true); profile.attack.SetCell(x, z, true); }
        var path = FindPath(actor, live.MapCreate, f.Sites[0]);
        var goal = path[path.Count - 1];
        try
        {
            var board = f.Board();
            var known = new HashSet<Vector3Int>(path);
            string prefix = side + " ";
            void RestoreKnowledge()
            {
                Set(f.Vision, side == Team.Player ? "_playerExplored" : "_enemyExplored", new HashSet<Vector3Int>(known));
                Set(f.Vision, side == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", new HashSet<Vector3Int>(known));
            }
            RestoreKnowledge(); board.Refresh();
            var manager = new AIOperationManager(config); manager.BeginBattle(side, "march-test-" + side);
            board.Operations = manager;
            var plan = new AIOperationPlan { OperationId = 1, PrimaryGoal = AIOperationGoal.LocateEnemyMainForce,
                OriginalGoal = AIOperationGoal.LocateEnemyMainForce, Status = AIOperationStatus.Active, StrategicPriority = 3,
                StartTurn = 1, ExpectedEndTurn = 8, MaximumEndTurn = 15, LastProgressTurn = 1, IsPrimary = true,
                HasTargetPosition = true, TargetX = goal.x, TargetZ = goal.z, TargetCategory = "Territory",
                AssignedUnitLifeIds = new List<string> { actor.ReflectionLifeId },
                OriginalAssignedUnitLifeIds = new List<string> { actor.ReflectionLifeId },
                Steps = new List<AIOperationStep> { new AIOperationStep { StepId = 1, Type = AIOperationStepType.Approach } } };
            manager.State.NextOperationId = 1; manager.State.ActiveOperations.Add(plan);
            var executor = new AIActionExecutor(f.Turn, f.Moves, live.AttackGenerator, live.BattleSystem,
                f.AP, null, null, f.Builder, null, new AILearning(false), side);
            float initialDistance = GridHelper.ChebyshevDistance(actor.transform.position, goal);
            int moved = 0;
            for (int ownTurn = 1; ownTurn <= 3; ownTurn++)
            {
                AITurnBudget.Begin(5000); actor.HasMovedThisTurn = false; actor.Fatigue = 0; f.State.SetAP(side, 40);
                RestoreKnowledge(); board.Refresh(); manager.BeginTurn(board, TurnStrategy.ScoutSearch, ownTurn);
                Check(prefix + "retained-operation-turn-" + ownTurn, manager.State.ActiveOperations.Contains(plan) && plan.OperationId == 1);
                Check(prefix + "formation-owned-by-operation-turn-" + ownTurn, manager.TryGetSquad(actor.ReflectionLifeId, out var squad) && squad.OperationId == plan.OperationId);
                var member = squad.Members.Find(item => item.ActorLifeId == actor.ReflectionLifeId);
                Check(prefix + "route-produces-slot-turn-" + ownTurn, member != null && member.HasSlot && member.MarchSlot);
                live.MapCreate.TryGetHeight(member.SlotX, member.SlotZ, out float height);
                var destination = new Vector3(member.SlotX, height, member.SlotZ);
                var action = new AIAction { Unit = actor, ActionType = AIActionType.Move, TargetPos = destination, APCost = board.CalcMoveCost(actor, destination) };
                int beforeAP = f.State.GetAP(side);
                Vector3 origin = actor.transform.position;
                Check(prefix + "existing-legal-command-turn-" + ownTurn, board.GetValidMoves(actor).Contains(destination) && manager.IsPlannedMarch(action, board));
                Check(prefix + "official-executor-turn-" + ownTurn, executor.Execute(action, board));
                Check(prefix + "exact-AP-payment-turn-" + ownTurn, f.State.GetAP(side) == beforeAP - action.APCost && actor.transform.position == destination);
                moved++; board.Refresh();
                Check(prefix + "old-slot-invalid-after-generation-turn-" + ownTurn, !manager.IsPlannedMarch(action, board));
                manager.Refresh(board, TurnStrategy.Balanced);
                var reverse = new AIAction { Unit = actor, ActionType = AIActionType.Move, TargetPos = origin,
                    APCost = board.CalcMoveCost(actor, origin) };
                if (GridHelper.ChebyshevDistance(origin, goal) > GridHelper.ChebyshevDistance(actor.transform.position, goal))
                    Check(prefix + "healthy-march-not-undone-by-later-fallback-turn-" + ownTurn, manager.ReversesHealthyMarch(reverse, board));
                manager.EndTurn(board, ownTurn);
            }
            Check(prefix + "multi-turn-real-displacement", moved == 3 && GridHelper.ChebyshevDistance(actor.transform.position, goal) < initialDistance);
            var saved = JsonUtility.FromJson<AIOperationBattleState>(JsonUtility.ToJson(manager.State));
            var resumed = new AIOperationManager(config); resumed.BeginBattle(side, "march-test-" + side, saved);
            Check(prefix + "operation-and-squad-survive-save", resumed.State.ActiveOperations.Count == 1 && resumed.State.Squads.Count == 1
                && resumed.State.Squads[0].OperationId == 1);
            board.Governor = new AIStrategicGovernor(); board.Refresh(); manager.Refresh(board, TurnStrategy.CrystalDefense);
            bool falseDefense = false;
            foreach (var active in manager.State.ActiveOperations) falseDefense |= active.PrimaryGoal == AIOperationGoal.DefendOwnCrystal;
            Check(prefix + "fallback-label-does-not-invent-defense-emergency", board.Governor.Mode != StrategicMode.EmergencyDefense && !falseDefense);
            board.Governor = null;
            AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.CrystalDefense);
            Check(prefix + "emergency-suspends-retained-operation", plan.Status == AIOperationStatus.Suspended);
            Check(prefix + "same-turn-emergency-releases-and-reassigns-actor", manager.TryGetSquad(actor.ReflectionLifeId, out var emergencySquad)
                && emergencySquad.OperationId != plan.OperationId);
            // Controlled end of the temporary defense order. No enemy is marked
            // dead or objective credited by this test setup.
            foreach (var active in manager.State.ActiveOperations)
                if (active.OperationId != plan.OperationId) active.Status = AIOperationStatus.Aborted;
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check(prefix + "resume-retains-original-plan-and-member", plan.Status == AIOperationStatus.Active
                && manager.TryGetSquad(actor.ReflectionLifeId, out var originalSquad) && originalSquad.OperationId == plan.OperationId);
            for (int index = 0; index < 2; index++)
            {
                var go = new GameObject("controlled-reinforcement-" + index); go.SetActive(false);
                go.transform.SetParent(actor.transform.parent); go.transform.position = f.Sites[index + 2];
                var reinforcement = go.AddComponent<Status>(); reinforcement.kind = Kind.Knight; reinforcement.type = Type.Unit;
                reinforcement.team = side; reinforcement.GrowthData = actor.GrowthData;
                reinforcement.HP = reinforcement.MaxHP = 100; reinforcement.ATK = 20;
                reinforcement.RestoreReflectionLifeId(Guid.NewGuid().ToString("N")); go.SetActive(true);
            }
            for (int refresh = 0; refresh < 2; refresh++)
            {
                AITurnBudget.Begin(5000); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
                Check(prefix + "reinforcement-reassignment-counts-distinct-actors-" + refresh,
                    plan.AssignedUnitLifeIds.Count == 3 && new HashSet<string>(plan.AssignedUnitLifeIds).Count == 3);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    static List<Vector3Int> FindPath(Status actor, MapCreate map, Vector3Int start)
    {
        var queue = new Queue<Vector3Int>(); var parent = new Dictionary<Vector3Int, Vector3Int>();
        var depth = new Dictionary<Vector3Int, int>(); queue.Enqueue(start); depth[start] = 0;
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            if (depth[from] >= 7)
            {
                var path = new List<Vector3Int> { from };
                while (parent.TryGetValue(from, out var previous)) { path.Add(previous); from = previous; }
                path.Reverse(); return path;
            }
            foreach (var offset in MovePatterns.Offsets(actor))
            {
                int x = from.x + offset.x, z = from.z + offset.y;
                if (!map.TryGetHeight(x, z, out float y)) continue;
                var next = new Vector3Int(x, Mathf.RoundToInt(y), z);
                if (depth.ContainsKey(next) || !map.IsLegalMove(actor, from, next)) continue;
                parent[next] = from; depth[next] = depth[from] + 1; queue.Enqueue(next);
            }
        }
        throw new InvalidOperationException("Controlled march requires a legal seven-step corridor in the real map");
    }
}
#endif
