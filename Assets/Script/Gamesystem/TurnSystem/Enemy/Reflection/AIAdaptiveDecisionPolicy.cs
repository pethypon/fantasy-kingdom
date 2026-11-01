using System;
using System.Collections.Generic;
using UnityEngine;

// Append only: indices are persisted in battle saves. This is decision policy, never game rules.
public enum AIAdaptiveRule { Recon, Advance, Combat, Siege, Support, Recruit, Economy }

[Serializable]
public sealed class AIAdaptivePolicyState
{
    public float[] Values = new float[7];
    public int[] Samples = new int[7];
    public float[] TurnRewards = new float[7];
    public int[] TurnSamples = new int[7];
    public bool TurnProgress;
    public int StalledTurns, Revision;
    public string LastReason;
}

/// <summary>
/// Owner: one reflection system; lifetime: one battle (including save/resume).
/// Seven bounded outcome averages, no Unity references or unbounded action/position history.
/// Learning uses finalized observed records. Selection cannot relax legality or survival tiers.
/// </summary>
public sealed class AIAdaptiveDecisionPolicy
{
    const int Count = 7;
    readonly float[] turnRewards = new float[Count];
    readonly int[] turnSamples = new int[Count];
    readonly AIReflectionConfig config;
    AIAdaptivePolicyState state = new AIAdaptivePolicyState();
    bool turnProgress;
    public AIAdaptiveDecisionPolicy(AIReflectionConfig config) { this.config = config; }
    public int Revision => state.Revision;

    public void Restore(AIAdaptivePolicyState saved)
    {
        state = new AIAdaptivePolicyState(); ClearTurn();
        if (saved == null) return;
        for (int i = 0; i < Count; i++)
        {
            if (saved.Values != null && i < saved.Values.Length)
                state.Values[i] = Mathf.Clamp(AIReflectionConfig.Finite(saved.Values[i]), -1, 1);
            if (saved.Samples != null && i < saved.Samples.Length)
                state.Samples[i] = Mathf.Clamp(saved.Samples[i], 0, 1000000);
            if (saved.TurnSamples != null && i < saved.TurnSamples.Length)
                turnSamples[i] = Mathf.Clamp(saved.TurnSamples[i], 0, 64);
            if (saved.TurnRewards != null && i < saved.TurnRewards.Length)
                turnRewards[i] = Mathf.Clamp(AIReflectionConfig.Finite(saved.TurnRewards[i]), -turnSamples[i], turnSamples[i]);
        }
        turnProgress = saved.TurnProgress;
        state.StalledTurns = Mathf.Clamp(saved.StalledTurns, 0, 12);
        state.Revision = Mathf.Clamp(saved.Revision, 0, 1000000);
        state.LastReason = saved.LastReason != null && saved.LastReason.Length <= 160 ? saved.LastReason : null;
    }
    public AIAdaptivePolicyState Capture() => new AIAdaptivePolicyState
    {
        Values = (float[])state.Values.Clone(), Samples = (int[])state.Samples.Clone(),
        TurnRewards = (float[])turnRewards.Clone(), TurnSamples = (int[])turnSamples.Clone(), TurnProgress = turnProgress,
        StalledTurns = state.StalledTurns, Revision = state.Revision, LastReason = state.LastReason
    };

    public static AIAdaptiveRule Classify(AIAction action)
    {
        if (action == null) return AIAdaptiveRule.Economy;
        switch (action.ActionType)
        {
            case AIActionType.Summon: return AIAdaptiveRule.Recruit;
            case AIActionType.Build: case AIActionType.Upgrade: case AIActionType.SubCrystal: return AIAdaptiveRule.Economy;
            case AIActionType.Attack:
                return action.TargetUnit != null && action.TargetUnit.type != Type.Unit ? AIAdaptiveRule.Siege : AIAdaptiveRule.Combat;
            case AIActionType.SkillUse:
                return action.TargetUnit != null && action.Unit != null && action.TargetUnit.team != action.Unit.team
                    ? AIAdaptiveRule.Combat : AIAdaptiveRule.Support;
            case AIActionType.Retreat: case AIActionType.Support: return AIAdaptiveRule.Support;
            default: return action.Unit != null && action.Unit.kind == Kind.Scout ? AIAdaptiveRule.Recon : AIAdaptiveRule.Advance;
        }
    }

