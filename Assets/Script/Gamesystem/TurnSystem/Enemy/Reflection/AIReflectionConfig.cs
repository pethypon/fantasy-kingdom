using UnityEngine;

/// <summary>Optional, bounded experience layer. It never changes game rules or strategic priority.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/自己評価・日記設定", fileName = "ReflectionConfig")]
public sealed class AIReflectionConfig : ScriptableObject
{
    public bool EnableReflection = true;
    public bool ApplyLearningToSelection = true;
    public bool EnableConsoleDiary = true;
    public bool EnableFileDiary = true;
    public bool EnableCandidateDebug;
    [Min(1)] public int DiaryIntervalOwnTurns = 15;
    [Min(16)] public int MaxKnowledgeEntries = 2048;
    [Min(16)] public int MaxRecentRecords = 128;
    [Min(64)] public int MaxRewardEvents = 8192;
    [Min(1)] public int MaxActionEffectTargets = 128;
    [Range(.001f, 1f)] public float ReflectionLearningRate = .2f;
    [Range(.001f, 1f)] public float MinimumLearningRate = .02f;
    public float MinLearnedValue = -5f;
    public float MaxLearnedValue = 5f;
    [Range(0f, 2f)] public float LearningInfluenceMultiplier = 1f;
    [Min(1)] public int MinimumSamplesForSelection = 2;
    [Min(1)] public int FullConfidenceSamples = 10;
    // BALANCE_TODO: self-evaluation penalties are provisional and adjustable here only.
    [Min(0f)] public float RepeatedNoProgressPenalty = .25f;
    [Min(0f)] public float OscillationPenalty = .5f;
    [Min(0f)] public float FailedActionPenalty = .25f;
    public float DefeatReward;
    public const float NormalKillReward = 1f, FormationKillReward = 2f;
    public const float ArtifactReward = 1f, EconomyStableReward = 1f, VictoryReward = 100f;

    // BALANCE_TODO: immediate rewards and their evidence thresholds are centralized here.
    [Min(0f)] public float NormalKillRewardValue = 1f;
    [Min(0f)] public float FormationKillRewardValue = 2f;
    [Min(0f)] public float ArtifactRewardValue = 1f;
    [Min(0f)] public float EconomyRecoveryReward = 1f;
    [Min(0f)] public float GoodTradeSurvivalReward = .2f;
    [Min(0f)] public float OwnLossSurvivalPenalty = .6f;
    [Range(0f, 1f)] public float GoodTradeLightDamageRatio = .1f;
    [Min(0f)] public float RetreatSurvivalReward = .4f;
    [Min(0f)] public float RetreatPositionReward = .2f;
    [Range(0f, 1f)] public float RetreatLowHpThreshold = .4f;
    [Min(0f)] public float RetreatMaximumPowerRatio = 1f;
    [Range(0f, 1f)] public float RewardMaximumIncomingFraction = .8f;
    [Min(0f)] public float MinimumRiskImprovement = .01f;
    [Min(0f)] public float CrystalRetreatAllowance = 2f;
    [Min(0f)] public float HighGroundPositionReward = .3f;
    [Min(0f)] public float RangePositionReward = .3f;
    [Min(0f)] public float RangeSurvivalReward = .2f;
    [Min(0f)] public float BadRangePositionPenalty = .3f;
    [Min(0f)] public float RangedMinimumAttackRange = 1.5f;
    [Min(0f)] public float OverextensionBeforePowerRatio = .85f;
    [Min(0f)] public float OverextensionAfterPowerRatio = .6f;
    [Min(0f)] public float LocalPowerReward = .3f;
    [Min(0f)] public float LocalPowerPenalty = .4f;
    [Min(0f)] public float LocalPowerDeltaThreshold = .25f;
    [Min(0f)] public float ObjectiveProgressReward = .15f;
    [Min(0f)] public float DefenseReward = .5f;
    [Min(0f)] public float ScoutTileReward = .1f;
    [Min(0f)] public float ScoutEnemyDiscoveryReward = .2f;
    [Min(0f)] public float ScoutUncertaintyReward = .1f;
    [Min(0f)] public float PreparationReward = .2f;
    public float MinActionReward = -3f;
    public float MaxActionReward = 3f;
    [Min(0f)] public float CombatRewardCap = 2f;
    [Min(0f)] public float ArtifactRewardCap = 1f;
    [Min(0f)] public float SurvivalRewardCap = 1f;
    [Min(0f)] public float PositionRewardCap = .8f;
    [Min(0f)] public float LocalPowerRewardCap = .8f;
    [Min(0f)] public float ObjectiveRewardCap = .5f;
    [Min(0f)] public float EconomyRewardCap = 1f;
    [Min(0f)] public float InformationRewardCap = .5f;
    [Min(0f)] public float DefenseRewardCap = 1f;
    [Min(0f)] public float EfficiencyRewardCap = .5f;
    [Min(0f)] public float PreparationRewardCap = .5f;
    [Min(0f)] public float PenaltyCategoryCap = 1f;

