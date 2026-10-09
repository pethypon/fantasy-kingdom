using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded value-only schema migration. Generalized keys and learned values are never rewritten.</summary>
public static class AIReflectionMigration
{
    public static bool TryUpgradeProfile(AIActionLearningProfile profile, AIReflectionConfig config = null)
    {
        config = config != null ? config : AIReflectionConfig.Active;
        if (!ValidProfileHeader(profile, config)) return false;
        var valid = new List<AIActionKnowledgeEntry>(profile.Entries.Count);
        var keys = new HashSet<string>();
        foreach (var entry in profile.Entries)
        {
            if (!ValidEntry(entry, config) || !keys.Add(entry.ContextKey + "\n" + entry.ActionKey)) continue;
            if (entry.FailureReasons == null) entry.FailureReasons = new List<FailureReasonCount>();
            if (entry.SuccessReasons == null) entry.SuccessReasons = new List<SuccessReasonCount>();
            valid.Add(entry);
        }
        profile.Entries = valid;
        if (profile.CompletedBattleIds == null) profile.CompletedBattleIds = new List<string>();
        if (profile.StrategyOutcomes == null) profile.StrategyOutcomes = new List<AIReflectionStrategyOutcome>();
        profile.StrategyOutcomes.RemoveAll(item => !ValidStrategy(item));
        profile.SchemaVersion = 2; profile.RebuildIndex();
        return true;
    }

    public static bool UpgradeBattle(AIReflectionBattleState state, AIReflectionConfig config = null)
    {
        config = config != null ? config : AIReflectionConfig.Active;
        if (state == null || state.SchemaVersion != 1 && state.SchemaVersion != 2
            || string.IsNullOrEmpty(state.BattleId) || state.BattleId.Length > 256
            || state.Faction != Team.Player && state.Faction != Team.Enemy
            || state.OwnTurns < 0 || state.OwnTurns > 1000000 || state.DiaryCount < 0 || state.DiaryCount > 1000000
            || state.NextActionId < 0 || state.NextActionId > 1000000000000L
            || !Finite(state.BattleReward) || !Finite(state.TurnReward)
            || state.RecentRecords != null && state.RecentRecords.Count > config.RecordLimit
            || state.Kills != null && state.Kills.Count > config.EventLimit
            || state.Artifacts != null && state.Artifacts.Count > config.EventLimit
            || state.StableTurns != null && state.StableTurns.Count > config.EventLimit
            || state.StrategyUse != null && state.StrategyUse.Count > 32) return false;
        bool legacy = state.SchemaVersion == 1;
        if (!UpgradeInterval(state.Interval ?? (state.Interval = new AIReflectionInterval()), config, legacy)
            || !UpgradeInterval(state.BattleSummary ?? (state.BattleSummary = new AIReflectionInterval()), config, legacy)
            || state.LastDiaryInterval != null && !UpgradeInterval(state.LastDiaryInterval, config, legacy)) return false;
        if (state.RecentRecords == null) state.RecentRecords = new List<AIActionRecord>();
        var ids = new HashSet<long>();
        state.RecentRecords.RemoveAll(record => !UpgradeRecord(record, state.NextActionId, config, legacy) || !ids.Add(record.ActionId));
        if (state.Kills == null) state.Kills = new List<AIReflectionRewardEvent>();
        if (state.Artifacts == null) state.Artifacts = new List<AIReflectionRewardEvent>();
        state.Kills.RemoveAll(item => !ValidEvent(item, state.NextActionId));
        state.Artifacts.RemoveAll(item => !ValidEvent(item, state.NextActionId));
        if (state.StableTurns == null) state.StableTurns = new List<int>();
        state.StableTurns.RemoveAll(turn => turn < 0 || turn > 1000000);
        if (state.StrategyUse == null) state.StrategyUse = new List<AIReflectionStrategyUse>();
        state.StrategyUse.RemoveAll(item => item == null || !Enum.IsDefined(typeof(TurnStrategy), item.Strategy)
            || item.ThreatBand < 0 || item.ThreatBand > 2 || item.OwnTurns < 0 || item.OwnTurns > 1000000);
        if (state.ObservedEnemyLifeIds == null) state.ObservedEnemyLifeIds = new List<string>();
        if (state.ObservedEnemyLifeIds.Count > config.EventLimit) return false;
        state.ObservedEnemyLifeIds.RemoveAll(id => string.IsNullOrEmpty(id) || id.Length > 128);
        if (state.EconomyRewardState == null)
            state.EconomyRewardState = new AIEconomyRewardState { HasGrantedRecovery = legacy && state.TotalStableTurns > 0 };
        if (state.DelayedCreditState == null) state.DelayedCreditState = new AIDelayedCreditState();
        if (state.ArmyUtilizationState == null) state.ArmyUtilizationState = new AIArmyUtilizationState();
        if (state.LearningProfile != null && !TryUpgradeProfile(state.LearningProfile, config)) state.LearningProfile = null;
        if (legacy)
        {
            state.LastDiaryOwnTurn = state.DiaryCount == 0 ? -1 : state.Ended ? state.OwnTurns
                : state.OwnTurns / Math.Max(1, config.DiaryIntervalOwnTurns) * Math.Max(1, config.DiaryIntervalOwnTurns);
            state.LastDiaryResult = state.LastDiaryOwnTurn < 0 ? null : state.Ended ? state.Result : "INTERVAL";
        }
        state.SchemaVersion = 2;
        return true;
    }

