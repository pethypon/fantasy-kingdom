using System;
using System.Collections.Generic;

/// <summary>Success explanations derive from completed goals and realized facts, not diary prose or optimistic expectations.</summary>
public sealed class AIOperationSuccessAnalyzer
{
    readonly AIOperationConfig config;
    public AIOperationSuccessAnalyzer(AIOperationConfig config)
    { this.config = config != null ? config : throw new ArgumentNullException(nameof(config)); }

    public List<AIOperationSuccessReason> Analyze(AIOperationPlan plan, AIOperationContext start, AIOperationContext end,
        AIOperationScoreBreakdown score, bool primaryGoalAchieved, bool provisional = false,
        int newTiles = 0, int newContacts = 0, int kills = 0, float preparationQuality = 0f)
    {
        var reasons = new List<AIOperationSuccessReason>();
        if (plan == null || start == null || end == null) return reasons;
        if (primaryGoalAchieved) Add(reasons, AIOperationSuccessReason.PrimaryGoalCompleted);
        int completionTurn = plan.ActualEndTurn > 0 ? plan.ActualEndTurn : end.Turn;
        if (primaryGoalAchieved && completionTurn < plan.ExpectedEndTurn) Add(reasons, AIOperationSuccessReason.FastCompletion);
        float loss = AIOperationScorer.LossRatio(start, end);
        if ((primaryGoalAchieved || newTiles > 0 || newContacts > 0 || kills > 0) && loss <= AIOperationConfig.Unit(config.LowLossRatio))
            Add(reasons, AIOperationSuccessReason.LowLosses);
        float enemyLoss = AIOperationConfig.NonNegative(end.EnemyKilledPower - start.EnemyKilledPower);
        float ownLoss = AIOperationConfig.NonNegative(end.OwnLossPower - start.OwnLossPower);
        if (kills > 0 && enemyLoss >= ownLoss && !end.OwnCrystalDestroyed) Add(reasons, AIOperationSuccessReason.GoodTrade);
        if (newTiles > 0 || newContacts > 0) Add(reasons, AIOperationSuccessReason.SuccessfulScout);
        if (end.DiversionConfirmed && end.RouteOpened) Add(reasons, AIOperationSuccessReason.SuccessfulDiversion);
        if (end.FlankConfirmed) Add(reasons, AIOperationSuccessReason.SuccessfulFlank);
        if (end.SurroundConfirmed) Add(reasons, AIOperationSuccessReason.SuccessfulSurround);
        float utilization = end.AssignedUnitCount > 0 ? end.UtilizedAssignedUnitCount / (float)end.AssignedUnitCount : end.ArmyUtilization;
        if (utilization >= AIOperationConfig.Unit(config.GoodArmyUtilizationThreshold)) Add(reasons, AIOperationSuccessReason.GoodArmyUtilization);
        if ((primaryGoalAchieved || kills > 0 || newTiles > 0) && loss <= AIOperationConfig.Unit(config.LowLossRatio)
            && !AIOperationScorer.CrystalExposed(start, end, config)) Add(reasons, AIOperationSuccessReason.GoodRiskControl);
        if (end.EconomyState < start.EconomyState || end.EconomyProductionValue > start.EconomyProductionValue)
            Add(reasons, AIOperationSuccessReason.EconomicEfficiency);
        if (Math.Max(end.PreparationContribution, preparationQuality) >= AIOperationConfig.Unit(config.StrongPreparationThreshold))
            Add(reasons, AIOperationSuccessReason.StrongPreparation);
        if (end.ExploitationProgress >= AIOperationConfig.Unit(config.SuccessfulExploitationThreshold)
            && (primaryGoalAchieved || kills > 0 || end.TerritoryCount > start.TerritoryCount)) Add(reasons, AIOperationSuccessReason.SuccessfulExploitation);
        return reasons;
    }
    static void Add(List<AIOperationSuccessReason> reasons, AIOperationSuccessReason reason)
    { if (!reasons.Contains(reason)) reasons.Add(reason); }
}
