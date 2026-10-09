using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AIActionKnowledgeEntry
{
    public string ContextKey, ActionKey;
    public float LearnedValue, CumulativeReward, AverageReward, MaxReward, MinReward;
    public int Samples, SuccessCount, FailureCount, LastTurn;
    public long LastUseSequence;
    public string LastBattleId;
    public List<FailureReasonCount> FailureReasons = new List<FailureReasonCount>();
    public List<SuccessReasonCount> SuccessReasons = new List<SuccessReasonCount>();
}

[Serializable]
public sealed class AIReflectionStrategyOutcome
{
    public TurnStrategy Strategy;
    public int ThreatBand, Battles, Wins, Losses, Draws, OwnTurns;
    public float CumulativeBattleReward;
}

/// <summary>Bounded, generalized contextual learning; contains no map coordinates or runtime unit IDs.</summary>
[Serializable]
public sealed class AIActionLearningProfile
{
    public int SchemaVersion = 2;
    public string ProfileId;
    public int BattlesPlayed, Wins, Losses;
    public long Sequence;
    public List<string> CompletedBattleIds = new List<string>();
    public List<AIReflectionStrategyOutcome> StrategyOutcomes = new List<AIReflectionStrategyOutcome>();
    public List<AIActionKnowledgeEntry> Entries = new List<AIActionKnowledgeEntry>();
    [NonSerialized] Dictionary<(string context, string action), AIActionKnowledgeEntry> index;