    internal static bool ValidProfileHeader(AIActionLearningProfile profile, AIReflectionConfig config)
    {
        if (profile == null || profile.SchemaVersion != 1 && profile.SchemaVersion != 2
            || string.IsNullOrEmpty(profile.ProfileId) || profile.ProfileId.Length > 80
            || profile.BattlesPlayed < 0 || profile.BattlesPlayed > 1000000 || profile.Wins < 0 || profile.Losses < 0
            || profile.Wins > profile.BattlesPlayed || profile.Losses > profile.BattlesPlayed || profile.Sequence < 0
            || profile.Entries == null || profile.Entries.Count > config.EntryLimit
            || profile.CompletedBattleIds != null && profile.CompletedBattleIds.Count > 128
            || profile.StrategyOutcomes != null && profile.StrategyOutcomes.Count > 32) return false;
        if (profile.CompletedBattleIds != null)
            foreach (var id in profile.CompletedBattleIds) if (id == null || id.Length > 256) return false;
        return true;
    }
    internal static bool ValidEntry(AIActionKnowledgeEntry entry, AIReflectionConfig config)
    {
        if (entry == null || string.IsNullOrEmpty(entry.ContextKey) || entry.ContextKey.Length > 512
            || string.IsNullOrEmpty(entry.ActionKey) || entry.ActionKey.Length > 256
            || entry.Samples < 0 || entry.Samples > 1000000 || entry.SuccessCount < 0 || entry.FailureCount < 0
            || entry.SuccessCount > entry.Samples || entry.FailureCount > entry.Samples
            || entry.LastUseSequence < 0 || !Finite(entry.LearnedValue) || entry.LearnedValue < config.Lower || entry.LearnedValue > config.Upper
            || !Finite(entry.CumulativeReward) || !Finite(entry.AverageReward) || !Finite(entry.MaxReward) || !Finite(entry.MinReward)
            || entry.LastBattleId != null && entry.LastBattleId.Length > 256
            || entry.FailureReasons != null && entry.FailureReasons.Count > 32
            || entry.SuccessReasons != null && entry.SuccessReasons.Count > 32) return false;
        if (entry.FailureReasons != null)
            foreach (var reason in entry.FailureReasons)
                if (reason == null || reason.Count < 0 || reason.Count > 1000000 || !Enum.IsDefined(typeof(AIFailureReason), reason.Reason)) return false;
        if (entry.SuccessReasons != null)
            foreach (var reason in entry.SuccessReasons)
                if (reason == null || reason.Count < 0 || reason.Count > 1000000 || !Enum.IsDefined(typeof(AISuccessReason), reason.Reason)) return false;
        return true;
    }
    internal static bool ValidStrategy(AIReflectionStrategyOutcome strategy) => strategy != null
        && Enum.IsDefined(typeof(TurnStrategy), strategy.Strategy) && strategy.ThreatBand >= 0 && strategy.ThreatBand <= 2
        && strategy.Battles >= 0 && strategy.Battles <= 1000000 && strategy.OwnTurns >= 0 && strategy.OwnTurns <= 1000000
        && strategy.Wins >= 0 && strategy.Losses >= 0 && strategy.Draws >= 0
        && strategy.Wins <= strategy.Battles && strategy.Losses <= strategy.Battles && strategy.Draws <= strategy.Battles
        && Finite(strategy.CumulativeBattleReward);