    // BALANCE_TODO: battle-local causal credit and turn-level utilization policy.
    [Min(1)] public int DelayedCreditWindow = 5;
    [Range(0f, 1f)] public float DelayedCreditDecay = .6f;
    [Min(0f)] public float DelayedCreditBaseReward = .5f;
    [Min(0f)] public float MaxDelayedRewardPerAction = 1f;
    [Min(1)] public int IdleArmyTurnThreshold = 4;
    [Min(1)] public int MissedOpportunityTurnThreshold = 3;
    [Min(0f)] public float IdleArmyPenalty = .5f;
    [Min(0f)] public float ExcessiveProductionPenalty = .5f;
    [Min(0f)] public float MissedOpportunityPenalty = .4f;
    [Min(0f)] public float UnnecessaryTurtlePenalty = .4f;
    [Min(0f)] public float MaxArmyTurnPenalty = 1f;
    [Min(1f)] public float ArmyStrongPowerRatio = 1.5f;
    [Min(1)] public int ArmyProductionWindowTurns = 5;
    [Min(1)] public int ArmyOverproductionUnitGrowth = 5;
    [Range(0f, 1f)] public float ArmyLowUtilizationThreshold = .4f;
    [Min(0f)] public float ArmyDefenseRadius = 6f;
    [Min(0f)] public float ArmyEnemyFarDistance = 12f;
    [Range(0f, 1f)] public float ArmyAtBaseFraction = .6f;
    [Min(1)] public int EconomyRecoveryResetTurns = 2;
    [Min(1)] public int EarlyPhaseMaxTurns = 15;
    [Min(1)] public int LatePhaseMinTurns = 45;
    [Min(1)] public int LatePhaseUnitThreshold = 20;
    [Min(1)] public int LatePhaseTerritoryThreshold = 100;
    [Range(0f, 1f)] public float LatePhaseCrystalHpRatio = .3f;

    static AIReflectionConfig active;
    public static AIReflectionConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIReflectionConfig>("AI/ReflectionConfig");
            if (active == null)
            {
                active = CreateInstance<AIReflectionConfig>();
                active.hideFlags = HideFlags.HideAndDontSave;
            }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => active = null;
    internal static float Finite(float value, float fallback = 0f)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    internal int EntryLimit => Mathf.Clamp(MaxKnowledgeEntries, 16, 10000);
    internal int RecordLimit => Mathf.Clamp(MaxRecentRecords, 16, 1024);
    internal int EventLimit => Mathf.Clamp(MaxRewardEvents, 64, 65536);
    internal float Lower => Mathf.Clamp(Finite(MinLearnedValue, -5), -100, 0);
    internal float Upper => Mathf.Clamp(Finite(MaxLearnedValue, 5), 0, 100);
    internal float ActionLower => Mathf.Clamp(Finite(MinActionReward, -3), -100, 0);
    internal float ActionUpper => Mathf.Clamp(Finite(MaxActionReward, 3), 0, 100);
    internal static float NonNegative(float value) => Mathf.Max(0, Finite(value));
}
