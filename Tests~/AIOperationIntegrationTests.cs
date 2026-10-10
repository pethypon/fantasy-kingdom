#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AIOperationIntegrationTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[OperationRuntime] FAIL " + name); passed++; Debug.Log("[OperationRuntime] PASS " + name); }
    static AIBoardState Board(GameSystems live, EconomyR2Tests.Fixture f, VisionGenerator vision)
    {
        var b = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals,
            vision, f.Builder, null, f.State, null, 20, null, f.Team);
        b.MapCreate = live.MapCreate; b.ReconThreatLevel = 15; b.Governor = new AIStrategicGovernor(); b.Governor.Evaluate(b); return b;
    }
    static AICommander Commander(GameSystems live, EconomyR2Tests.Fixture f, TurnGenerator hub, VisionGenerator vision)
        => new AICommander(hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem, vision, f.AP,
            f.Units, f.Crystals, live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, live.SkillSystem,
            null, 15, 56192, f.Team);
    public static void PlayMode(GameSystems live)
    {
        passed = 0;
        foreach (var team in new[] { Team.Enemy, Team.Player }) VerifyFaction(live, team);
        var oldReflection = new AIReflectionBattleState { BattleId = "Operation_Migration" };
        var old = new SaveSystem.GameSaveData { Version = 5, AI = new SaveSystem.AISaveData { Reflection = oldReflection } };
        typeof(SaveSystem).GetMethod("RunMigrations", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { old });
        Check("v5 migration introduces no invented operation and retains action memory", old.Version == 6 && old.AI.Operations == null
            && ReferenceEquals(old.AI.Reflection, oldReflection));
        Debug.Log("[OperationRuntime] " + passed + " real FoW/Commander/save/fallback checks passed");
    }
    static void VerifyFaction(GameSystems live, Team team)
    {
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var extra = new GameObject("Operation isolated vision/turn"); extra.SetActive(false);
            var vision = extra.AddComponent<VisionGenerator>(); var hub = extra.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var config = ScriptableObject.CreateInstance<AIOperationConfig>();
            var reflectionConfig = ScriptableObject.CreateInstance<AIReflectionConfig>();
            reflectionConfig.EnableConsoleDiary = reflectionConfig.EnableFileDiary = false;
            try
            {
                hub.Systems.MoveGenerator = live.MoveGenerator; hub.Systems.AttackGenerator = live.AttackGenerator;
                hub.Systems.UnitSetting = f.Units; hub.Systems.FactionState = f.State; hub.Systems.CrystalSystem = f.Crystals;
                hub.Systems.VisionGenerator = vision;
                typeof(VisionGenerator).GetField("PlayerUnit", Private).SetValue(vision, f.Units.PlayerUnit);
                typeof(VisionGenerator).GetField("EnemyUnit", Private).SetValue(vision, f.Units.EnemyUnit);
                typeof(VisionGenerator).GetField("territorysystem", Private).SetValue(vision, live.TerritorySystem);
                var cells = f.AdjacentCells(); var seen = new HashSet<Vector3Int>(); foreach (var cell in cells) seen.Add(GridHelper.ToGridXZ(cell));
                foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(name, Private).SetValue(vision, new HashSet<Vector3Int>(name.StartsWith(team == Team.Player ? "_player" : "_enemy") ? seen : new HashSet<Vector3Int>()));
                var scout = f.Unit(); scout.kind = scout.GrowthData.kind = Kind.Scout; scout.transform.position = cells[0];
                var fighter = f.Unit(); fighter.transform.position = cells[1]; fighter.ATK = 8;
                Vector3 unknown = Vector3.zero; bool found = false;
                foreach (var cell in live.MapCreate.SetPos)
                    if (!seen.Contains(GridHelper.ToGridXZ(cell)) && GridHelper.ChebyshevDistance(cell, cells[0]) > 10)
                    { unknown = cell; found = true; break; }
                Check("T17 real terrain has an unobserved opponent cell " + team, found);
                var hidden = f.Opponent(unknown, 999); var board = Board(live, f, vision);
                var manager = new AIOperationManager(config, new AIOperationLearningProfile { ProfileId = "PlayerDeveloper" });
                manager.BeginBattle(team, "Runtime_Operation_" + team); manager.BeginTurn(board, TurnStrategy.ScoutSearch, 1);
                var features = new AIOperationFeatureExtractor(config);
                var target = new AIOperationPlan { PrimaryGoal = AIOperationGoal.DestroySubCrystal, HasTargetPosition = true,
                    TargetLifeId = hidden.ReflectionLifeId, TargetX = (int)unknown.x, TargetZ = (int)unknown.z };
                string before = JsonUtility.ToJson(features.Capture(board, target, 1));
                hidden.transform.position = cells[2]; hidden.HP = 1; hidden.ATK = 9999;
                var attack = new AIAction { ActionType = AIActionType.Attack, Unit = scout, TargetUnit = hidden, TargetPos = unknown };
                float hint = manager.ActionBonus(attack, board);
                string after = JsonUtility.ToJson(features.Capture(board, target, 1));
                Check("T17 current hidden position/HP cannot alter operation facts or attack hint " + team,
                    before == after && !board.AlivePlayerUnits.Contains(hidden) && hint == 0);
                hidden.transform.position = unknown;
                var commander = Commander(live, f, hub, vision); commander.SaveTurnCount = 20; commander.RestoreStrategy(TurnStrategy.ScoutSearch);
                typeof(AICommander).GetField("_board", Private).SetValue(commander, board);
                typeof(AICommander).GetField("operations", Private).SetValue(commander, manager);
                var reflection = new AIActionReflectionSystem(team, reflectionConfig, persistent: false);
                reflection.BeginBattle("Runtime_Operation_" + team, 15); reflection.BeginTurn(20, board, TurnStrategy.ScoutSearch, 15);
                typeof(AICommander).GetField("reflection", Private).SetValue(commander, reflection);
                // The manager fixture already opened own turn one. Let Commander establish its normal callback boundary.
                manager.State.LastStartedGlobalTurn = -1;
                typeof(AICommander).GetMethod("BeginOperationTurn", Private).Invoke(commander, null);
                Check("one main operation and bounded concurrent plans " + team, manager.PrimaryOperation != null
                    && manager.State.ActiveOperations.Count <= config.ConcurrentLimit);
                var assigned = new HashSet<string>(); bool unique = true, king = false;
                foreach (var plan in manager.ActiveOperations)
                    if (plan.Status != AIOperationStatus.Suspended)
                        foreach (string id in plan.AssignedUnitLifeIds)
                        { unique &= assigned.Add(id); if (id == f.Crystal.ReflectionLifeId) king = true; }
                Check("units retain distinct operation assignments and critical objects stay free " + team, unique && !king);
                var moves = board.GetValidMoves(scout);
                Check("real scout has a legal operation-observed move " + team, moves.Count > 0);
                var move = new AIAction { ActionType = AIActionType.Move, Unit = scout, TargetPos = moves[0], APCost = board.CalcMoveCost(scout, moves[0]),
                    Score = 10, StrategicPriority = 3 };
                bool success = (bool)typeof(AICommander).GetMethod("ExecuteReflectedAction", Private).Invoke(commander, new object[] { move });
                Check("real executor links finalized action to a retained operation " + team, success && reflection.LastRecord.OperationId > 0
                    && reflection.LastRecord.OperationStepId > 0 && reflection.LastRecord.Outcome != null);
                bool retained = false;
                foreach (var plan in manager.ActiveOperations) retained |= plan.ActionIds.Contains(reflection.LastRecord.ActionId);
                foreach (var plan in manager.State.CompletedOperations) retained |= plan.ActionIds.Contains(reflection.LastRecord.ActionId);
                Check("completed action appears in exactly the operation evidence route " + team, retained);
                var game = new SaveSystem.GameSaveData();
                typeof(SaveSystem).GetMethod("CollectAI", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { game.AI, commander });
                game = JsonUtility.FromJson<SaveSystem.GameSaveData>(JsonUtility.ToJson(game));
                Check("T18 actual save contains independent action and operation payloads " + team,
                    game.Version == 6 && game.AI.Reflection != null && game.AI.Operations != null
                    && game.AI.Operations.BattleId == manager.State.BattleId && game.AI.Operations.ActiveOperations.Count > 0);
                var loaded = Commander(live, f, hub, vision); SaveSystem.RestoreAICommander(game.AI, loaded);
                Check("T18 actual commander restore keeps the operation counter and active goal " + team,
                    loaded.Operations != null && loaded.Operations.State.NextOperationId == manager.State.NextOperationId
                    && loaded.Operations.PrimaryOperation?.OperationId == manager.PrimaryOperation?.OperationId);
                typeof(AICommander).GetField("_board", Private).SetValue(loaded, board);
                typeof(AICommander).GetMethod("OperationFailed", Private).Invoke(loaded, new object[] { new InvalidOperationException("isolated operation failure") });
                var weaker = new AIAction { Score = 10, StrategicPriority = 4, APCost = 0, ActionType = AIActionType.Rotate };
                var stronger = new AIAction { Score = 12, StrategicPriority = 4, APCost = 0, ActionType = AIActionType.Rotate };
                var winner = typeof(AICommander).GetMethod("SelectBestAction", Private).Invoke(loaded,
                    new object[] { new List<AIAction> { weaker, stronger }, new HashSet<string>(), null });
                Check("operation faults leave normal action selection functional " + team, ReferenceEquals(winner, stronger));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(reflectionConfig); UnityEngine.Object.DestroyImmediate(extra); }
        }
    }
}
#endif