    public AIActionKnowledgeEntry Find(string contextKey, string actionKey)
    {
        EnsureIndex();
        index.TryGetValue((contextKey, actionKey), out var entry);
        return entry;
    }
    public float GetModifier(string contextKey, string actionKey, AIReflectionConfig config, float similarity = 1f)
    {
        if (config == null || !config.EnableReflection || !config.ApplyLearningToSelection) return 0;
        var entry = Find(contextKey, actionKey);
        // Version-one keys describe the same factual bands without the newly optional match phase.
        if (entry == null && contextKey != null)
        {
            int phase = contextKey.LastIndexOf("|phase", StringComparison.Ordinal);
            if (phase >= 0) entry = Find(contextKey.Substring(0, phase), actionKey);
        }
        if (entry == null || entry.Samples < Mathf.Clamp(config.MinimumSamplesForSelection, 1, 1000000)) return 0;
        float confidence = Mathf.Clamp01(entry.Samples / (float)Mathf.Clamp(config.FullConfidenceSamples, 1, 1000000));
        float modifier = Mathf.Clamp(AIReflectionConfig.Finite(entry.LearnedValue), config.Lower, config.Upper)
            * confidence * Mathf.Clamp01(AIReflectionConfig.Finite(similarity))
            * Mathf.Clamp(AIReflectionConfig.Finite(config.LearningInfluenceMultiplier, 1), 0, 2);
        return Mathf.Clamp(modifier, config.Lower, config.Upper);
    }
    public float Learn(AIActionRecord record, AIReflectionConfig config)
    {
        if (record == null || string.IsNullOrEmpty(record.ContextKey) || string.IsNullOrEmpty(record.ActionKey)) return 0;
        EnsureIndex();
        var entry = Find(record.ContextKey, record.ActionKey);
        if (entry == null)
        {
            if (Entries.Count >= config.EntryLimit) PruneOne();
            entry = new AIActionKnowledgeEntry { ContextKey = record.ContextKey, ActionKey = record.ActionKey };
            Entries.Add(entry); index[(entry.ContextKey, entry.ActionKey)] = entry;
        }
        float old = entry.LearnedValue;
        float alpha = LearningAlpha(entry.Samples + 1, config);
        float reward = AIReflectionConfig.Finite(record.Reward);
        entry.LearnedValue = Mathf.Clamp(old + alpha * (reward - old), config.Lower, config.Upper);
        entry.Samples = Math.Min(1000000, entry.Samples + 1);
        if (record.StrategicOutcome == AIStrategicOutcome.Progress) entry.SuccessCount = Math.Min(1000000, entry.SuccessCount + 1);
        if (record.StrategicOutcome == AIStrategicOutcome.Failure) entry.FailureCount = Math.Min(1000000, entry.FailureCount + 1);
        entry.CumulativeReward = Mathf.Clamp(entry.CumulativeReward + reward, -1000000, 1000000);
        entry.AverageReward = entry.CumulativeReward / Math.Max(1, entry.Samples);
        entry.MaxReward = entry.Samples == 1 ? reward : Mathf.Max(entry.MaxReward, reward);
        entry.MinReward = entry.Samples == 1 ? reward : Mathf.Min(entry.MinReward, reward);
        entry.LastTurn = record.Turn; entry.LastBattleId = record.BattleId;
        entry.LastUseSequence = ++Sequence;
        CountFailure(entry.FailureReasons, record.FailureReason);
        if (record.SuccessReasons != null)
            foreach (var reason in record.SuccessReasons) CountSuccess(entry.SuccessReasons, reason);
        return entry.LearnedValue - old;
    }
    public float CorrectReward(AIActionRecord record, float delta, AIReflectionConfig config)
    {
        delta = AIReflectionConfig.Finite(delta);
        var entry = Find(record.ContextKey, record.ActionKey);
        if (entry == null || entry.Samples <= 0) return 0;
        float old = entry.LearnedValue;
        float alpha = LearningAlpha(entry.Samples, config);
        entry.LearnedValue = Mathf.Clamp(old + alpha * delta, config.Lower, config.Upper);
        entry.CumulativeReward = Mathf.Clamp(entry.CumulativeReward + delta, -1000000, 1000000);
        entry.AverageReward = entry.CumulativeReward / entry.Samples;
        entry.MaxReward = Mathf.Max(entry.MaxReward, record.Reward);
        entry.MinReward = Mathf.Min(entry.MinReward, record.Reward);
        return entry.LearnedValue - old;
    }
    public void RebuildIndex() { index = null; EnsureIndex(); }
    public void RecordStrategyResult(AIReflectionBattleState battle, string result, float reward)
    {
        if (battle?.StrategyUse == null) return;
        int totalTurns = 0;
        foreach (var usage in battle.StrategyUse) totalTurns += Math.Max(0, usage.OwnTurns);
        foreach (var usage in battle.StrategyUse)
        {
            if (usage.OwnTurns <= 0) continue;
            AIReflectionStrategyOutcome outcome = null;
            foreach (var candidate in StrategyOutcomes)
                if (candidate.Strategy == usage.Strategy && candidate.ThreatBand == usage.ThreatBand) { outcome = candidate; break; }
            if (outcome == null)
            {
                if (StrategyOutcomes.Count >= 32) continue;
                outcome = new AIReflectionStrategyOutcome { Strategy = usage.Strategy, ThreatBand = usage.ThreatBand };
                StrategyOutcomes.Add(outcome);
            }
            outcome.Battles = Math.Min(1000000, outcome.Battles + 1);
            if (result == "VICTORY") outcome.Wins = Math.Min(1000000, outcome.Wins + 1);
            if (result == "DEFEAT") outcome.Losses = Math.Min(1000000, outcome.Losses + 1);
            if (result == "DRAW") outcome.Draws = Math.Min(1000000, outcome.Draws + 1);
            outcome.OwnTurns = (int)Math.Min(1000000, (long)outcome.OwnTurns + usage.OwnTurns);
            // A battle-level reward is distributed only between strategic outcome statistics, never actions.
            outcome.CumulativeBattleReward = Mathf.Clamp(outcome.CumulativeBattleReward
                + AIReflectionConfig.Finite(reward) * usage.OwnTurns / Math.Max(1, totalTurns), -1000000, 1000000);
        }
    }
    static float LearningAlpha(int samples, AIReflectionConfig config)
    {
        float maximum = Mathf.Clamp(AIReflectionConfig.Finite(config.ReflectionLearningRate, .2f), .001f, 1);
        float minimum = Mathf.Clamp(AIReflectionConfig.Finite(config.MinimumLearningRate, .02f), .001f, maximum);
        return Mathf.Clamp(maximum / Mathf.Sqrt(Math.Max(1, samples)), minimum, maximum);
    }
    void EnsureIndex()
    {
        if (index != null) return;
        index = new Dictionary<(string, string), AIActionKnowledgeEntry>(Entries?.Count ?? 0);
        if (Entries == null) Entries = new List<AIActionKnowledgeEntry>();
        foreach (var entry in Entries)
            if (entry != null) index[(entry.ContextKey, entry.ActionKey)] = entry;
    }
    void PruneOne()
    {
        int remove = 0; double lowest = double.MaxValue;
        for (int i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            double importance = entry.Samples + Math.Abs(entry.LearnedValue) * 10
                - Math.Max(0, Sequence - entry.LastUseSequence) * .001;
            if (importance < lowest) { lowest = importance; remove = i; }
        }
        var victim = Entries[remove]; index.Remove((victim.ContextKey, victim.ActionKey));
        Entries.RemoveAt(remove);
    }
    internal static void CountFailure(List<FailureReasonCount> reasons, AIFailureReason reason)
    {
        if (reason == AIFailureReason.None) return;
        foreach (var item in reasons) if (item.Reason == reason) { item.Count = Math.Min(1000000, item.Count + 1); return; }
        reasons.Add(new FailureReasonCount { Reason = reason, Count = 1 });
    }
    internal static void CountSuccess(List<SuccessReasonCount> reasons, AISuccessReason reason)
    {
        if (reason == AISuccessReason.None || reasons == null) return;
        foreach (var item in reasons) if (item.Reason == reason) { item.Count = Math.Min(1000000, item.Count + 1); return; }
        reasons.Add(new SuccessReasonCount { Reason = reason, Count = 1 });
    }
}

