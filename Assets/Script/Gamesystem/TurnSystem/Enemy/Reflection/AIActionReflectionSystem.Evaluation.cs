using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Battle-local evaluation state. Rewards are corrected in memory, never by replaying diary text.</summary>
public sealed partial class AIActionReflectionSystem
{
    readonly AIEconomyRewardTracker economyRewards;
    readonly AIDelayedCreditAssigner delayedCredits;
    readonly AIArmyUtilizationAnalyzer armyUtilization;
    readonly HashSet<string> observedEnemyLifeIds = new HashSet<string>(StringComparer.Ordinal);

    void RestoreEvaluationState()
    {
        observedEnemyLifeIds.Clear();
        if (state.ObservedEnemyLifeIds != null)
            foreach (string id in state.ObservedEnemyLifeIds) RememberContact(id);
        // Old battle records are observed evidence, not live hidden-unit data.
        foreach (var record in state.RecentRecords)
        { SeedObservedContacts(record.Before); SeedObservedContacts(record.After); }
        economyRewards.Restore(state.EconomyRewardState);
        delayedCredits.Restore(state.DelayedCreditState, state.RecentRecords);
        armyUtilization.Restore(state.ArmyUtilizationState);
    }

    void CaptureEvaluationState()
    {
        state.EconomyRewardState = economyRewards.Capture();
        state.DelayedCreditState = delayedCredits.Capture();
        state.ArmyUtilizationState = armyUtilization.Capture();
        state.ObservedEnemyLifeIds = new List<string>(observedEnemyLifeIds);
        state.ObservedEnemyLifeIds.Sort(StringComparer.Ordinal);
    }

    void RememberContact(string id)
    {
        if (!string.IsNullOrEmpty(id) && id.Length <= 128 && observedEnemyLifeIds.Count < config.EventLimit)
            observedEnemyLifeIds.Add(id);
    }
    void SeedObservedContacts(AIActionContextSnapshot snapshot)
    {
        if (snapshot?.VisibleEnemyLifeIds == null) return;
        foreach (string id in snapshot.VisibleEnemyLifeIds) RememberContact(id);
    }
    void FilterNewlyObservedEnemies(AIActionRecord record)
    {
        var contacts = record.Outcome.NewEnemyLifeIds;
        if (contacts != null)
            for (int i = contacts.Count - 1; i >= 0; i--)
            {
                string id = contacts[i];
                if (string.IsNullOrEmpty(id) || id.Length > 128 || observedEnemyLifeIds.Contains(id)
                    || observedEnemyLifeIds.Count >= config.EventLimit) contacts.RemoveAt(i);
                else observedEnemyLifeIds.Add(id);
            }
        SeedObservedContacts(record.After);
    }

    void ApplyOutcomeCredits(AIActionRecord record, PendingAction action)
    {
        // Only events raised by this action are inspected; cost is independent of the battle ledger size.
        foreach (string id in action.KillEventIds)
        {
            bool formation = kills.TryGetValue(id, out var outcome) && outcome.Formation;
            ApplyDelayedEvent(record, "kill:" + id, id, formation
                ? config.FormationKillRewardValue : config.NormalKillRewardValue);
        }
        foreach (string id in action.ArtifactEventIds)
            ApplyDelayedEvent(record, "artifact:" + id, null, config.ArtifactRewardValue);
    }
    void ApplyDelayedEvent(AIActionRecord outcome, string eventId, string killedLifeId, float reward)
    {
        var corrections = delayedCredits.ApplyOutcome(new AIDelayedOutcomeEvent
        { EventId = eventId, KilledLifeId = killedLifeId, ActorLifeId = outcome.ActorLifeId,
            ActionId = outcome.ActionId, Reward = reward });
        foreach (var correction in corrections)
        {
            if (!completed.TryGetValue(correction.ActionId, out var preparation))
            { delayedCredits.ReleaseUnappliedReward(correction.ActionId, correction.Delta); continue; }
            var before = preparation.RewardBreakdown.Copy();
            float applied = Mathf.Min(correction.Delta, Mathf.Max(0, config.ActionUpper - preparation.Reward),
                Mathf.Max(0, AIReflectionConfig.NonNegative(config.MaxDelayedRewardPerAction) - before.DelayedReward));
            delayedCredits.ReleaseUnappliedReward(correction.ActionId, correction.Delta - applied);
            if (applied <= 0) continue;
            preparation.RewardBreakdown.DelayedReward += applied;
            AddSuccess(preparation, AISuccessReason.SuccessfulPreparation);
            ApplyRecordCorrection(preparation, before, outcome.Turn);
        }
    }