    public void Observe(AIActionRecord record)
    {
        if (!config.EnableAdaptivePolicy || record?.Before == null || record.After == null || record.Outcome == null
            || record.Before.CurrentThreatLevel < config.AdaptiveMinimumThreat) return;
        if (record.ActionType == AIActionType.Wait || record.ActionType == AIActionType.Rotate) return;
        int rule = record.AdaptiveRule;
        if (rule < 0 || rule >= Count) return;
        // One prolific actor cannot dominate a whole policy. Per-turn means are updated once below.
        if (turnSamples[rule] < 64)
        {
            turnRewards[rule] += Mathf.Clamp(AIReflectionConfig.Finite(record.Reward), -1, 1);
            turnSamples[rule]++;
        }
        var outcome = record.Outcome;
        turnProgress |= record.ExecutionSucceeded && (outcome.DamageDealt > 0 || outcome.EnemyKills > 0
            || outcome.DistanceToObjectiveDelta > 0 || outcome.NewTilesRevealed > 0
            || outcome.HealingDone > 0 || outcome.ArtifactsAcquired > 0 || outcome.ShieldReduced
            || outcome.ShieldGranted || outcome.EffectChanged);
    }

    public void EndTurn(AIActionContextSnapshot snapshot, bool? knownObjective = null)
    {
        if (!config.EnableAdaptivePolicy || snapshot == null || snapshot.CurrentThreatLevel < config.AdaptiveMinimumThreat)
        { ClearTurn(); return; }
        bool changed = false;
        float rate = Mathf.Clamp(AIReflectionConfig.Finite(config.AdaptiveLearningRate, .25f), .01f, .5f);
        for (int i = 0; i < Count; i++)
        {
            if (turnSamples[i] == 0) continue;
            float previous = state.Values[i];
            state.Values[i] = Mathf.Clamp(previous + rate * (turnRewards[i] / turnSamples[i] - previous), -1, 1);
            state.Samples[i] = Math.Min(1000000, state.Samples[i] + 1);
            changed |= Mathf.Abs(state.Values[i] - previous) >= .001f;
        }
        int oldStall = state.StalledTurns;
        // Empty peacetime or actual emergencies are not evidence that an assault policy failed.
        state.StalledTurns = (knownObjective ?? snapshot.HasObjective) && snapshot.OwnUnitCount >= 3 && !snapshot.EmergencyDefense
            && snapshot.EconomyState == EconomicState.Healthy && !turnProgress ? Math.Min(12, oldStall + 1) : 0;
        if (changed || oldStall != state.StalledTurns)
        {
            state.Revision = Math.Min(1000000, state.Revision + 1);
            state.LastReason = state.StalledTurns >= 3 ? "既知目標への進捗停滞：索敵・進軍を優先、追加投資を抑制"
                : "実行結果から探索・進軍・戦闘・支援・補充・投資の重みを更新";
        }
        ClearTurn();
    }

    public float Modifier(AIAction action, int threat)
    {
        if (!config.EnableAdaptivePolicy || action == null || threat < config.AdaptiveMinimumThreat
            || action.StrategicPriority < 3 || action.ActionType == AIActionType.Wait || action.ActionType == AIActionType.Rotate
            || action.Unit != null && MatchObjectiveRules.IsCommander(action.Unit)) return 0;
        int i = (int)Classify(action);
        float learned = state.Samples[i] >= Mathf.Clamp(config.AdaptiveMinimumSamples, 2, 20) ? state.Values[i] : 0;
        float stall = state.StalledTurns >= 3 ? Mathf.Min(1, state.StalledTurns / 6f) : 0;
        float bias = i == (int)AIAdaptiveRule.Recon || i == (int)AIAdaptiveRule.Advance ? stall
            : i == (int)AIAdaptiveRule.Economy || i == (int)AIAdaptiveRule.Recruit ? -stall : 0;
        return Mathf.Clamp(learned + bias, -1, 1) * Limit;
    }
    float Limit => Mathf.Clamp(AIReflectionConfig.Finite(config.AdaptiveMaximumScore, 18), 0, 30);

    public void ApplyPriorities(List<AIAction> actions, AIBoardState board)
    {
        if (!config.EnableAdaptivePolicy || state.StalledTurns < 3 || board?.Governor == null
            || board.ReconThreatLevel < config.AdaptiveMinimumThreat
            || board.Governor.Mode == StrategicMode.EmergencyDefense || board.Governor.Mode == StrategicMode.EconomicRecovery) return;
        foreach (var action in actions)
        {
            var actor = action.Unit;
            if (action.StrategicPriority != 4 || action.ActionType != AIActionType.Move || actor == null
                || !actor.IsAlive || actor.HasMovedThisTurn || MatchObjectiveRules.IsCommander(actor)
                || action.APCost < 0 || action.APCost > board.EnemyAP
                || board.EstimateCounterDamageAt(action.TargetPos, actor) >= actor.HP) continue;
            // Only an existing observation-derived objective can be promoted. No invented hidden goal.
            if (board.Operations?.IsPlannedMarch(action, board) == true
                || board.Governor.Exploration.IsObjectiveProgress(actor, action.TargetPos, board))
                action.StrategicPriority = 3;
        }
    }
    void ClearTurn()
    { Array.Clear(turnRewards, 0, Count); Array.Clear(turnSamples, 0, Count); turnProgress = false; }
}