public static class AIActionRewardEvaluator
{
    public static float Evaluate(AIActionRecord record, AIReflectionConfig config)
    {
        if (record == null) return 0;
        if (config == null) config = AIReflectionConfig.Active;
        var breakdown = new AIRewardBreakdown(); record.RewardBreakdown = breakdown;
        var outcome = record.Outcome; var before = record.Before; var after = record.After;
        if (outcome != null)
        {
            int kills = Math.Max(0, outcome.EnemyKills), formation = Mathf.Clamp(outcome.FormationKills, 0, kills);
            breakdown.Combat = (kills - formation) * AIReflectionConfig.NonNegative(config.NormalKillRewardValue)
                + formation * AIReflectionConfig.NonNegative(config.FormationKillRewardValue);
            breakdown.Artifact = Math.Max(0, outcome.ArtifactsAcquired) * AIReflectionConfig.NonNegative(config.ArtifactRewardValue);
            if (formation > 0) Success(record, AISuccessReason.FormationKill);
            else if (kills > 0) Success(record, AISuccessReason.EnemyKill);
            if (outcome.ArtifactsAcquired > 0) Success(record, AISuccessReason.ArtifactObtained);
            if (record.ExecutionSucceeded && before != null && after != null) EvaluateFacts(record, config, breakdown);
        }
        if (record.IsRepeatedAction) breakdown.RepeatPenalty = -AIReflectionConfig.NonNegative(
            record.IsOscillation ? config.OscillationPenalty : config.RepeatedNoProgressPenalty);
        if (record.FailureReason != AIFailureReason.None && record.FailureReason != AIFailureReason.RepeatedNoProgress)
            breakdown.FailurePenalty = -AIReflectionConfig.NonNegative(config.FailedActionPenalty);
        breakdown.Clamp(config);
        record.ImmediateReward = breakdown.Total;
        return breakdown.Total;
    }
    static void EvaluateFacts(AIActionRecord record, AIReflectionConfig config, AIRewardBreakdown reward)
    {
        var before = record.Before; var after = record.After; var outcome = record.Outcome;
        bool movement = AIFailureAnalyzer.IsMovement(record.ActionType);
        bool riskImproved = AIFailureAnalyzer.RiskImproved(before, after, config);
        bool safe = AIFailureAnalyzer.SafeAfter(after, config);
        bool rangeAdvantage = after.IsRangedActor && after.CanAttackObservedEnemy && after.OutsideObservedEnemyRange;
        bool gainedRange = rangeAdvantage && !(before.CanAttackObservedEnemy && before.OutsideObservedEnemyRange);
        if (outcome.OwnLosses > 0 && outcome.EnemyKills == 0)
            reward.Survival -= AIReflectionConfig.NonNegative(config.OwnLossSurvivalPenalty);
        else if (outcome.EnemyKills > outcome.OwnLosses)
        {
            Success(record, AISuccessReason.GoodTrade);
            float lightDamage = Math.Max(1, before.ActorHP) * Mathf.Clamp01(config.GoodTradeLightDamageRatio);
            if (outcome.OwnLosses == 0 && outcome.DamageTaken <= lightDamage && before.IncomingDamage > 0)
                reward.Survival += AIReflectionConfig.NonNegative(config.GoodTradeSurvivalReward);
        }
        if (record.ActionType == AIActionType.Retreat && AIFailureAnalyzer.GoodRetreat(before, after, config))
        {
            reward.Survival += AIReflectionConfig.NonNegative(config.RetreatSurvivalReward);
            reward.Position += AIReflectionConfig.NonNegative(config.RetreatPositionReward);
            Success(record, AISuccessReason.SuccessfulRetreat);
        }
        if (movement && safe && after.ActorHeight > before.ActorHeight
            && (after.CanAttackObservedEnemy || riskImproved || gainedRange))
        {
            reward.Position += AIReflectionConfig.NonNegative(config.HighGroundPositionReward);
            Success(record, AISuccessReason.HighGroundAdvantage);
        }
        if (movement && safe && gainedRange)
        {
            reward.Position += AIReflectionConfig.NonNegative(config.RangePositionReward);
            reward.Survival += AIReflectionConfig.NonNegative(config.RangeSurvivalReward);
            Success(record, AISuccessReason.GoodRangeManagement);
        }
        else if (movement && before.IsRangedActor && before.OutsideObservedEnemyRange && !after.OutsideObservedEnemyRange
            && after.LocalEnemyPower > 0 && after.IncomingDamage > before.IncomingDamage + AIReflectionConfig.NonNegative(config.MinimumRiskImprovement))
            reward.Position -= AIReflectionConfig.NonNegative(config.BadRangePositionPenalty);
        if (record.ActorRole != "Scout" && before.LocalEnemyPower > 0 && after.LocalEnemyPower > 0)
        {
            float threshold = AIReflectionConfig.NonNegative(config.LocalPowerDeltaThreshold);
            if (outcome.LocalPowerRatioDelta > threshold && safe)
            {
                reward.LocalPower += AIReflectionConfig.NonNegative(config.LocalPowerReward);
                Success(record, AISuccessReason.GoodPositioning);
            }
            else if (movement && outcome.LocalPowerRatioDelta < -threshold)
                reward.LocalPower -= AIReflectionConfig.NonNegative(config.LocalPowerPenalty);
        }
        if (movement && before.HasObjective && after.HasObjective && outcome.DistanceToObjectiveDelta > 0 && safe
            && !before.EmergencyDefense && !after.EmergencyDefense && !before.OwnCrystalThreatened && !after.OwnCrystalThreatened)
        {
            reward.Objective += AIReflectionConfig.NonNegative(config.ObjectiveProgressReward);
            Success(record, AISuccessReason.ObjectiveProgress);
        }
        if (before.OwnCrystalThreatened && (!after.OwnCrystalThreatened
            || movement && safe && after.OwnCrystalDistance < before.OwnCrystalDistance && riskImproved))
        {
            reward.Defense += AIReflectionConfig.NonNegative(config.DefenseReward);
            Success(record, AISuccessReason.CrystalDefense);
        }
        int discoveries = outcome.NewEnemyLifeIds != null ? outcome.NewEnemyLifeIds.Count : 0;
        if (record.ActorRole == "Scout" && (outcome.NewTilesRevealed > 0 || discoveries > 0))
        {
            reward.Information += outcome.NewTilesRevealed * AIReflectionConfig.NonNegative(config.ScoutTileReward)
                + discoveries * AIReflectionConfig.NonNegative(config.ScoutEnemyDiscoveryReward);
            if (outcome.BeliefUncertaintyReduction > 0)
                reward.Information += AIReflectionConfig.NonNegative(config.ScoutUncertaintyReward);
            Success(record, AISuccessReason.SuccessfulScout);
        }
        if (record.ActionType == AIActionType.SkillUse && (outcome.EffectChanged || outcome.ShieldGranted || outcome.HealingDone > 0))
        {
            reward.Preparation += AIReflectionConfig.NonNegative(config.PreparationReward);
            Success(record, AISuccessReason.SuccessfulPreparation);
        }
        record.PreparationCandidate |= safe && (reward.Information > 0 || reward.Preparation > 0
            || movement && (gainedRange || riskImproved || reward.Position > 0 || reward.Objective > 0
                || record.ActionType == AIActionType.Support && outcome.LocalPowerRatioDelta > 0
                || record.ActionType == AIActionType.Surround && outcome.LocalPowerRatioDelta > 0));
        if (record.PreparationCandidate && record.ActionType == AIActionType.Surround) Success(record, AISuccessReason.SuccessfulSurround);
        if (record.PreparationCandidate && record.ActionType == AIActionType.Support) Success(record, AISuccessReason.SuccessfulPreparation);
    }
    public static void Success(AIActionRecord record, AISuccessReason reason)
    {
        if (record.SuccessReasons == null) record.SuccessReasons = new List<AISuccessReason>();
        if (reason != AISuccessReason.None && !record.SuccessReasons.Contains(reason)) record.SuccessReasons.Add(reason);
    }
}

