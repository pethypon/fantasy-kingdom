using System;
using UnityEngine;

[Serializable]
public sealed class AIEconomyRewardState
{
    public int Version = 1, EpisodeId, ConsecutiveUnhealthyTurns;
    public int LastObservedOwnTurn = -1, LastUnhealthyOwnTurn = -1, LastEndedOwnTurn = -1;
    public bool Initialized, WasUnhealthy, RecoveryRewardGranted, HasGrantedRecovery;
    public EconomicState PreviousState;
    public int PendingEpisodeId;
    public long PendingCandidateActionId;
}

public sealed class AIEconomyRecoveryResult
{
    public float Reward;
    public int EpisodeId;
    public long CandidateActionId;
    public bool Recovered => EpisodeId > 0;
}

/// <summary>One reward per recovery episode, with own-turn hysteresis and no disk or live-object state.</summary>
public sealed class AIEconomyRewardTracker
{
    readonly AIReflectionConfig config;
    AIEconomyRewardState state = new AIEconomyRewardState();
    public AIEconomyRewardTracker(AIReflectionConfig config = null)
        => this.config = config != null ? config : AIReflectionConfig.Active;

    public void Observe(AIActionContextSnapshot snapshot, int ownTurn)
    { if (snapshot != null) Observe(snapshot.EconomyState, ownTurn); }
    public void Observe(EconomicState economy, int ownTurn) => Observe(economy, ownTurn, 0);

    public void ObserveAction(AIActionRecord record, int ownTurn)
    {
        if (record?.Before == null || record.After == null) return;
        Observe(record.Before.EconomyState, ownTurn);
        bool build = record.ActionType == AIActionType.Build && record.Outcome != null && record.Outcome.OwnBuildingDelta > 0;
        bool upgrade = record.ActionType == AIActionType.Upgrade;
        bool causedRecovery = record.ExecutionSucceeded && (build || upgrade)
            && record.Before.EconomyState > EconomicState.Healthy && record.After.EconomyState == EconomicState.Healthy;
        // The finalized before/after transition and a confirmed economic executor are causal evidence.
        // A subsequent unrelated action cannot claim a recovery already observed earlier in the turn.
        Observe(record.After.EconomyState, ownTurn, causedRecovery ? record.ActionId : 0);
    }

    void Observe(EconomicState economy, int ownTurn, long candidateActionId)
    {
        if (ownTurn < 0 || ownTurn < state.LastObservedOwnTurn || !ValidEconomy(economy)) return;
        if (!state.Initialized)
        {
            state.Initialized = true; state.PreviousState = economy;
            state.LastObservedOwnTurn = ownTurn;
            if (economy > EconomicState.Healthy)
            {
                state.ConsecutiveUnhealthyTurns = 1; state.LastUnhealthyOwnTurn = ownTurn;
                if (!state.HasGrantedRecovery || config.EconomyRecoveryResetTurns <= 1) StartEpisode();
            }
            return;
        }
        if (economy > EconomicState.Healthy)
        {
            if (state.LastUnhealthyOwnTurn != ownTurn)
                state.ConsecutiveUnhealthyTurns = state.LastUnhealthyOwnTurn == ownTurn - 1
                    && state.PreviousState > EconomicState.Healthy ? state.ConsecutiveUnhealthyTurns + 1 : 1;
            state.LastUnhealthyOwnTurn = ownTurn;
            if (!state.WasUnhealthy && (!state.HasGrantedRecovery
                || state.ConsecutiveUnhealthyTurns >= Mathf.Clamp(config.EconomyRecoveryResetTurns, 1, 64))) StartEpisode();
        }
        else
        {
            if (state.WasUnhealthy && !state.RecoveryRewardGranted)
            {
                state.PendingEpisodeId = state.EpisodeId;
                state.PendingCandidateActionId = Math.Max(0, candidateActionId);
                state.RecoveryRewardGranted = state.HasGrantedRecovery = true;
            }
            state.WasUnhealthy = false; state.ConsecutiveUnhealthyTurns = 0; state.LastUnhealthyOwnTurn = -1;
        }
        state.PreviousState = economy; state.LastObservedOwnTurn = ownTurn;
    }

    void StartEpisode()
    {
        state.EpisodeId = Math.Min(1000000, state.EpisodeId + 1);
        state.WasUnhealthy = true; state.RecoveryRewardGranted = false;
    }

    public AIEconomyRecoveryResult EndTurn(int ownTurn, AIActionContextSnapshot snapshot = null)
    {
        var result = new AIEconomyRecoveryResult();
        if (ownTurn < 0 || ownTurn <= state.LastEndedOwnTurn || ownTurn < state.LastObservedOwnTurn) return result;
        Observe(snapshot, ownTurn);
        state.LastEndedOwnTurn = ownTurn;
        if (state.PendingEpisodeId <= 0) return result;
        result.EpisodeId = state.PendingEpisodeId; result.CandidateActionId = state.PendingCandidateActionId;
        result.Reward = Mathf.Clamp(AIReflectionConfig.Finite(config.EconomyRecoveryReward, 1), 0, 3);
        state.PendingEpisodeId = 0; state.PendingCandidateActionId = 0;
        return result;
    }

    public AIEconomyRewardState Capture() => Copy(state);
    public void Restore(AIEconomyRewardState saved)
    {
        state = saved != null && saved.Version == 1 && ValidEconomy(saved.PreviousState)
            ? Copy(saved) : new AIEconomyRewardState();
        state.EpisodeId = Mathf.Clamp(state.EpisodeId, 0, 1000000);
        state.ConsecutiveUnhealthyTurns = Mathf.Clamp(state.ConsecutiveUnhealthyTurns, 0, 64);
        state.LastObservedOwnTurn = Mathf.Clamp(state.LastObservedOwnTurn, -1, 1000000);
        state.LastUnhealthyOwnTurn = Mathf.Clamp(state.LastUnhealthyOwnTurn, -1, state.LastObservedOwnTurn);
        state.LastEndedOwnTurn = Mathf.Clamp(state.LastEndedOwnTurn, -1, 1000000);
        state.PendingEpisodeId = Mathf.Clamp(state.PendingEpisodeId, 0, state.EpisodeId);
        state.PendingCandidateActionId = Math.Max(0, state.PendingCandidateActionId);
        if (state.PendingEpisodeId == 0) state.PendingCandidateActionId = 0;
    }
    static bool ValidEconomy(EconomicState value) => value >= EconomicState.Healthy && value <= EconomicState.Collapse;
    static AIEconomyRewardState Copy(AIEconomyRewardState value) => new AIEconomyRewardState
    {
        Version = value.Version, EpisodeId = value.EpisodeId, ConsecutiveUnhealthyTurns = value.ConsecutiveUnhealthyTurns,
        LastObservedOwnTurn = value.LastObservedOwnTurn, LastUnhealthyOwnTurn = value.LastUnhealthyOwnTurn,
        LastEndedOwnTurn = value.LastEndedOwnTurn, Initialized = value.Initialized, WasUnhealthy = value.WasUnhealthy,
        RecoveryRewardGranted = value.RecoveryRewardGranted, HasGrantedRecovery = value.HasGrantedRecovery,
        PreviousState = value.PreviousState, PendingEpisodeId = value.PendingEpisodeId,
        PendingCandidateActionId = value.PendingCandidateActionId
    };
}
