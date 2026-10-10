using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional planning layer. All operation balancing is centralized here; game rules remain authoritative.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/複数ターン作戦設定", fileName = "OperationConfig")]
public sealed class AIOperationConfig : ScriptableObject
{
    [Header("有効化と経験学習")]
    public bool Enabled = true;
    public bool ApplyLearningToSelection = true;
    public bool EnablePersistentLearning = true;
    public bool EnableDebug;
    [Min(1)] public int MinimumThreatLevel = 10;
    [Min(1)] public int MinimumSamplesForSelection = 2;
    [Min(1)] public int FullConfidenceSamples = 10;
    [Range(.001f, 1f)] public float LearningRate = .15f;
    [Range(0f, 3f)] public float MaxOperationExperienceModifier = 3f;
    [Range(0f, 3f)] public float MaxActionBonus = 3f;
    public float OperationLearningMin = -3f;
    public float OperationLearningMax = 3f;
    [Range(0f, .5f)] public float MaxLearningRewardWithoutPrimaryGoal = .5f;
    [Range(0f, 1f)] public float MaxOperationCreditPerAction = 1f;
    [Range(0f, 1f)] public float OperationCreditDecay = .8f;
    [Range(0f, 1f)] public float DirectGoalContribution = 1f;
    [Range(0f, 1f)] public float PreparationContribution = .7f;
    [Range(0f, 1f)] public float ScoutContribution = .4f;
    [Range(0f, 1f)] public float SupportContribution = .5f;

    [Header("作戦の期間と保存上限")]
    [Min(1)] public int MaxConcurrentOperations = 3;
    [Min(1)] public int DefaultExpectedDuration = 8;
    [Min(1)] public int DefaultMaximumDuration = 15;
    [Min(1)] public int ReplanAfterStalledTurns = 3;
    [Min(16)] public int MaxKnowledgeEntries = 1000;
    [Min(16)] public int MaxActiveActionRecords = 128;
    [Min(1)] public int MaxCompletedOperations = 24;
    [Min(1)] public int MaxRevisions = 8;
    [Min(1)] public int MaxSteps = 12;
    [Min(1)] public int MaxAssignments = 64;
    [Min(16)] public int MaxCompletedOperationIds = 512;
    [Min(1024)] public int MaxProfileFileBytes = 8 * 1024 * 1024;

    [Header("主目標と最高評価の条件")]
    [Range(0f, 69f)] public float MaxScoreWithoutPrimaryGoal = 69f;
    [Range(0f, 100f)] public float PerfectScore = 100f;
    [Range(0f, 94f)] public float MaxScoreWithoutPerfectGate = 94f;
    [Range(0f, 99f)] public float MaxProvisionalScore = 94f;
    [Range(0f, 1f)] public float PerfectMinimumSurvival = .8f;
    [Range(0f, 1f)] public float PartialGoalProgressCap = .75f;
    [Range(0f, 1f)] public float CrystalDefenseMinimumHpRatio = .6f;
    [Range(0f, 1f)] public float CrystalDamageTolerance = .01f;
    [Range(0f, 1f)] public float CatastrophicLossRatio = .65f;
    [Range(0f, 1f)] public float LowLossRatio = .2f;
    [Range(0f, 1f)] public float GoodArmyUtilizationThreshold = .75f;
    [Range(0f, 1f)] public float UnderutilizationThreshold = .4f;
    [Range(0f, 1f)] public float StrongPreparationThreshold = .7f;
    [Range(0f, 1f)] public float SuccessfulExploitationThreshold = .5f;
    [Min(1)] public int DefaultScoutTargetTiles = 32;
    [Min(1)] public int FullInformationEnemyContacts = 3;
    [Min(1)] public int DefaultTerritoryGain = 4;
    [Range(.01f, 1f)] public float DefaultTargetPowerReduction = .5f;
    [Min(1f)] public float DefaultExpectedApBudget = 40f;
    [Min(1f)] public float EstimatedKillPower = 20f;
    [Min(1)] public int DiversionMinimumEnemies = 2;
    [Min(1)] public int DiversionMinimumDistanceGain = 2;
    [Min(1)] public int ObjectiveDefenseRadius = 3;

