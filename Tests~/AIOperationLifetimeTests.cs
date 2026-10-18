#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Copy into the isolated regression harness; invoke PlayMode after the normal map starts.</summary>
public static class AIOperationLifetimeTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException("[OperationLifetime] " + message); }
    static void Set(object value, string name, object field)
        => value.GetType().GetField(name, Private).SetValue(value, field);
    static object Get(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
    static void Call(object value, string name, params object[] args)
        => value.GetType().GetMethod(name, Private).Invoke(value, args);
    public static void PlayMode(GameSystems live)
    {
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy }) Verify(live, team);
        Debug.Log("[AIOperationLifetimeTests] " + checks + " destroyed/cached/turn-tail/faction checks passed");
    }
    static AIOperationPlan Plan(AIOperationGoal goal, Status target, Vector3 at, long id)
    {
        return new AIOperationPlan
        {
            OperationId = id, PrimaryGoal = goal, OriginalGoal = goal, Status = AIOperationStatus.Active,
            HasTargetPosition = true, TargetLifeId = target.ReflectionLifeId, TargetX = (int)at.x, TargetZ = (int)at.z,
            StartTurn = 1, ExpectedEndTurn = 8, MaximumEndTurn = 15, LastProgressTurn = 1, StrategicPriority = 1,
            StartContext = new AIOperationContext(), CurrentContext = new AIOperationContext(),
            Steps = new List<AIOperationStep> { new AIOperationStep { StepId = 1, Type = AIOperationStepType.Approach } }
        };
    }
    static void Verify(GameSystems live, Team team)
    {
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var extra = new GameObject("Operation lifetime isolated vision/turn"); extra.SetActive(false);
            var vision = extra.AddComponent<VisionGenerator>(); var hub = extra.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            var config = ScriptableObject.CreateInstance<AIOperationConfig>();
            try
            {
                config.EnablePersistentLearning = false;
                var cells = f.AdjacentCells(); var seen = new HashSet<Vector3Int>();
                foreach (var cell in live.MapCreate.SetPos) seen.Add(GridHelper.ToGridXZ(cell));
                foreach (string name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(name, Private).SetValue(vision, new HashSet<Vector3Int>(seen));
                Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit);
                Set(vision, "territorysystem", live.TerritorySystem);
                var doomedOwn = f.Unit(); doomedOwn.transform.position = cells[0]; doomedOwn.ATK = 20;
                var survivor = f.Unit(); survivor.transform.position = cells[1]; survivor.ATK = 40;
                var doomedFoe = f.Opponent(cells[2], 10);
                var liveFoe = f.Opponent(cells[2], 10);
                var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals,
                    vision, f.Builder, null, f.State, null, 20, null, team);
                board.MapCreate = live.MapCreate; board.ReconThreatLevel = 15;
                board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
                var manager = new AIOperationManager(config); manager.BeginBattle(team, "DestroyedStatus_" + team);
                manager.State.LastStartedOwnTurn = 1;
                var features = (AIOperationFeatureExtractor)Get(manager, "features"); features.Prepare(board, 1);
                var ownArmy = (List<Status>)Get(manager, "ownArmy"); ownArmy.Add(doomedOwn); ownArmy.Add(survivor);
                string ownLife = doomedOwn.ReflectionLifeId, foeLife = doomedFoe.ReflectionLifeId;
                var defense = Plan(AIOperationGoal.DefendOwnCrystal, f.Crystal, f.Crystal.transform.position, 1);
                defense.StartContext = features.Capture(board, defense, 1); defense.CurrentContext = defense.StartContext.Copy();
                var assault = Plan(AIOperationGoal.DestroySubCrystal, liveFoe, cells[2], 2);
                assault.IsBaseAssault = true; assault.BaseAssaultPattern = AIBaseAssaultPattern.DirectAssault;
                assault.BaseTargetType = AIEnemyBaseType.SubCrystal; assault.RallyX = (int)cells[0].x; assault.RallyZ = (int)cells[0].z;
                assault.AssignedUnitLifeIds.Add(survivor.ReflectionLifeId); assault.OriginalAssignedUnitLifeIds.Add(survivor.ReflectionLifeId);
                assault.StartContext = features.Capture(board, assault, 1); assault.CurrentContext = assault.StartContext.Copy();
                manager.State.KnownEnemyBases.Add(new AIKnownEnemyBase { StableTargetId = liveFoe.ReflectionLifeId,
                    Type = AIEnemyBaseType.SubCrystal, X = (int)cells[2].x, Z = (int)cells[2].z,
                    FirstSeenTurn = 1, LastSeenTurn = 1, CurrentlyVisible = true, IntelConfidence = 1,
                    LastKnownHpRatio = 1, EstimatedStrategicValue = 75 });
                manager.State.ActiveOperations.Add(assault); manager.State.ActiveOperations.Add(defense);
                UnityEngine.Object.DestroyImmediate(doomedOwn.gameObject); UnityEngine.Object.DestroyImmediate(doomedFoe.gameObject);
                Check(doomedOwn == null && !ReferenceEquals(doomedOwn, null) && doomedFoe == null && !ReferenceEquals(doomedFoe, null), "fixture creates Unity fake-null wrappers " + team);
                int cachedGeneration = board.Generation;
                Check(!features.TryOwn(ownLife, out var invalidOwn) && invalidOwn == null, "cached own lookup rejects destroyed component " + team);
                Check(!features.TryObserved(foeLife, out var invalidFoe) && invalidFoe == null, "cached observed lookup rejects destroyed component " + team);
                foreach (var unit in features.OwnUnits(board, 1)) Check(unit != null && unit.IsAlive, "cached own enumeration exposes live units only " + team);
                foreach (var unit in features.ObservedUnits(board, 1)) Check(unit != null && unit.IsAlive, "cached observed enumeration exposes live units only " + team);
                var capture = features.Capture(board, defense, 1);
                Check(board.Generation == cachedGeneration && !capture.ConfirmedTargetDestroyed && !capture.TargetForceConfirmedEliminated,
                    "same-generation defense capture survives vanished contact without inventing a kill " + team);
                var flank = Plan(AIOperationGoal.FlankEnemyForce, liveFoe, cells[2], 3);
                Call(manager, "ConfirmTacticalGoal", flank, new AIActionRecord { ActorLifeId = survivor.ReflectionLifeId,
                    TargetLifeId = liveFoe.ReflectionLifeId, Outcome = new AIActionOutcome { DamageDealt = 1 } }, board);
                Check(flank.CurrentContext != null, "formation analysis skips destroyed retained partners " + team);
                // Match the production turn tail: Refresh replaces board facts, but does not replace manager.ownArmy.
                board.Refresh();
                Check(ownArmy.Exists(unit => unit == null), "board refresh leaves retained operation army stale " + team);
                var commander = new AICommander(hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem, vision, f.AP,
                    f.Units, f.Crystals, live.MapCreate, MajorPersonality.Combat, f.Builder, null, f.State, live.SkillSystem,
                    null, 15, 56192, team);
                Set(commander, "_board", board); Set(commander, "operations", manager);
                Set(commander, "operationTurnStarted", true); Set(commander, "currentOperationOwnTurn", 1);
                Call(commander, "EndOperationTurn");
                Check(!(bool)Get(commander, "operationUnavailable") && manager.OwnTurns == 1 && manager.State.LastEndedOwnTurn == 1,
                    "real commander completes operation turn instead of disabling operation recording " + team);
                Check(!ownArmy.Exists(unit => unit == null || !unit.IsAlive), "turn tail removes dead retained army entries " + team);
                manager.BeginTurn(board, TurnStrategy.ScoutSearch, 2);
                Check(manager.State.LastStartedOwnTurn == 2 && !(bool)Get(commander, "operationUnavailable"),
                    "operation planning continues on the next own turn " + team);
            }
            finally { UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(extra); }
        }
    }
}
#endif