public static class AIFailureAnalyzer
{
    public static bool HasMeaningfulProgress(AIActionRecord record, AIReflectionConfig config = null)
    {
        if (record == null) return false;
        if (config == null) config = AIReflectionConfig.Active;
        var outcome = record.Outcome;
        if (!record.ExecutionSucceeded || outcome == null || record.Before == null || record.After == null) return false;
        if (outcome.EnemyKills > 0 || outcome.ArtifactsAcquired > 0 || outcome.DamageDealt > 0
            || outcome.HealingDone > 0 || outcome.ShieldReduced || outcome.ShieldGranted
            || record.ActionType == AIActionType.SkillUse && outcome.EffectChanged
            || outcome.OwnBuildingDelta > 0 || outcome.OwnUnitDelta > 0 || outcome.TerritoryDelta > 0
            || outcome.NewTilesRevealed > 0 || outcome.NewEnemyLifeIds != null && outcome.NewEnemyLifeIds.Count > 0) return true;
        if (record.ActionType == AIActionType.Upgrade) return true; // Successful executor confirms paid level progression.
        if (record.ActionType == AIActionType.Rotate) return outcome.DirectionChanged;
        if (outcome.EconomyAfter < outcome.EconomyBefore) return true;
        var before = record.Before; var after = record.After;
        bool safe = SafeAfter(after, config), riskImproved = RiskImproved(before, after, config);
        bool rangedAdvantage = after.IsRangedActor && after.CanAttackObservedEnemy && after.OutsideObservedEnemyRange
            && !(before.CanAttackObservedEnemy && before.OutsideObservedEnemyRange);
        return riskImproved || IsMovement(record.ActionType) && safe && (outcome.DistanceToObjectiveDelta > 0
            || after.ActorHeight > before.ActorHeight && (after.CanAttackObservedEnemy || rangedAdvantage)
            || rangedAdvantage || outcome.LocalPowerRatioDelta > AIReflectionConfig.NonNegative(config.LocalPowerDeltaThreshold));
    }
    public static AIFailureReason Analyze(AIActionRecord record, string executionFailure = null, AIReflectionConfig config = null)
    {
        if (config == null) config = AIReflectionConfig.Active;
        if (!record.ExecutionSucceeded)
        {
            if (!string.IsNullOrEmpty(executionFailure))
            {
                if (executionFailure.IndexOf("resource", StringComparison.OrdinalIgnoreCase) >= 0) return AIFailureReason.ResourceShortage;
                if (executionFailure.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0) return AIFailureReason.InvalidTarget;
            }
            return AIFailureReason.ExecutionFailed;
        }
        var before = record.Before; var after = record.After; var outcome = record.Outcome;
        if (outcome.OwnLosses > 0 && outcome.EnemyKills == 0) return AIFailureReason.UnitLost;
        if (outcome.OwnLosses > outcome.EnemyKills) return AIFailureReason.BadTrade;
        if (!before.OwnCrystalThreatened && after.OwnCrystalThreatened) return AIFailureReason.CrystalExposed;
        if (outcome.EconomyAfter >= EconomicState.Crisis && outcome.EconomyAfter > outcome.EconomyBefore) return AIFailureReason.EconomyWorsened;
        if (record.ActionType == AIActionType.Attack || record.ActionType == AIActionType.SkillUse)
        {
            if (!record.MeaningfulProgress && outcome.DamageTaken > 0) return AIFailureReason.PoorTargetSelection;
            if (!record.MeaningfulProgress) return before.TargetObserved && !after.TargetObserved
                ? AIFailureReason.TargetLost : AIFailureReason.NoDamage;
        }
        if (IsMovement(record.ActionType) && after.LocalEnemyPower > 0)
        {
            if (before.IsRangedActor && before.OutsideObservedEnemyRange && !after.OutsideObservedEnemyRange
                && after.IncomingDamage > before.IncomingDamage + AIReflectionConfig.NonNegative(config.MinimumRiskImprovement))
                return AIFailureReason.BadRangeManagement;
            if (before.LocalPowerRatio >= AIReflectionConfig.NonNegative(config.OverextensionBeforePowerRatio)
                && after.LocalPowerRatio < AIReflectionConfig.NonNegative(config.OverextensionAfterPowerRatio)) return AIFailureReason.Overextended;
            if (record.ActionType == AIActionType.Retreat && !SafeAfter(after, config)
                && after.IncomingDamage > before.IncomingDamage && outcome.DamageTaken > 0) return AIFailureReason.FailedRetreat;
            if (record.ActorRole != "Scout" && before.LocalEnemyPower > 0
                && outcome.LocalPowerRatioDelta < -AIReflectionConfig.NonNegative(config.LocalPowerDeltaThreshold)
                && !RiskImproved(before, after, config)) return AIFailureReason.PositionWorsened;
        }
        if (record.IsRepeatedAction) return AIFailureReason.RepeatedNoProgress;
        // Waiting can preserve defense and AP. A quiet wait is neutral, never an automatic failure.
        return AIFailureReason.None;
    }
    public static bool SafeAfter(AIActionContextSnapshot after, AIReflectionConfig config)
        => after != null && after.ActorHP > 0 && after.IncomingDamage < Math.Max(1, after.ActorHP)
            * Mathf.Clamp01(AIReflectionConfig.Finite(config.RewardMaximumIncomingFraction, .8f));
    public static bool RiskImproved(AIActionContextSnapshot before, AIActionContextSnapshot after, AIReflectionConfig config)
        => before != null && after != null && after.ActorHP > 0
            && after.IncomingDamage < before.IncomingDamage - AIReflectionConfig.NonNegative(config.MinimumRiskImprovement);
    public static bool GoodRetreat(AIActionContextSnapshot before, AIActionContextSnapshot after, AIReflectionConfig config)
        => before != null && after != null && before.ActorHpRatio <= Mathf.Clamp01(config.RetreatLowHpThreshold)
            && before.LocalEnemyPower > 0 && before.LocalPowerRatio <= AIReflectionConfig.NonNegative(config.RetreatMaximumPowerRatio)
            && RiskImproved(before, after, config) && SafeAfter(after, config)
            && after.OwnCrystalDistance <= before.OwnCrystalDistance
                + (before.OwnCrystalThreatened ? 0 : AIReflectionConfig.NonNegative(config.CrystalRetreatAllowance));
    public static bool IsMovement(AIActionType type) => type == AIActionType.Move || type == AIActionType.Retreat
        || type == AIActionType.Support || type == AIActionType.Surround || type == AIActionType.DefenseRepos;
}

