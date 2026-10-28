#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Source-only planner diagnostic contracts. NOT RUN; no formal game/match claims.</summary>
public static class AIR4DiagnosticTests
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(string id, bool result)
    { if (!result) throw new InvalidOperationException("[R4Diagnostic] FAIL " + id); Debug.Log("[R4Diagnostic] PASS " + id); }
    static bool Has(AICognitivePlanner planner, string reason)
    {
        foreach (string value in planner.RejectedReasons)
            if (value == reason || value.StartsWith(reason + "|", StringComparison.Ordinal)) return true;
        return false;
    }
    public static void Run()
    {
        var cpu = typeof(AITurnBudget).GetField("clock", PrivateStatic);
        var wall = typeof(AITurnBudget).GetField("wallClock", PrivateStatic);
        var limit = typeof(AITurnBudget).GetField("limit", PrivateStatic);
        object oldCpu = cpu.GetValue(null), oldWall = wall.GetValue(null), oldLimit = limit.GetValue(null);
        try
        {
            AITurnBudget.Begin(8000);
            var world = World(); var planner = new AICognitivePlanner(); var limits = new AICognitivePlannerLimits(6);
            var action = Action(); var actions = new[] { action }; var knowledge = Knowledge();
            planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            Check("X20/completed-plan-has-no-rejection", planner.Current != null && planner.RejectedCount == 0);
            float originalScore = action.Score;

            action.APCost = 21;
            planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            Check("X20/insufficient-AP-has-own-reason", Has(planner, "insufficient_AP"));
            action.APCost = 3; action.StrategicPriority = 1;
            bool assessedProtected = false;
            planner.Refresh(world, actions, knowledge, limits, assessment: a => { assessedProtected = true; return Safe(a); });
            Check("X20/protected-band-is-not-misreported-as-unsupported", Has(planner, "protected_safety_band") && !assessedProtected);
            action.StrategicPriority = 3;

            planner.Refresh(world, actions, knowledge, limits,
                assessment: a => new AICognitiveActionAssessment { Legal = false, HardSafe = true, Confidence = 1 });
            Check("X20/illegal-formal-assessment", Has(planner, "illegal_action"));
            planner.Refresh(world, actions, knowledge, limits,
                assessment: a => new AICognitiveActionAssessment { Legal = true, HardSafe = false, Confidence = 1 });
            Check("X20/lethal-risk-is-a-safety-rejection", Has(planner, "dangerous_immediate_loss"));
            planner.Refresh(world, actions, knowledge, limits,
                assessment: a => new AICognitiveActionAssessment { Legal = true, HardSafe = true, Confidence = float.NaN });
            Check("X20/nonfinite-prediction-is-distinct", Has(planner, "non_finite_prediction"));

            var noEffect = new AIAction { ActionType = AIActionType.SkillUse, APCost = 3, StrategicPriority = 3, TargetPos = Vector3.one, Score = 8 };
            planner.Refresh(world, new[] { noEffect }, knowledge, limits, assessment: Safe);
            Check("X20/unproven-effect-is-not-invented", Has(planner, "unsupported_operator_or_effect_unproven"));
            planner.Refresh(world, actions, Array.Empty<AIKnowledgeMatch>(), limits, assessment: Safe);
            Check("X20/missing-knowledge-evidence", Has(planner, "knowledge_not_proven_for_action"));
            Check("X20/rejection-diagnostics-do-not-change-action-score", action.Score == originalScore);

            planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            planner.ObserveExecution(planner.Current.NextActionKey, false, world);
            planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            Check("X20/observed-failure-context-is-distinct", Has(planner, "repeated_failure_unchanged_observation"));

            var many = new AIAction[72];
            planner.Refresh(world, many, knowledge, limits, assessment: Safe);
            Check("X20/rejection-records-bounded-with-total-preserved", planner.RejectedCount == 32
                && planner.RejectedTotalCount == 65 && Has(planner, "candidate_scan_truncated"));
            foreach (string entry in planner.RejectedReasons)
                Check("X20/rejection-value-is-bounded", entry.Length <= 2048);
            planner.DiagnosticsEnabled = false;
            planner.Refresh(world, many, knowledge, limits, assessment: Safe);
            Check("X20/diagnostics-off-retains-only-cheap-total", planner.RejectedCount == 0 && planner.RejectedTotalCount == 65);
            planner.DiagnosticsEnabled = true;

            planner.Reset(); planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            var cancelled = planner.RefreshSteps(world, actions, knowledge, limits, assessment: Safe);
            Check("F06/planning-pauses-before-assessment", cancelled.MoveNext());
            (cancelled as IDisposable)?.Dispose();
            Check("F07/cancelled-generation-does-not-expose-ready-plan", planner.Current != null
                && planner.Current.Status == AICognitivePlanStatus.Suspended && planner.Candidates.Count == 0
                && !planner.BindPreferred(world.BoardGeneration, actions, out _)
                && Has(planner, "planning_cancelled_before_finalization"));

            planner.Reset(); planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            bool threw = false;
            var broken = planner.RefreshSteps(world, actions, knowledge, limits,
                assessment: _ => throw new InvalidOperationException("fixture-only-callback"));
            try { while (broken.MoveNext()) { } }
            catch (InvalidOperationException) { threw = true; }
            finally { (broken as IDisposable)?.Dispose(); }
            Check("I08/callback-failure-invalidates-unfinished-generation", threw && planner.Candidates.Count == 0
                && !planner.BindPreferred(world.BoardGeneration, actions, out _)
                && Has(planner, "planning_cancelled_before_finalization"));

            AITurnBudget.SetLimitWithoutRestart(0);
            planner.Refresh(world, actions, knowledge, limits, assessment: Safe);
            Check("X15/zero-budget-is-explicit-and-no-clock-reset", Has(planner, "budget_exhausted") && AITurnBudget.LimitMs == 0);
            planner.Reset();
            Check("X20/reset-drops-battle-local-diagnostics", planner.RejectedCount == 0 && planner.RejectedTotalCount == 0);
        }
        finally
        { cpu.SetValue(null, oldCpu); wall.SetValue(null, oldWall); limit.SetValue(null, oldLimit); }
    }
    static AIAction Action() => new AIAction { ActionType = AIActionType.Summon, SummonKind = Kind.Knight,
        APCost = 3, StrategicPriority = 3, TargetPos = Vector3.one, Score = 8 };
    static AICognitiveActionAssessment Safe(AIAction action)
        => new AICognitiveActionAssessment { Legal = true, HardSafe = true, Confidence = 1 };
    static IReadOnlyList<AIKnowledgeMatch> Knowledge()
    {
        AIStrategyKnowledgeCatalog.TryGet("SL-06", out var principle);
        return new[] { new AIKnowledgeMatch(principle, AITruthValue.True, 1,
            new[] { "fixture-only-paid-recruit-proof" }, Array.Empty<AIStrategyPredicate>(), string.Empty) };
    }
    static AIObservedWorldModel World() => new AIObservedWorldModel("r4-diagnostic-fixture", Team.Enemy, 1, 3, 40, 40, 20, default,
        objectives: new[] { new AIWorldObjectiveValue("goal", 1, AIOperationGoal.DestroyEnemyCrystal, "seen-objective",
            new Vector3(5, 0, 5), true, AIWorldFactSource.CurrentObservation, 1, 1) });
}
#endif