    void EndEvaluationTurn(int turnNumber, AIActionContextSnapshot snapshot, bool terminalTurn,
        AIArmyTurnEvidence evidence)
    {
        if (!Enabled) return;
        EnsureBattle();
        if (state.Ended || state.LastEndedTurn == turnNumber || pending.Count != 0) return;
        state.LastEndedTurn = turnNumber; state.OwnTurns++;
        if (snapshot != null) state.LastSnapshot = snapshot.Copy();
        SeedObservedContacts(state.LastSnapshot);
        state.Interval.After = state.LastSnapshot; state.BattleSummary.After = state.LastSnapshot;
        state.Interval.LastTurn = Math.Max(state.Interval.LastTurn, turnNumber);
        state.BattleSummary.LastTurn = Math.Max(state.BattleSummary.LastTurn, turnNumber);
        if (state.Interval.FirstOwnTurn < 0) state.Interval.FirstOwnTurn = state.OwnTurns;
        if (state.BattleSummary.FirstOwnTurn < 0) state.BattleSummary.FirstOwnTurn = 1;
        var recovery = economyRewards.EndTurn(state.OwnTurns, state.LastSnapshot);
        if (recovery.Recovered && recovery.Reward > 0 && MarkStableTurn(turnNumber))
        {
            if (recovery.CandidateActionId > 0 && completed.TryGetValue(recovery.CandidateActionId, out var cause))
            {
                var before = cause.RewardBreakdown.Copy();
                cause.RewardBreakdown.Economy += recovery.Reward;
                AddSuccess(cause, AISuccessReason.EconomyRecovered);
                ApplyRecordCorrection(cause, before, turnNumber);
                cause.EconomyStableReward = Math.Max(0, cause.RewardBreakdown.Economy - before.Economy);
            }
            else AddTurnReward(new AIRewardBreakdown { Economy = recovery.Reward }, turnNumber);
        }
        if (evidence != null)
        {
            var utilization = armyUtilization.EndTurn(state.OwnTurns, evidence);
            AddTurnReward(utilization.Breakdown, turnNumber);
            if (evidence.UtilizedUnitCount > 0 && (evidence.CombatParticipation || evidence.ObjectiveProgress
                || evidence.ScoutActivity || evidence.SupportActivity))
            {
                CountSuccess(state.Interval.Successes, AISuccessReason.GoodArmyUtilization);
                CountSuccess(state.BattleSummary.Successes, AISuccessReason.GoodArmyUtilization);
            }
            foreach (var reason in utilization.Reasons)
            {
                AIActionLearningProfile.CountFailure(state.Interval.Failures, reason);
                AIActionLearningProfile.CountFailure(state.BattleSummary.Failures, reason);
            }
        }
        if (!terminalTurn && state.OwnTurns % Math.Max(1, config.DiaryIntervalOwnTurns) == 0)
            OutputDiary("INTERVAL");
    }

    void CountTerminalOwnTurn()
    {
        if (state.LastStartedTurn < 0 || state.LastEndedTurn == state.LastStartedTurn) return;
        // Flush finalized economic outcomes even if the decisive action ended the match mid-turn.
        // Suppress scheduled output: the terminal diary owns this boundary.
        EndEvaluationTurn(state.LastStartedTurn, state.LastSnapshot, true, null);
    }

    void AddTurnReward(AIRewardBreakdown reward, int turn)
    {
        if (reward == null || reward.Total == 0) return;
        reward.Clamp(config);
        state.BattleReward += reward.Total; state.TurnReward += reward.Total;
        state.Interval.AddReward(reward, turn); state.BattleSummary.AddReward(reward, turn);
    }

    void AddSuccess(AIActionRecord record, AISuccessReason reason)
    {
        if (record.SuccessReasons.Contains(reason)) return;
        record.SuccessReasons.Add(reason);
        var entry = Profile.Find(record.ContextKey, record.ActionKey);
        if (entry != null) AIActionLearningProfile.CountSuccess(entry.SuccessReasons, reason);
        CountSuccess(state.Interval.Successes, reason); CountSuccess(state.BattleSummary.Successes, reason);
    }
    static void CountSuccess(List<SuccessReasonCount> reasons, AISuccessReason reason)
    {
        foreach (var item in reasons)
            if (item.Reason == reason) { item.Count = Math.Min(1000000, item.Count + 1); return; }
        reasons.Add(new SuccessReasonCount { Reason = reason, Count = 1 });
    }

