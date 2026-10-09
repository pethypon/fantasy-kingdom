using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AIDelayedActionEvidence
{
    public long ActionId;
    public string ActorLifeId, TargetLifeId;
    public bool Preparation, TargetIsAlly, MeaningfulPosition;
    public float GrantedReward;
    public List<string> DiscoveredLifeIds = new List<string>();
    public List<string> SupportedLifeIds = new List<string>();
    public List<string> AppliedEventIds = new List<string>();
}

[Serializable]
public sealed class AIDelayedCreditState
{
    public int Version = 1;
    public List<AIDelayedActionEvidence> RecentActions = new List<AIDelayedActionEvidence>();
}

public sealed class AIDelayedOutcomeEvent
{
    public string EventId, KilledLifeId, ActorLifeId;
    public long ActionId;
    public float Reward = 1;
}

public sealed class AIDelayedCreditCorrection
{
    public long ActionId;
    public string EventId;
    public float Delta;
}

/// <summary>Recent-action causal credit only. Returned corrections are applied by the reflection owner.</summary>
public sealed class AIDelayedCreditAssigner
{
    readonly AIReflectionConfig config;
    readonly List<AIDelayedActionEvidence> recent = new List<AIDelayedActionEvidence>();
    int Window => Mathf.Clamp(config.DelayedCreditWindow, 1, 64);
    float RewardLimit => Mathf.Clamp(AIReflectionConfig.Finite(config.MaxDelayedRewardPerAction, 1), 0, 3);
    public AIDelayedCreditAssigner(AIReflectionConfig config = null)
        => this.config = config != null ? config : AIReflectionConfig.Active;

    public void Remember(AIActionRecord record)
    {
        if (record == null || record.ActionId <= 0) return;
        for (int i = 0; i < recent.Count; i++) if (recent[i].ActionId == record.ActionId) return;
        bool supportSkill = record.ActionType == AIActionType.SkillUse && record.Outcome != null
            && (record.Outcome.EffectChanged || record.Outcome.ShieldGranted || record.Outcome.HealingDone > 0
                || record.Outcome.ApRecovered > 0 || record.PreparationCandidate);
        bool eligibleType = AIFailureAnalyzer.IsMovement(record.ActionType) || supportSkill;
        var evidence = new AIDelayedActionEvidence
        {
            ActionId = record.ActionId, ActorLifeId = Identity(record.ActorLifeId), TargetLifeId = Identity(record.TargetLifeId),
            TargetIsAlly = record.Before != null && record.Before.TargetIsAlly,
            Preparation = eligibleType && record.ExecutionSucceeded && (record.MeaningfulProgress || record.PreparationCandidate),
            MeaningfulPosition = MeaningfulPosition(record),
            GrantedReward = Mathf.Clamp(AIReflectionConfig.Finite(record.RewardBreakdown?.DelayedReward ?? 0), 0, RewardLimit)
        };
        if (record.Outcome?.NewEnemyLifeIds != null)
            foreach (var id in record.Outcome.NewEnemyLifeIds)
            { string valid = Identity(id); if (valid != null && !evidence.DiscoveredLifeIds.Contains(valid) && evidence.DiscoveredLifeIds.Count < 64) evidence.DiscoveredLifeIds.Add(valid); }
        if (supportSkill && record.Before?.SupportTargetLifeIds != null)
            evidence.SupportedLifeIds = Sanitize(record.Before.SupportTargetLifeIds, 64, false);
        recent.Add(evidence);
        while (recent.Count > Window) recent.RemoveAt(0);
    }

    public List<AIDelayedCreditCorrection> ApplyOutcome(AIDelayedOutcomeEvent outcome)
    {
        var corrections = new List<AIDelayedCreditCorrection>();
        if (outcome == null || !ValidEvent(outcome.EventId) || AIReflectionConfig.Finite(outcome.Reward) <= 0) return corrections;
        float basis = Mathf.Clamp(AIReflectionConfig.Finite(config.DelayedCreditBaseReward, .5f), 0, RewardLimit)
            * Mathf.Clamp01(AIReflectionConfig.Finite(outcome.Reward));
        float decay = Mathf.Clamp01(AIReflectionConfig.Finite(config.DelayedCreditDecay, .6f));
        while (recent.Count > Window) recent.RemoveAt(0);
        for (int i = recent.Count - 1; i >= 0; i--)
        {
            var preparation = recent[i];
            if (!preparation.Preparation || outcome.ActionId > 0 && preparation.ActionId >= outcome.ActionId
                || preparation.AppliedEventIds.Contains(outcome.EventId) || !CausallyRelated(preparation, outcome)) continue;
            float delta = Mathf.Min(RewardLimit - preparation.GrantedReward, basis * Mathf.Pow(decay, recent.Count - 1 - i));
            if (delta <= 0 || preparation.AppliedEventIds.Count >= config.EventLimit) continue;
            preparation.GrantedReward += delta; preparation.AppliedEventIds.Add(outcome.EventId);
            corrections.Add(new AIDelayedCreditCorrection { ActionId = preparation.ActionId, EventId = outcome.EventId, Delta = delta });
        }
        return corrections;
    }

