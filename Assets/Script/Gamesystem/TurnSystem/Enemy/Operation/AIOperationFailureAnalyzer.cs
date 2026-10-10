using System;
using System.Collections.Generic;

/// <summary>Reports observable failure causes; rational strategic recalls do not manufacture a primary-goal failure.</summary>
public sealed class AIOperationFailureAnalyzer
{
    readonly AIOperationConfig config;
    public AIOperationFailureAnalyzer(AIOperationConfig config)
    { this.config = config != null ? config : throw new ArgumentNullException(nameof(config)); }

    public List<AIOperationFailureReason> Analyze(AIOperationPlan plan, AIOperationContext start, AIOperationContext end,
        AIOperationScoreBreakdown score, bool primaryGoalAchieved, bool provisional = false,
        bool badIntel = false, bool resourceWaste = false, bool overcommitted = false)
    {
        var reasons = new List<AIOperationFailureReason>();
        if (plan == null || start == null || end == null) return reasons;
        bool strategicAbort = plan.Status == AIOperationStatus.Aborted && plan.StrategicAbort
            && AIOperationScorer.IsStrategicAbortReason(plan.AbortReason);
        if (!provisional && !primaryGoalAchieved && !strategicAbort) Add(reasons, AIOperationFailureReason.PrimaryGoalFailed);
        float loss = AIOperationScorer.LossRatio(start, end);
        if (loss > AIOperationConfig.Unit(config.LowLossRatio)) Add(reasons, AIOperationFailureReason.ExcessiveLoss);
        if (AIOperationScorer.CrystalExposed(start, end, config)) Add(reasons, AIOperationFailureReason.CrystalExposed);
        if (end.EconomyState > start.EconomyState) Add(reasons, AIOperationFailureReason.EconomicOverstretch);
        if (badIntel || plan.AbortReason == AIOperationAbortReason.IntelligenceInvalidated) Add(reasons, AIOperationFailureReason.BadIntel);
        if (resourceWaste) Add(reasons, AIOperationFailureReason.ResourceWaste);
        if (overcommitted) Add(reasons, AIOperationFailureReason.Overcommitment);
        if (plan.AbortReason == AIOperationAbortReason.InsufficientForce) Add(reasons, AIOperationFailureReason.InsufficientForce);
        if (!strategicAbort && !primaryGoalAchieved && (end.Turn > plan.ExpectedEndTurn
            || end.Turn - plan.LastProgressTurn >= Math.Max(1, config.ReplanAfterStalledTurns))) Add(reasons, AIOperationFailureReason.SlowExecution);
        float utilization = end.AssignedUnitCount > 0 ? end.UtilizedAssignedUnitCount / (float)end.AssignedUnitCount : end.ArmyUtilization;
        if (end.AssignedUnitCount > 0 && utilization < AIOperationConfig.Unit(config.UnderutilizationThreshold))
            Add(reasons, AIOperationFailureReason.ArmyUnderutilization);
        if (plan.Steps != null) foreach (var step in plan.Steps)
        {
            if (step == null || !step.Failed) continue;
            switch (step.Type)
            {
                case AIOperationStepType.Scout:
                case AIOperationStepType.Support:
                case AIOperationStepType.Regroup: Add(reasons, AIOperationFailureReason.PoorPreparation); break;
                case AIOperationStepType.Diversion: Add(reasons, AIOperationFailureReason.FailedDiversion); break;
                case AIOperationStepType.Flank: Add(reasons, AIOperationFailureReason.FailedFlank); break;
                case AIOperationStepType.Surround: Add(reasons, AIOperationFailureReason.FailedSurround); break;
            }
        }
        if (!strategicAbort && !provisional && !primaryGoalAchieved && plan.Status == AIOperationStatus.Aborted)
            Add(reasons, AIOperationFailureReason.ObjectiveAbandoned);
        if (!provisional && primaryGoalAchieved && HasRequiredStep(plan, AIOperationStepType.Exploit)
            && end.ExploitationProgress < AIOperationConfig.Unit(config.SuccessfulExploitationThreshold))
            Add(reasons, AIOperationFailureReason.MissedExploitation);
        return reasons;
    }
    static bool HasRequiredStep(AIOperationPlan plan, AIOperationStepType type)
    {
        if (plan.Steps != null) foreach (var step in plan.Steps) if (step != null && step.Required && step.Type == type) return true;
        return false;
    }
    static void Add(List<AIOperationFailureReason> reasons, AIOperationFailureReason reason)
    { if (!reasons.Contains(reason)) reasons.Add(reason); }
}