    static bool UpgradeRecord(AIActionRecord record, long lastActionId, AIReflectionConfig config, bool legacy)
    {
        if (record == null || record.Before == null || record.After == null || record.Outcome == null
            || record.ActionId <= 0 || record.ActionId > lastActionId || !Finite(record.Reward)
            || !Finite(record.BaseScore) || !Finite(record.LearnedModifier) || !Finite(record.FinalScore)
            || string.IsNullOrEmpty(record.ContextKey) || record.ContextKey.Length > 512
            || string.IsNullOrEmpty(record.ActionKey) || record.ActionKey.Length > 256
            || !Enum.IsDefined(typeof(AIActionType), record.ActionType) || !Enum.IsDefined(typeof(AIFailureReason), record.FailureReason)
            || record.SuccessReasons != null && record.SuccessReasons.Count > 32) return false;
        if (record.SuccessReasons == null) record.SuccessReasons = new List<AISuccessReason>();
        record.SuccessReasons.RemoveAll(reason => !Enum.IsDefined(typeof(AISuccessReason), reason));
        bool missingBreakdown = record.RewardBreakdown == null;
        if (legacy || missingBreakdown) record.RewardBreakdown = AIReflectionInterval.LegacyBreakdown(record);
        if (legacy || missingBreakdown) record.ImmediateReward = record.Reward;
        if (!Finite(record.RewardBreakdown.Total) || !Finite(record.ImmediateReward) || !Finite(record.LearningValueChange)) return false;
        if (record.Before.VisibleEnemyLifeIds == null) record.Before.VisibleEnemyLifeIds = new List<string>();
        if (record.After.VisibleEnemyLifeIds == null) record.After.VisibleEnemyLifeIds = new List<string>();
        if (record.Before.VisibleEnemyLifeIds.Count > config.EventLimit || record.After.VisibleEnemyLifeIds.Count > config.EventLimit) return false;
        if (record.DecisionTrace == null) record.DecisionTrace = AIReflectionDecisionTrace.Build(null, record);
        return record.DecisionTrace.DecisionFacts == null || record.DecisionTrace.DecisionFacts.Count <= 32;
    }
    static bool UpgradeInterval(AIReflectionInterval interval, AIReflectionConfig config, bool legacy)
    {
        if (!Finite(interval.Reward) || !Finite(interval.PenaltyReward) || !Finite(interval.VictoryReward)
            || interval.Patterns != null && interval.Patterns.Count > config.EntryLimit
            || interval.Failures != null && interval.Failures.Count > 32
            || interval.Successes != null && interval.Successes.Count > 32
            || interval.TraceExamples != null && interval.TraceExamples.Count > 3) return false;
        interval.PatternLimit = config.EntryLimit;
        if (interval.Patterns == null) interval.Patterns = new List<AIReflectionPatternSummary>();
        interval.Patterns.RemoveAll(pattern => pattern == null || pattern.Key == null || pattern.Key.Length > 800 || pattern.Samples < 0
            || !Finite(pattern.Reward) || !Finite(pattern.LearnedChange));
        if (interval.Failures == null) interval.Failures = new List<FailureReasonCount>();
        interval.Failures.RemoveAll(reason => reason == null || reason.Count < 0 || !Enum.IsDefined(typeof(AIFailureReason), reason.Reason));
        if (interval.Successes == null) interval.Successes = new List<SuccessReasonCount>();
        interval.Successes.RemoveAll(reason => reason == null || reason.Count < 0 || !Enum.IsDefined(typeof(AISuccessReason), reason.Reason));
        if (interval.TraceExamples == null) interval.TraceExamples = new List<AIReflectionDecisionTrace>();
        interval.TraceExamples.RemoveAll(trace => trace == null || trace.DecisionFacts != null && trace.DecisionFacts.Count > 32);
        if (legacy || interval.Breakdown == null)
        {
            interval.Breakdown = new AIRewardBreakdown { Combat = interval.NormalKills + interval.FormationKills * 2,
                Artifact = interval.Artifacts, Economy = interval.StableTurns };
            interval.Breakdown.Efficiency = interval.Reward - interval.VictoryReward - interval.Breakdown.Total;
        }
        return Finite(interval.Breakdown.Total);
    }
    static bool ValidEvent(AIReflectionRewardEvent item, long lastActionId) => item != null && !string.IsNullOrEmpty(item.Id)
        && item.Id.Length <= 128 && item.ActionId >= 0 && item.ActionId <= lastActionId;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