    static bool CausallyRelated(AIDelayedActionEvidence preparation, AIDelayedOutcomeEvent outcome)
    {
        if (!string.IsNullOrEmpty(outcome.KilledLifeId))
        {
            if (!preparation.TargetIsAlly && Same(preparation.TargetLifeId, outcome.KilledLifeId)) return true;
            if (preparation.DiscoveredLifeIds.Contains(outcome.KilledLifeId)) return true;
        }
        if (preparation.TargetIsAlly && Same(preparation.TargetLifeId, outcome.ActorLifeId)) return true;
        if (!string.IsNullOrEmpty(outcome.ActorLifeId) && preparation.SupportedLifeIds.Contains(outcome.ActorLifeId)) return true;
        return preparation.MeaningfulPosition && Same(preparation.ActorLifeId, outcome.ActorLifeId);
    }
    static bool MeaningfulPosition(AIActionRecord record)
    {
        if (!record.MeaningfulProgress || record.Before == null || record.After == null
            || !AIFailureAnalyzer.IsMovement(record.ActionType)) return false;
        bool moved = record.Before.ActorX != record.After.ActorX || record.Before.ActorY != record.After.ActorY
            || record.Before.ActorHeight != record.After.ActorHeight;
        return moved && (record.Outcome?.DistanceToObjectiveDelta > 0 || record.After.IncomingDamage < record.Before.IncomingDamage
            || record.After.LocalPowerRatio > record.Before.LocalPowerRatio
            || record.After.ActorHeight > record.Before.ActorHeight && record.After.CanAttackObservedEnemy);
    }

    public AIDelayedCreditState Capture()
    {
        var result = new AIDelayedCreditState();
        foreach (var evidence in recent) result.RecentActions.Add(Copy(evidence));
        return result;
    }
    public void Restore(AIDelayedCreditState saved, IEnumerable<AIActionRecord> recentRecords = null)
    {
        recent.Clear();
        if (saved != null && saved.Version == 1 && saved.RecentActions != null)
        {
            var seen = new HashSet<long>();
            for (int i = Math.Max(0, saved.RecentActions.Count - Window); i < saved.RecentActions.Count; i++)
            {
                var evidence = saved.RecentActions[i]; if (evidence == null || evidence.ActionId <= 0 || !seen.Add(evidence.ActionId)) continue;
                var copy = new AIDelayedActionEvidence { ActionId = evidence.ActionId,
                    ActorLifeId = Identity(evidence.ActorLifeId), TargetLifeId = Identity(evidence.TargetLifeId),
                    Preparation = evidence.Preparation, TargetIsAlly = evidence.TargetIsAlly, MeaningfulPosition = evidence.MeaningfulPosition,
                    GrantedReward = Mathf.Clamp(AIReflectionConfig.Finite(evidence.GrantedReward), 0, RewardLimit),
                    DiscoveredLifeIds = Sanitize(evidence.DiscoveredLifeIds, 64, false),
                    SupportedLifeIds = Sanitize(evidence.SupportedLifeIds, 64, false),
                    AppliedEventIds = Sanitize(evidence.AppliedEventIds, config.EventLimit, true) };
                recent.Add(copy);
            }
            // Saved causal evidence already holds the exact five-action window and event dedupe ledger.
            if (recent.Count > 0) return;
        }
        if (recentRecords != null) foreach (var record in recentRecords) Remember(record);
    }
    static List<string> Sanitize(List<string> values, int cap, bool events)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (values != null) for (int i = 0; i < values.Count && i < cap; i++)
        {
            string value = values[i];
            if ((events ? ValidEvent(value) : Identity(value) != null) && seen.Add(value)) result.Add(value);
        }
        return result;
    }
    static AIDelayedActionEvidence Copy(AIDelayedActionEvidence value) => new AIDelayedActionEvidence
    {
        ActionId = value.ActionId, ActorLifeId = value.ActorLifeId, TargetLifeId = value.TargetLifeId,
        Preparation = value.Preparation, TargetIsAlly = value.TargetIsAlly, MeaningfulPosition = value.MeaningfulPosition,
        GrantedReward = value.GrantedReward,
        DiscoveredLifeIds = value.DiscoveredLifeIds != null ? new List<string>(value.DiscoveredLifeIds) : new List<string>(),
        SupportedLifeIds = value.SupportedLifeIds != null ? new List<string>(value.SupportedLifeIds) : new List<string>(),
        AppliedEventIds = value.AppliedEventIds != null ? new List<string>(value.AppliedEventIds) : new List<string>()
    };
    static bool Same(string first, string second) => !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) && first == second;
    static string Identity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 ? value : null;
    static bool ValidEvent(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}