    [Header("評価ランクと学習報酬")]
    [Range(0f, 100f)] public float PerfectRankThreshold = 95f;
    [Range(0f, 100f)] public float ExcellentRankThreshold = 85f;
    [Range(0f, 100f)] public float SuccessRankThreshold = 70f;
    [Range(0f, 100f)] public float PartialRankThreshold = 55f;
    [Range(0f, 100f)] public float NeutralRankThreshold = 40f;
    [Range(0f, 100f)] public float FailureRankThreshold = 20f;
    public float PerfectLearningReward = 3f;
    public float ExcellentLearningReward = 2f;
    public float SuccessLearningReward = 1f;
    public float PartialLearningReward = .3f;
    public float NeutralLearningReward;
    public float FailureLearningReward = -1f;
    public float CriticalFailureLearningReward = -3f;
    [Range(40f, 54f)] public float StrategicAbortScore = 45f;
    [Range(-.5f, .5f)] public float StrategicAbortLearningReward;

    [Header("危険と損失の減点")]
    [Min(0f)] public float CrystalExposurePenalty = 12f;
    [Min(0f)] public float EconomicCollapsePenalty = 15f;
    [Min(0f)] public float CatastrophicLossPenalty = 20f;
    [Min(0f)] public float StallPenalty = 4f;
    [Min(0f)] public float ArmyIdlePenalty = 3f;
    [Min(0f)] public float BadIntelPenalty = 5f;
    [Min(0f)] public float ResourceWastePenalty = 4f;
    [Min(0f)] public float OvercommitmentPenalty = 6f;
    [Range(0f, 100f)] public float MaximumPenalty = 30f;
    [Range(0f, 19f)] public float MaxScoreDestroyedOwnCrystal;

    [Header("目的別の配点（合計100点）")]
    public List<AIOperationGoalWeights> GoalWeights = CreateDefaultWeights();

    public int EntryLimit => Mathf.Clamp(MaxKnowledgeEntries, 16, 8192);
    public int RecordLimit => Mathf.Clamp(MaxActiveActionRecords, 16, 256);
    public int CompletedLimit => Mathf.Clamp(MaxCompletedOperations, 1, 64);
    public int StepLimit => Mathf.Clamp(MaxSteps, 1, 32);
    public int AssignmentLimit => Mathf.Clamp(MaxAssignments, 1, 128);
    public int RevisionLimit => Mathf.Clamp(MaxRevisions, 1, 32);
    public int EventLimit => Mathf.Clamp(MaxCompletedOperationIds, 16, 8192);
    public int ConcurrentLimit => Mathf.Clamp(MaxConcurrentOperations, 1, 3);
    // Compatibility names are read-only aliases, so there is only one serialized setting per limit.
    public int MaxOperationActions => MaxActiveActionRecords;
    public int MaxAssignedUnits => MaxAssignments;

    static AIOperationConfig active;
    public static AIOperationConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIOperationConfig>("AI/OperationConfig");
            if (active == null)
            {
                active = CreateInstance<AIOperationConfig>();
                active.hideFlags = HideFlags.HideAndDontSave;
            }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => active = null;

    readonly Dictionary<AIOperationGoal, AIOperationGoalWeights> fallbackWeights = new Dictionary<AIOperationGoal, AIOperationGoalWeights>();
    public AIOperationGoalWeights GetGoalWeights(AIOperationGoal goal)
    {
        if (GoalWeights != null) foreach (var item in GoalWeights) if (item != null && item.Goal == goal) return item;
        if (!fallbackWeights.TryGetValue(goal, out var fallback)) fallbackWeights.Add(goal, fallback = DefaultWeights(goal));
        return fallback;
    }
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static float Finite(float value, float fallback = 0f) => IsFinite(value) ? value : fallback;
    public static float Unit(float value) => Mathf.Clamp01(Finite(value));
    public static float NonNegative(float value) => Mathf.Max(0f, Finite(value));