public sealed class AIRepeatActionDetector
{
    readonly List<AIActionRecord> recent = new List<AIActionRecord>(32);
    readonly int capacity;
    public AIRepeatActionDetector(int capacity = 32) { this.capacity = Mathf.Clamp(capacity, 3, 128); }
    public bool Evaluate(AIActionRecord record)
    {
        bool repeat = false;
        if (!record.MeaningfulProgress && record.ActionType != AIActionType.Wait)
        {
            AIActionRecord previous = null, older = null;
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var candidate = recent[i];
                // Runtime identity separates two live allies; the stored learning pattern remains generalized.
                bool sameActor = !string.IsNullOrEmpty(candidate.ActorLifeId) && !string.IsNullOrEmpty(record.ActorLifeId)
                    ? candidate.ActorLifeId == record.ActorLifeId : candidate.ActorRuntimeId == record.ActorRuntimeId;
                if (!sameActor || candidate.ActorRole != record.ActorRole) continue;
                if (candidate.MeaningfulProgress) break;
                if (previous == null) previous = candidate; else { older = candidate; break; }
            }
            if (previous != null && previous.ContextKey == record.ContextKey && previous.ActionKey == record.ActionKey)
            {
                repeat = SameTransition(previous, record) && previous.ExecutionSucceeded == record.ExecutionSucceeded;
                if (older != null && AIFailureAnalyzer.IsMovement(record.ActionType)
                    && SameTransition(older, record) && ReverseTransition(previous, record))
                { repeat = true; record.IsOscillation = true; }
            }
        }
        record.IsRepeatedAction = repeat;
        recent.Add(record); if (recent.Count > capacity) recent.RemoveAt(0);
        return repeat;
    }
    public void Clear() => recent.Clear();
    public void Restore(IEnumerable<AIActionRecord> records)
    {
        Clear();
        if (records == null) return;
        foreach (var record in records) if (record != null)
        { recent.Add(record); if (recent.Count > capacity) recent.RemoveAt(0); }
    }
    static bool SameTransition(AIActionRecord a, AIActionRecord b) => a.Before.ActorX == b.Before.ActorX
        && a.Before.ActorY == b.Before.ActorY && a.After.ActorX == b.After.ActorX && a.After.ActorY == b.After.ActorY
        && a.TargetCategory == b.TargetCategory
        && (AIFailureAnalyzer.IsMovement(a.ActionType) || a.Before.TargetX == b.Before.TargetX && a.Before.TargetY == b.Before.TargetY);
    static bool ReverseTransition(AIActionRecord a, AIActionRecord b) => a.Before.ActorX == b.After.ActorX
        && a.Before.ActorY == b.After.ActorY && a.After.ActorX == b.Before.ActorX && a.After.ActorY == b.Before.ActorY;
}
