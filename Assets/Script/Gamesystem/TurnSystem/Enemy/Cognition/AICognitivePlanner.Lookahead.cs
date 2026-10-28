using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class AICognitivePlanner
{
    public const float MaximumLookaheadUtilityAdjustment = .15f;

    /// <summary>
    /// Reuses the existing search's published, completed iteration. It starts no work/clock,
    /// writes no AIAction score, grants no legality and never asserts future-plan Sim parity.
    /// Root must also verify the search's recorded generation/owner against the live board.
    /// </summary>
    public bool ObserveCompletedLookahead(AIObservedWorldModel world, IReadOnlyDictionary<AIAction, float> results,
        int completedDepth, string resultKind, AICognitiveAIConfig config = null)
    {
        if (world == null || results == null || results.Count > MaximumFormalCandidates || completedDepth <= 0 || completedDepth > 20 ||
            resultKind != "minimax_completed" || Current == null ||
            Current.Status != AICognitivePlanStatus.Active || validatedGeneration != world.BoardGeneration ||
            Current.BoardGenerationAtValidation != world.BoardGeneration || Current.OwnerTeam != world.ActorTeam ||
            Current.BattleId != world.BattleId || Current.StrategicPriorityBand < 2 ||
            (config != null && (!config.Enabled || !config.EnableAutonomousPlanning))) return false;

        bool updated = false;
        AICognitivePlan best = null;
        foreach (var plan in candidates)
        {
            if (!SameObservedObjective(plan, Current, world)) continue;
            if (TryCompletedDelta(plan, world, results, out float delta) && completedDepth >= plan.LookaheadCompletedDepth)
            {
                float baseUtility = plan.Utility - plan.LookaheadUtilityAdjustment;
                float normalized = delta / (1f + Mathf.Abs(delta));
                float adjustment = Mathf.Clamp(normalized * MaximumLookaheadUtilityAdjustment,
                    -MaximumLookaheadUtilityAdjustment, MaximumLookaheadUtilityAdjustment);
                if (AIWorldFact.Finite(baseUtility) && AIWorldFact.Finite(adjustment))
                {
                    plan.LookaheadCompletedDepth = completedDepth; plan.LookaheadDelta = delta;
                    plan.LookaheadUtilityAdjustment = adjustment; plan.LookaheadSourceKind = resultKind;
                    plan.Utility = baseUtility + adjustment; updated = true;
                }
            }
            // No unsearched proposal is promoted by another plan's result.
            if (plan.LookaheadCompletedDepth > 0 && plan.LookaheadSourceKind == "minimax_completed" &&
                (best == null || AICognitivePlanEvaluator.Compare(plan, best) < 0)) best = plan;
        }
        if (!updated || best == null) return false;

        // Ordinary strategy bands remain lexicographic and goal identity is unchanged.
        // The existing switch margin protects a progressing plan from small fluctuations.
        float margin = config == null ? .18f : config.SwitchMargin;
        if (!AIWorldFact.Finite(margin)) margin = .18f;
        margin = Mathf.Clamp01(margin);
        if (!ReferenceEquals(Current, best) && replanner.ShouldSwitch(Current, best, margin))
        {
            Current.Status = AICognitivePlanStatus.Validated;
            Current = best; Current.Status = AICognitivePlanStatus.Active;
            ReplanReason = "completed_existing_minimax_same_objective";
        }
        return true;
    }

    static bool SameObservedObjective(AICognitivePlan plan, AICognitivePlan current, AIObservedWorldModel world)
        => plan != null && plan.Status != AICognitivePlanStatus.Failed && plan.Status != AICognitivePlanStatus.Suspended &&
            plan.Status != AICognitivePlanStatus.Completed && plan.StrategicPriorityBand >= 2 &&
            plan.OwnerTeam == world.ActorTeam && plan.BattleId == world.BattleId &&
            plan.BoardGenerationAtValidation == world.BoardGeneration && plan.Goal == current.Goal &&
            plan.ObjectiveRef == current.ObjectiveRef && plan.HasValidDependencies() &&
            AIWorldFact.Finite(plan.Utility) && AIWorldFact.Finite(plan.LookaheadUtilityAdjustment);

    static bool TryCompletedDelta(AICognitivePlan plan, AIObservedWorldModel world,
        IReadOnlyDictionary<AIAction, float> results, out float delta)
    {
        delta = 0;
        // Existing search branches are already bounded. Inspect at most the same
        // 64 formal-candidate cap even if a diagnostic caller supplies a larger map.
        int count = 0;
        bool found = false;
        foreach (var entry in results)
        {
            if (++count > MaximumFormalCandidates) break;
            var action = entry.Key;
            if (action == null || action.APCost < 0 || action.APCost > world.OwnAP ||
                action.APCost != plan.Steps[0].ExpectedCost || action.StrategicPriority < 2 ||
                !string.IsNullOrEmpty(action.StrategicRejectReason) || !AIWorldFact.Finite(entry.Value)) continue;
            if (AICognitiveActionKey.For(action) != plan.NextActionKey) continue;
            // Equal stable bindings with contradictory values cannot be resolved by
            // Dictionary iteration order. Refuse an ambiguous diagnostic payload.
            if (found && delta != entry.Value) return false;
            delta = entry.Value; found = true;
        }
        return found;
    }
}
