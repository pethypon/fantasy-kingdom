#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Source-only completed-result handoff contracts. NOT RUN; no claim about real Sim/3D or match parity.</summary>
public static class AIR4LookaheadTests
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[R4Lookahead] FAIL " + id); Debug.Log("[R4Lookahead] PASS " + id); }

    public static void Run()
    {
        var cpu = typeof(AITurnBudget).GetField("clock", PrivateStatic);
        var wall = typeof(AITurnBudget).GetField("wallClock", PrivateStatic);
        var budget = typeof(AITurnBudget).GetField("limit", PrivateStatic);
        object savedCpu = cpu.GetValue(null), savedWall = wall.GetValue(null), savedLimit = budget.GetValue(null);
        var actorHost = new GameObject("R4 lookahead own fixture"); actorHost.SetActive(false);
        var targetHost = new GameObject("R4 lookahead observed fixture"); targetHost.SetActive(false);
        var config = ScriptableObject.CreateInstance<AICognitiveAIConfig>();
        try
        {
            AITurnBudget.Begin(8000); object startedClock = cpu.GetValue(null);
            var actor = actorHost.AddComponent<Status>(); actor.kind = Kind.Knight; actor.RestoreReflectionLifeId("r4-look-own");
            var target = targetHost.AddComponent<Status>(); target.kind = Kind.Crystal; target.RestoreReflectionLifeId("r4-look-target");
            actor.transform.position = new Vector3(1, 0, 1); target.transform.position = new Vector3(4, 0, 1);
            var move = new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = new Vector3(2, 0, 1), APCost = 3,
                Score = 10, StrategicPriority = 3 };
            var attack = new AIAction { ActionType = AIActionType.Attack, Unit = actor, TargetUnit = target,
                TargetPos = target.transform.position, APCost = 5, Score = 10, StrategicPriority = 3 };
            var actions = new List<AIAction> { move, attack };
            var world = World(1);
            var planner = new AICognitivePlanner();
            var matches = Matches("CH-06", "SL-04");
            config.Enabled = true; config.SwitchMargin = 0;
            planner.Refresh(world, actions, matches, new AICognitivePlannerLimits(6), assessment: Assess);
            Check("F01-source-distinct-means", planner.Candidates.Count == 2);
            var results = new Dictionary<AIAction, float> { [move] = -1000, [attack] = 1000 };
            string original = planner.Current.PlanId;
            Check("F06-depth-zero-rejected", !planner.ObserveCompletedLookahead(world, results, 0, "minimax_completed", config)
                && planner.Current.PlanId == original);
            Check("F06-heuristic-not-minimax", !planner.ObserveCompletedLookahead(world, results, 2, "heuristic", config));
            Check("F06-stale-generation-rejected", !planner.ObserveCompletedLookahead(World(2), results, 2, "minimax_completed", config));
            float moveScore = move.Score, attackScore = attack.Score;
            Check("F01-existing-completed-result-used", planner.ObserveCompletedLookahead(world, results, 2, "minimax_completed", config)
                && planner.Current.PrimaryMethod == "ATTACK" && planner.Current.LookaheadCompletedDepth == 2);
            Check("F01-no-second-action-score-addition", move.Score == moveScore && attack.Score == attackScore);
            Check("F01-no-new-thinking-clock", ReferenceEquals(cpu.GetValue(null), startedClock) && AITurnBudget.LimitMs == 8000);
            foreach (var candidate in planner.Candidates)
                Check("F01-bounded-utility-" + candidate.PrimaryMethod,
                    Mathf.Abs(candidate.LookaheadUtilityAdjustment) <= AICognitivePlanner.MaximumLookaheadUtilityAdjustment);
            float utility = planner.Current.Utility;
            planner.ObserveCompletedLookahead(world, results, 2, "minimax_completed", config);
            Check("F01-repeated-result-idempotent", Mathf.Abs(planner.Current.Utility - utility) < .00001f);
            Check("F01-no-whole-plan-parity-claim", planner.Current.EnemyResponses.TrueForAll(r => r.IsPrediction));
            utility = planner.Current.Utility;

            var nonfinite = new Dictionary<AIAction, float> { [move] = float.NaN, [attack] = float.PositiveInfinity };
            Check("F06-nonfinite-results-ignored", !planner.ObserveCompletedLookahead(world, nonfinite, 3, "minimax_completed", config)
                && planner.Current.Utility == utility);
            var unmatched = new AIAction { ActionType = AIActionType.Move, Unit = actor,
                TargetPos = new Vector3(3, 0, 2), APCost = 3, Score = 99, StrategicPriority = 3 };
            Check("F06-unbound-result-ignored", !planner.ObserveCompletedLookahead(world,
                new Dictionary<AIAction, float> { [unmatched] = 999 }, 3, "minimax_completed", config));
            attack.StrategicPriority = 0;
            Check("I02-protected-band-not-reclassified", !planner.ObserveCompletedLookahead(world,
                new Dictionary<AIAction, float> { [attack] = 999 }, 3, "minimax_completed", config));
            attack.StrategicPriority = 3; config.Enabled = false;
            Check("X19-disabled-handoff-no-op", !planner.ObserveCompletedLookahead(world, results, 3, "minimax_completed", config));
            config.Enabled = true;
            Check("F06-lower-depth-does-not-replace-complete-depth", !planner.ObserveCompletedLookahead(world, results, 1, "minimax_completed", config)
                && planner.Current.LookaheadCompletedDepth == 2);

            var stablePlanner = new AICognitivePlanner();
            stablePlanner.Refresh(world, actions, matches, new AICognitivePlannerLimits(6), assessment: Assess);
            string retained = stablePlanner.Current.PlanId; config.SwitchMargin = 1;
            Check("P04-small-lookahead-difference-retains-progressing-plan", stablePlanner.ObserveCompletedLookahead(world,
                new Dictionary<AIAction, float> { [move] = 0, [attack] = .1f }, 2, "minimax_completed", config)
                && stablePlanner.Current.PlanId == retained);
            foreach (var candidate in stablePlanner.Candidates)
                if (candidate.PrimaryMethod == "ATTACK") candidate.Goal = AIOperationGoal.EconomicRecovery;
            stablePlanner.ObserveCompletedLookahead(world, results, 3, "minimax_completed", config);
            Check("P04-completed-score-does-not-change-final-objective", stablePlanner.Current.Goal == AIOperationGoal.DestroyEnemyCrystal);

            var engine = new AISearchEngine();
            typeof(AISearchEngine).GetProperty(nameof(AISearchEngine.LastCompletedDepth)).SetValue(engine, 3);
            typeof(AISearchEngine).GetProperty(nameof(AISearchEngine.LastResultKind)).SetValue(engine, "minimax_completed");
            try { engine.EvaluateWithLookaheadSteps(new List<AIAction>(), null, new Dictionary<AIAction, float>()); }
            catch (ArgumentNullException) { }
            Check("F06-entry-invalidates-old-metadata-before-enumeration", engine.LastCompletedDepth == 0 && engine.LastResultKind == "none"
                && engine.LastResultBoardGeneration == -1 && engine.LastResultObservationIdentity == 0);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actorHost); UnityEngine.Object.DestroyImmediate(targetHost);
            UnityEngine.Object.DestroyImmediate(config);
            cpu.SetValue(null, savedCpu); wall.SetValue(null, savedWall); budget.SetValue(null, savedLimit);
        }
    }

    static AICognitiveActionAssessment Assess(AIAction action) => new AICognitiveActionAssessment {
        Legal = true, HardSafe = true, Confidence = 1, ExpectedGoalProgress = .4f,
        EffectiveDamage = action.ActionType == AIActionType.Attack ? 10 : 0 };
    static List<AIKnowledgeMatch> Matches(params string[] ids)
    {
        var result = new List<AIKnowledgeMatch>();
        foreach (string id in ids)
        { AIStrategyKnowledgeCatalog.TryGet(id, out var principle); result.Add(new AIKnowledgeMatch(principle, AITruthValue.True, 1,
            new[] { "source-formal-fixture" }, Array.Empty<AIStrategyPredicate>(), string.Empty)); }
        return result;
    }
    static AIObservedWorldModel World(int generation) => new AIObservedWorldModel("r4-lookahead", Team.Enemy, 1, generation, 40, 40, 20, default,
        own: new[] { new AIObservedUnitValue("r4-look-own", Kind.Knight, Team.Enemy, new Vector3(1, 0, 1), 32, 32, 30, 30, 10, 20, false, false, false, 1) },
        observed: new[] { new AIObservedUnitValue("r4-look-target", Kind.Crystal, Team.Player, new Vector3(4, 0, 1), 5000, 5000, 0, 0, 0, -1, false, true, true, 1) },
        objectives: new[] { new AIWorldObjectiveValue("enemy-main", 3, AIOperationGoal.DestroyEnemyCrystal, "r4-look-target",
            new Vector3(4, 0, 1), true, AIWorldFactSource.CurrentObservation, 1, 1) });
}
#endif