    void ApplyRecordCorrection(AIActionRecord record, AIRewardBreakdown before, int occurrenceTurn)
    {
        float previousReward = record.Reward;
        record.RewardBreakdown.Clamp(config); record.Reward = record.RewardBreakdown.Total;
        record.ImmediateReward = record.Reward - record.RewardBreakdown.Economy - record.RewardBreakdown.DelayedReward;
        record.EconomyStableReward = Math.Max(0, record.RewardBreakdown.Economy);
        float delta = record.Reward - previousReward;
        var breakdownDelta = AIRewardBreakdown.Difference(record.RewardBreakdown, before);
        float change = Profile.CorrectReward(record, delta, config);
        record.LearningValueChange += change;
        state.BattleReward += delta;
        state.Interval.CorrectReward(record, delta, change, breakdownDelta, occurrenceTurn);
        state.BattleSummary.CorrectReward(record, delta, change, breakdownDelta, occurrenceTurn);
        AIReflectionDecisionTrace.UpdateOutcome(record);
        LogReward(record);
    }

    void UpgradeFormationReward(AIActionRecord record)
    {
        var before = record.RewardBreakdown.Copy();
        float economy = before.Economy, delayed = before.DelayedReward;
        bool formationAlreadyCounted = record.SuccessReasons.Contains(AISuccessReason.FormationKill);
        record.Outcome.FormationKills++;
        AIActionRewardEvaluator.Evaluate(record, config);
        if (!formationAlreadyCounted)
        { record.SuccessReasons.Remove(AISuccessReason.FormationKill); AddSuccess(record, AISuccessReason.FormationKill); }
        record.RewardBreakdown.Economy = economy; record.RewardBreakdown.DelayedReward = delayed;
        // The event changes one normal kill into one formation kill, rather than granting a second kill.
        var counts = state.Interval;
        if (state.LastDiaryOwnTurn == state.OwnTurns && state.LastDiaryResult == "INTERVAL"
            && state.LastDiaryInterval != null && record.Turn >= state.LastDiaryInterval.FirstTurn
            && record.Turn <= state.LastDiaryInterval.LastTurn)
            counts = state.LastDiaryInterval;
        counts.NormalKills = Math.Max(0, counts.NormalKills - 1); counts.FormationKills++;
        state.BattleSummary.NormalKills = Math.Max(0, state.BattleSummary.NormalKills - 1); state.BattleSummary.FormationKills++;
        ApplyRecordCorrection(record, before, state.LastStartedTurn);
    }

    void LogReward(AIActionRecord record)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (config.EnableCandidateDebug)
            Debug.Log("[AI自己評価結果] turn=" + record.Turn + " action=" + record.ActionType + " id=" + record.ActionId
                + " role=" + record.ActorRole + " context=" + record.ContextKey
                + " base=" + record.BaseScore.ToString("F2") + " learned=" + record.LearnedModifier.ToString("F2")
                + " final=" + record.FinalScore.ToString("F2") + " combat=" + record.RewardBreakdown.Combat.ToString("F2")
                + " artifact=" + record.RewardBreakdown.Artifact.ToString("F2")
                + " survival=" + record.RewardBreakdown.Survival.ToString("F2")
                + " position=" + record.RewardBreakdown.Position.ToString("F2")
                + " localPower=" + record.RewardBreakdown.LocalPower.ToString("F2")
                + " objective=" + record.RewardBreakdown.Objective.ToString("F2")
                + " economy=" + record.RewardBreakdown.Economy.ToString("F2")
                + " information=" + record.RewardBreakdown.Information.ToString("F2")
                + " defense=" + record.RewardBreakdown.Defense.ToString("F2")
                + " efficiency=" + record.RewardBreakdown.Efficiency.ToString("F2")
                + " preparation=" + record.RewardBreakdown.Preparation.ToString("F2")
                + " delayed=" + record.RewardBreakdown.DelayedReward.ToString("F2")
                + " repeatPenalty=" + record.RewardBreakdown.RepeatPenalty.ToString("F2")
                + " failurePenalty=" + record.RewardBreakdown.FailurePenalty.ToString("F2")
                + " idlePenalty=" + record.RewardBreakdown.IdleArmyPenalty.ToString("F2")
                + " productionPenalty=" + record.RewardBreakdown.OverproductionPenalty.ToString("F2")
                + " missedPenalty=" + record.RewardBreakdown.MissedOpportunityPenalty.ToString("F2")
                + " total=" + record.Reward.ToString("F2") + " failure=" + record.FailureReason
                + " successes=" + string.Join(",", record.SuccessReasons));
#endif
    }
}