    static List<AIOperationGoalWeights> CreateDefaultWeights()
    {
        var result = new List<AIOperationGoalWeights>();
        foreach (AIOperationGoal goal in Enum.GetValues(typeof(AIOperationGoal))) if (goal != AIOperationGoal.None) result.Add(DefaultWeights(goal));
        return result;
    }
    static AIOperationGoalWeights DefaultWeights(AIOperationGoal goal)
    {
        // BALANCE_TODO: provisional operation weights; edit this asset instead of scattering constants into decisions.
        if (goal == AIOperationGoal.DefendOwnCrystal || goal == AIOperationGoal.DefendSubCrystal || goal == AIOperationGoal.HoldTerritory)
            return new AIOperationGoalWeights { Goal = goal, GoalAchievement = 40, Defense = 20, Survival = 10, MilitaryOutcome = 10,
                ArmyUtilization = 5, RiskControl = 5, Position = 5, Efficiency = 3, TimeEfficiency = 2 };
        if (goal == AIOperationGoal.ScoutRegion || goal == AIOperationGoal.LocateEnemyMainForce)
            return new AIOperationGoalWeights { Goal = goal, GoalAchievement = 40, Information = 25, Survival = 10, Efficiency = 5,
                Position = 5, RiskControl = 5, TimeEfficiency = 5, ArmyUtilization = 5 };
        if (goal == AIOperationGoal.EconomicRecovery || goal == AIOperationGoal.SecureResourceArea)
            return new AIOperationGoalWeights { Goal = goal, GoalAchievement = 40, Economy = 30, Efficiency = 10,
                Defense = 5, Survival = 5, RiskControl = 5, TimeEfficiency = 5 };
        if (goal == AIOperationGoal.CreateDiversion || goal == AIOperationGoal.FlankEnemyForce || goal == AIOperationGoal.SurroundEnemyForce)
            return new AIOperationGoalWeights { Goal = goal, GoalAchievement = 40, MilitaryOutcome = 5, Survival = 10, Efficiency = 5,
                Position = 10, Information = 5, Defense = 5, TimeEfficiency = 5, ArmyUtilization = 5, Preparation = 5, RiskControl = 5 };
        return new AIOperationGoalWeights { Goal = goal, GoalAchievement = 40, MilitaryOutcome = 10, Survival = 10, Efficiency = 8,
            Position = 5, Territory = 5, Economy = 3, Information = 3, Defense = 3, TimeEfficiency = 5, ArmyUtilization = 3,
            Preparation = 3, RiskControl = 2 };
    }
}

[Serializable]
public sealed class AIOperationGoalWeights
{
    public AIOperationGoal Goal;
    public float GoalAchievement, MilitaryOutcome, Survival, Efficiency, Position, Territory, Economy;
    public float Information, Defense, TimeEfficiency, ArmyUtilization, Preparation, Exploitation, RiskControl;
    public float Total => NonNegative(GoalAchievement) + NonNegative(MilitaryOutcome) + NonNegative(Survival) + NonNegative(Efficiency)
        + NonNegative(Position) + NonNegative(Territory) + NonNegative(Economy) + NonNegative(Information) + NonNegative(Defense)
        + NonNegative(TimeEfficiency) + NonNegative(ArmyUtilization) + NonNegative(Preparation) + NonNegative(Exploitation) + NonNegative(RiskControl);
    public AIOperationGoalWeights Copy() => (AIOperationGoalWeights)MemberwiseClone();
    public void Normalize()
    {
        float total = Total;
        if (total <= 0f) { GoalAchievement = 100f; return; }
        float scale = 100f / total;
        GoalAchievement = NonNegative(GoalAchievement) * scale; MilitaryOutcome = NonNegative(MilitaryOutcome) * scale;
        Survival = NonNegative(Survival) * scale; Efficiency = NonNegative(Efficiency) * scale;
        Position = NonNegative(Position) * scale; Territory = NonNegative(Territory) * scale; Economy = NonNegative(Economy) * scale;
        Information = NonNegative(Information) * scale; Defense = NonNegative(Defense) * scale; TimeEfficiency = NonNegative(TimeEfficiency) * scale;
        ArmyUtilization = NonNegative(ArmyUtilization) * scale; Preparation = NonNegative(Preparation) * scale;
        Exploitation = NonNegative(Exploitation) * scale; RiskControl = NonNegative(RiskControl) * scale;
    }
    static float NonNegative(float value) => AIOperationConfig.NonNegative(value);
}
