using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Only bounded causal operation credit is returned to eligible actions. Operation scores stay separate.</summary>
public sealed partial class AIActionReflectionSystem
{
    public bool ApplyOperationCredit(long operationId, long actionId, float reward, float contribution,
        float maximumPerAction = 1f)
    {
        if (!Enabled || state == null || state.Ended || operationId <= 0 || actionId <= 0
            || !completed.TryGetValue(actionId, out var record) || record.OperationId != operationId
            || !record.ExecutionSucceeded || record.FailureReason != AIFailureReason.None || record.Reward < 0
            || !record.MeaningfulProgress && !record.PreparationCandidate) return false;
        if (record.OperationCreditsApplied == null) record.OperationCreditsApplied = new List<long>();
        if (record.OperationCreditsApplied.Contains(operationId) || record.OperationCreditsApplied.Count >= 8) return false;
        record.OperationCreditsApplied.Add(operationId);
        contribution = Mathf.Clamp01(AIReflectionConfig.Finite(contribution));
        record.OperationContribution = Mathf.Max(record.OperationContribution, contribution);
        float delta = Mathf.Min(AIReflectionConfig.NonNegative(reward),
            Mathf.Max(0, AIReflectionConfig.NonNegative(maximumPerAction) - record.OperationCreditReward),
            Mathf.Max(0, config.ActionUpper - record.Reward),
            Mathf.Max(0, AIReflectionConfig.NonNegative(config.MaxDelayedRewardPerAction) - record.RewardBreakdown.DelayedReward));
        if (delta <= 0 || contribution <= 0) return false;
        var before = record.RewardBreakdown.Copy();
        record.RewardBreakdown.DelayedReward += delta;
        AddSuccess(record, AISuccessReason.SuccessfulPreparation);
        ApplyRecordCorrection(record, before, state.LastStartedTurn);
        record.OperationCreditReward += Math.Max(0, record.RewardBreakdown.DelayedReward - before.DelayedReward);
        return true;
    }
}
