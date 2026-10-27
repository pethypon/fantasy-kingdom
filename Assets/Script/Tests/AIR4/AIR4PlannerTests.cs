#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>R4 proposal contracts. NOT RUN by the development-only task; these are not match/Sim-parity claims.</summary>
public static class AIR4PlannerTests
{
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    static int count;
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[AIR4Planner] FAIL " + id); count++; Debug.Log("[AIR4Planner] PASS " + id); }

    public static void Run()
    {
        count = 0;
        var cpu = typeof(AITurnBudget).GetField("clock", StaticPrivate);
        var wall = typeof(AITurnBudget).GetField("wallClock", StaticPrivate);
        var limit = typeof(AITurnBudget).GetField("limit", StaticPrivate);
        object oldCpu = cpu.GetValue(null), oldWall = wall.GetValue(null), oldLimit = limit.GetValue(null);
        var actorHost = new GameObject("R4 planner own fixture"); actorHost.SetActive(false);
        var targetHost = new GameObject("R4 planner observed fixture"); targetHost.SetActive(false);
        try
        {
            AITurnBudget.Begin(8000);
            var actor = actorHost.AddComponent<Status>(); actor.kind = Kind.Knight; actor.RestoreReflectionLifeId("r4-own");
            actor.transform.position = new Vector3(1, 0, 1);
            var target = targetHost.AddComponent<Status>(); target.kind = Kind.Crystal; target.RestoreReflectionLifeId("r4-target");
            target.transform.position = new Vector3(4, 0, 1);
            var move = new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = new Vector3(2, 0, 1), APCost = 3, Score = 8, StrategicPriority = 3 };
            var attack = new AIAction { ActionType = AIActionType.Attack, Unit = actor, TargetUnit = target, TargetPos = target.transform.position, APCost = 5, Score = 9, StrategicPriority = 3 };
            var actions = new List<AIAction> { move, attack };
            var world = World(); var matches = Matches("CH-06", "CH-10");
            var planner = new AICognitivePlanner(); var limits = new AICognitivePlannerLimits(6);
            planner.Refresh(world, actions, matches, limits, assessment: Assessment);
            Check("P01 direct and legal approach produce different means for same objective", planner.Candidates.Count == 2 &&
                planner.Candidates[0].PrimaryMethod != planner.Candidates[1].PrimaryMethod && planner.Candidates[0].Goal == planner.Candidates[1].Goal);
            Check("P10 preferred action is an original formal candidate", planner.BindPreferred(1, actions, out var bound) && actions.Contains(bound));
            Check("P11 a different board generation invalidates binding", !planner.BindPreferred(2, actions, out _));
            Check("P15 plan records actual source knowledge evidence", planner.Current.KnowledgeIds.Count > 0 && planner.Current.ExplanationEvidenceIds.Contains("fixture-formal-evidence"));
            foreach (var plan in planner.Candidates)
                Check("P05 response classes never claim an invented legal enemy move " + plan.PrimaryMethod,
                    plan.EnemyResponses.Count > 0 && plan.EnemyResponses.TrueForAll(r => r.Legality != AICognitiveResponseLegality.LegalKnown && r.IsPrediction));

            planner.Refresh(world, actions, FalseMatches("CH-06", "CH-10"), limits, assessment: Assessment);
            Check("P02 contradicted knowledge cannot generate an executable plan", planner.Candidates.Count == 0);
            planner.Refresh(world, actions, matches, limits, assessment: a => new AICognitiveActionAssessment { Legal = true, HardSafe = false, Confidence = 1 });
            Check("P12 hard safety assessment rejects all planning hints", planner.Candidates.Count == 0);
            planner.Refresh(world, actions, matches, limits, emergency: true, assessment: Assessment);
            Check("I02 emergency defense remains protected", !planner.BindPreferred(1, actions, out _) && planner.ReplanReason == "protected_emergency_defense");

            planner.Refresh(world, actions, matches, limits, assessment: a => new AICognitiveActionAssessment { Legal = true, HardSafe = true, Confidence = float.NaN });
            Check("P07 nonfinite prediction is safely rejected", planner.Candidates.Count == 0);
            planner.Refresh(World(ap: 1), actions, matches, limits, assessment: Assessment);
            Check("S04 actions cannot exceed current shared AP", planner.Candidates.Count == 0);

            planner.Refresh(world, actions, matches, new AICognitivePlannerLimits(1, 1, 1, 1, 1), assessment: Assessment);
            Check("P06 independent upper bounds apply", planner.Candidates.Count == 1 && planner.Current.Steps.Count == 1 && planner.Current.EnemyResponses.Count == 1);
            var duplicateMoves = new List<AIAction> { move, move, move };
            planner.Refresh(world, duplicateMoves, matches, limits, assessment: Assessment);
            Check("P09 same means do not fill the candidate beam", planner.Candidates.Count == 1);

            planner.Refresh(world, new[] { move }, Matches("SL-01"), limits, assessment: Assessment);
            Check("SL01-alias positive formal firing-position knowledge binds a current move", planner.Current != null &&
                planner.Current.PrimaryMethod == "IMPROVE_POSITION" && planner.BindPreferred(1, new[] { move }, out _));
            planner.Refresh(world, new[] { move }, FalseMatches("SL-01"), limits, assessment: Assessment);
            Check("SL01-alias negative firing-position evidence does not produce a plan", planner.Candidates.Count == 0);
            var summon = new AIAction { ActionType = AIActionType.Summon, TargetPos = new Vector3(2, 0, 1),
                SummonKind = Kind.Knight, APCost = 3, Score = 4, StrategicPriority = 3 };
            planner.Refresh(world, new[] { summon }, Matches("SL-06"), limits, assessment: Assessment);
            Check("SL06-alias positive missing-role knowledge binds an existing paid-summon candidate", planner.Current != null &&
                planner.Current.PrimaryMethod == "REINFORCE" && planner.BindPreferred(1, new[] { summon }, out _));
            planner.Refresh(world, new[] { summon }, FalseMatches("SL-06"), limits, assessment: Assessment);
            Check("SL06-alias negative missing-role evidence does not produce a plan", planner.Candidates.Count == 0);

            planner.Refresh(world, actions, matches, limits, assessment: Assessment,
                actionKnowledge: a => ReferenceEquals(a, move) ? Matches("SL-01") : FalseMatches("CH-10"));
            Check("per-action proof never grants one action another action's tactical evidence", planner.Candidates.Count == 1 &&
                planner.Current.PrimaryMethod == "IMPROVE_POSITION" && planner.BindPreferred(1, actions, out var scoped) && ReferenceEquals(scoped, move));
            planner.Refresh(world, actions, matches, limits, assessment: Assessment, actionKnowledge: a => null);
            Check("missing per-action proof does not fall back to unrelated global proof", planner.Candidates.Count == 0);

            var outside = new AIAction { ActionType = AIActionType.Move, Unit = actor, TargetPos = new Vector3(3, 0, -1),
                APCost = 3, Score = 8, StrategicPriority = 3 };
            planner.Refresh(world, new[] { outside }, Matches("SL-01"), limits, assessment: Assessment);
            Check("40x40-input negative horizontal coordinate is rejected inside R4", planner.Candidates.Count == 0);
            planner.Refresh(World(width: 41), new[] { outside }, Matches("SL-01"), limits, assessment: Assessment);
            Check("40x40-input guard does not normalize a differently sized legacy map", planner.Candidates.Count == 1);

            var cyclic = new AICognitivePlan();
            cyclic.Steps.Add(new AIPlanOperator { StepId = 0, Dependencies = new List<int> { 1 } });
            cyclic.Steps.Add(new AIPlanOperator { StepId = 1, Dependencies = new List<int> { 0 } });
            Check("P08 cyclic dependency graph is rejected", !cyclic.HasValidDependencies());
            var valid = new AICognitivePlan(); valid.Steps.Add(new AIPlanOperator { StepId = 0 });
            valid.Steps.Add(new AIPlanOperator { StepId = 1, Dependencies = new List<int> { 0 } });
            Check("dependency-contract forward dependency remains observation-bound (not SC01 execution)", valid.HasValidDependencies());
            Check("operation-bridge known movement operator translates", AIOperationManager.TryTranslateCognitiveOperator("OPEN_LINE", out var translated) && translated == AIOperationStepType.Approach);
            Check("operation-bridge economic intent is not falsely translated to an attack", !AIOperationManager.TryTranslateCognitiveOperator("PLAN_PRODUCTION", out _));
            Check("operation-bridge unknown operator is rejected", !AIOperationManager.TryTranslateCognitiveOperator("NOT_A_GAME_ACTION", out _));

            planner.Refresh(world, actions, matches, limits, assessment: Assessment);
            var saved = planner.Current;
            planner.RestoreIntent(saved);
            Check("SV03 restore keeps goal but discards pending orders", planner.Current.Goal == saved.Goal && planner.Current.Steps.Count == 0 && !planner.BindPreferred(1, actions, out _));
            planner.Refresh(world, actions, matches, limits, assessment: Assessment);
            Check("P04 final objective survives fresh rebinding", planner.Current.Goal == saved.Goal && planner.Current.ObjectiveRef == saved.ObjectiveRef);
            string failedKey = planner.Current.NextActionKey, failedMethod = planner.Current.PrimaryMethod;
            planner.ObserveExecution(failedKey, false, world);
            planner.Refresh(world, actions, matches, limits, assessment: Assessment);
            Check("H06 failed unchanged means are suppressed while alternatives remain", planner.Current == null || planner.Current.PrimaryMethod != failedMethod);

            AITurnBudget.SetLimitWithoutRestart(0);
            planner.Refresh(world, actions, matches, limits, assessment: Assessment);
            Check("X-low-budget fallback never starts another thinking clock", !planner.BindPreferred(1, actions, out _) && AITurnBudget.LimitMs == 0);
            Debug.Log("[AIR4Planner] completed " + count + " source-contract assertions");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actorHost); UnityEngine.Object.DestroyImmediate(targetHost);
            cpu.SetValue(null, oldCpu); wall.SetValue(null, oldWall); limit.SetValue(null, oldLimit);
        }
    }

    static AICognitiveActionAssessment Assessment(AIAction action) => new AICognitiveActionAssessment {
        Legal = true, HardSafe = true, Confidence = 1, ExpectedGoalProgress = action.ActionType == AIActionType.Attack ? .2f : .1f,
        EffectiveDamage = action.ActionType == AIActionType.Attack ? 10 : 0 };
    static List<AIKnowledgeMatch> Matches(params string[] ids)
    {
        var result = new List<AIKnowledgeMatch>();
        foreach (string id in ids)
        { AIStrategyKnowledgeCatalog.TryGet(id, out var principle); result.Add(new AIKnowledgeMatch(principle, AITruthValue.True, 1,
            new[] { "fixture-formal-evidence" }, Array.Empty<AIStrategyPredicate>(), string.Empty)); }
        return result;
    }
    static List<AIKnowledgeMatch> FalseMatches(params string[] ids)
    {
        var result = new List<AIKnowledgeMatch>();
        foreach (string id in ids)
        { AIStrategyKnowledgeCatalog.TryGet(id, out var principle); result.Add(new AIKnowledgeMatch(principle, AITruthValue.False, 1,
            new[] { "fixture-contradiction" }, Array.Empty<AIStrategyPredicate>(), "Contradicted")); }
        return result;
    }
    static AIObservedWorldModel World(int ap = 20, int width = 40) => new AIObservedWorldModel("r4-fixture", Team.Enemy, 1, 1, width, 40, ap, default,
        own: new[] { new AIObservedUnitValue("r4-own", Kind.Knight, Team.Enemy, new Vector3(1, 0, 1), 32, 32, 30, 30, 10, ap, false, false, false, 1) },
        observed: new[] { new AIObservedUnitValue("r4-target", Kind.Crystal, Team.Player, new Vector3(4, 0, 1), 5000, 5000, 0, 0, 0, -1, false, true, true, 1) },
        objectives: new[] { new AIWorldObjectiveValue("enemy-main", 3, AIOperationGoal.DestroyEnemyCrystal, "r4-target", new Vector3(4, 0, 1), true, AIWorldFactSource.CurrentObservation, 1, 1) });
}
#endif
