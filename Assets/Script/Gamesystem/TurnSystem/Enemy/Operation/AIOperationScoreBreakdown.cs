using System;
using System.Collections.Generic;

/// <summary>Weighted score points, not action rewards. Penalty is non-positive.</summary>
[Serializable]
public sealed class AIOperationScoreBreakdown
{
    public float GoalAchievement, MilitaryOutcome, Survival, Efficiency, Position, Territory, Economy;
    public float Information, Defense, TimeEfficiency, ArmyUtilization, PreparationQuality, Exploitation, RiskControl;
    public float Penalty, FinalScore;
    public float Total => GoalAchievement + MilitaryOutcome + Survival + Efficiency + Position + Territory + Economy
        + Information + Defense + TimeEfficiency + ArmyUtilization + PreparationQuality + Exploitation + RiskControl + Penalty;
    public AIOperationScoreBreakdown Copy() => (AIOperationScoreBreakdown)MemberwiseClone();
}

[Serializable]
public sealed class AIOperationEvaluationResult
{
    public bool PrimaryGoalAchieved, CanReachPerfect, IsProvisional, IsStrategicAbort;
    public float GoalProgress, FinalScore, ProvisionalScore, LearningReward;
    public AIOperationRank Rank;
    public AIOperationScoreBreakdown Breakdown;
    public List<AIOperationSuccessReason> SuccessReasons = new List<AIOperationSuccessReason>();
    public List<AIOperationFailureReason> FailureReasons = new List<AIOperationFailureReason>();
}
