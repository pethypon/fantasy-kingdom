using System;
using UnityEngine;

/// <summary>Structured actual rewards. Hard outcomes keep their value while soft additions share the action budget.</summary>
[Serializable]
public sealed class AIRewardBreakdown
{
    public float Combat, Artifact, Survival, Position, LocalPower, Objective, Economy, Information, Defense;
    public float Efficiency, Preparation, RepeatPenalty, FailurePenalty, IdleArmyPenalty;
    public float OverproductionPenalty, MissedOpportunityPenalty, DelayedReward;
    public float Total => Combat + Artifact + Survival + Position + LocalPower + Objective + Economy
        + Information + Defense + Efficiency + Preparation + RepeatPenalty + FailurePenalty
        + IdleArmyPenalty + OverproductionPenalty + MissedOpportunityPenalty + DelayedReward;

    public AIRewardBreakdown Copy() => (AIRewardBreakdown)MemberwiseClone();
    public void Add(AIRewardBreakdown value)
    {
        if (value == null) return;
        Combat += value.Combat; Artifact += value.Artifact; Survival += value.Survival; Position += value.Position;
        LocalPower += value.LocalPower; Objective += value.Objective; Economy += value.Economy;
        Information += value.Information; Defense += value.Defense; Efficiency += value.Efficiency;
        Preparation += value.Preparation; RepeatPenalty += value.RepeatPenalty; FailurePenalty += value.FailurePenalty;
        IdleArmyPenalty += value.IdleArmyPenalty; OverproductionPenalty += value.OverproductionPenalty;
        MissedOpportunityPenalty += value.MissedOpportunityPenalty; DelayedReward += value.DelayedReward;
    }
    public static AIRewardBreakdown Difference(AIRewardBreakdown after, AIRewardBreakdown before)
    {
        var delta = after != null ? after.Copy() : new AIRewardBreakdown();
        if (before == null) return delta;
        delta.Combat -= before.Combat; delta.Artifact -= before.Artifact; delta.Survival -= before.Survival;
        delta.Position -= before.Position; delta.LocalPower -= before.LocalPower; delta.Objective -= before.Objective;
        delta.Economy -= before.Economy; delta.Information -= before.Information; delta.Defense -= before.Defense;
        delta.Efficiency -= before.Efficiency; delta.Preparation -= before.Preparation;
        delta.RepeatPenalty -= before.RepeatPenalty; delta.FailurePenalty -= before.FailurePenalty;
        delta.IdleArmyPenalty -= before.IdleArmyPenalty; delta.OverproductionPenalty -= before.OverproductionPenalty;
        delta.MissedOpportunityPenalty -= before.MissedOpportunityPenalty; delta.DelayedReward -= before.DelayedReward;
        return delta;
    }
    public void Clamp(AIReflectionConfig config)
    {
        if (config == null) config = AIReflectionConfig.Active;
        Combat = Positive(Combat, config.CombatRewardCap); Artifact = Positive(Artifact, config.ArtifactRewardCap);
        Survival = Signed(Survival, config.SurvivalRewardCap); Position = Signed(Position, config.PositionRewardCap);
        LocalPower = Signed(LocalPower, config.LocalPowerRewardCap); Objective = Signed(Objective, config.ObjectiveRewardCap);
        Economy = Signed(Economy, config.EconomyRewardCap); Information = Signed(Information, config.InformationRewardCap);
        Defense = Signed(Defense, config.DefenseRewardCap); Efficiency = Signed(Efficiency, config.EfficiencyRewardCap);
        Preparation = Signed(Preparation, config.PreparationRewardCap);
        RepeatPenalty = Negative(RepeatPenalty, config.PenaltyCategoryCap);
        FailurePenalty = Negative(FailurePenalty, config.PenaltyCategoryCap);
        IdleArmyPenalty = Negative(IdleArmyPenalty, config.PenaltyCategoryCap);
        OverproductionPenalty = Negative(OverproductionPenalty, config.PenaltyCategoryCap);
        MissedOpportunityPenalty = Negative(MissedOpportunityPenalty, config.PenaltyCategoryCap);
        DelayedReward = Positive(DelayedReward, config.MaxDelayedRewardPerAction);
        float total = Total;
        if (total > config.ActionUpper)
        {
            float positiveSoft = SoftSum(true);
            float excess = total - config.ActionUpper;
            if (positiveSoft > 0) ScaleSoft(Mathf.Clamp01((positiveSoft - excess) / positiveSoft), true);
            // Authored caps can leave hard rewards larger than the entire action budget.
            float hard = Combat + Artifact;
            if (Total > config.ActionUpper && hard > 0)
            {
                float scale = Mathf.Clamp01((hard - (Total - config.ActionUpper)) / hard);
                Combat *= scale; Artifact *= scale;
            }
        }
        if (Total < config.ActionLower)
        {
            float negativeSoft = -SoftSum(false);
            if (negativeSoft > 0) ScaleSoft(Mathf.Clamp01((negativeSoft - (config.ActionLower - Total)) / negativeSoft), false);
        }
    }
    float SoftSum(bool positive)
        => Part(Survival, positive) + Part(Position, positive) + Part(LocalPower, positive)
            + Part(Objective, positive) + Part(Economy, positive) + Part(Information, positive)
            + Part(Defense, positive) + Part(Efficiency, positive) + Part(Preparation, positive)
            + Part(RepeatPenalty, positive) + Part(FailurePenalty, positive) + Part(IdleArmyPenalty, positive)
            + Part(OverproductionPenalty, positive) + Part(MissedOpportunityPenalty, positive) + Part(DelayedReward, positive);
    void ScaleSoft(float scale, bool positive)
    {
        Survival = Scale(Survival, scale, positive); Position = Scale(Position, scale, positive);
        LocalPower = Scale(LocalPower, scale, positive); Objective = Scale(Objective, scale, positive);
        Economy = Scale(Economy, scale, positive); Information = Scale(Information, scale, positive);
        Defense = Scale(Defense, scale, positive); Efficiency = Scale(Efficiency, scale, positive);
        Preparation = Scale(Preparation, scale, positive); RepeatPenalty = Scale(RepeatPenalty, scale, positive);
        FailurePenalty = Scale(FailurePenalty, scale, positive); IdleArmyPenalty = Scale(IdleArmyPenalty, scale, positive);
        OverproductionPenalty = Scale(OverproductionPenalty, scale, positive);
        MissedOpportunityPenalty = Scale(MissedOpportunityPenalty, scale, positive); DelayedReward = Scale(DelayedReward, scale, positive);
    }
    static float Part(float value, bool positive) => positive ? Mathf.Max(0, value) : Mathf.Min(0, value);
    static float Scale(float value, float scale, bool positive) => (positive ? value > 0 : value < 0) ? value * scale : value;
    static float Signed(float value, float cap) => Mathf.Clamp(AIReflectionConfig.Finite(value), -AIReflectionConfig.NonNegative(cap), AIReflectionConfig.NonNegative(cap));
    static float Positive(float value, float cap) => Mathf.Clamp(AIReflectionConfig.Finite(value), 0, AIReflectionConfig.NonNegative(cap));
    static float Negative(float value, float cap) => Mathf.Clamp(AIReflectionConfig.Finite(value), -AIReflectionConfig.NonNegative(cap), 0);
}
