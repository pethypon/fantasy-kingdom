using System;
using System.Collections.Generic;

public enum AIStrategyCategory { Chess, Shogi, Go, RTS, TacticalSLG, Historical, General }
public enum AIStrategyEditorialStatus { DesignPrinciple, NeedsEditorialVerification }
public enum AIStrategyCostModel { OfficialActionAP, SharedActionSequenceAP, OfficialProductionForecast, ObservationOpportunity }

/// <summary>
/// Evidence queries, not additional game rules. A query is Unknown until a formal
/// legal-action/observation/forecast calculation supplies its result. In particular,
/// geometric proximity alone cannot establish a route, line of sight or a combo.
/// </summary>
public enum AIStrategyPredicate
{
    LegalMoveAvailable, LegalAttackAvailable, SharedAPAffordable,
    TwoImportantTargetsThreatened, DefenderOverloaded, ReserveCanCover,
    SafeDiversionAvailable, DefenderResponseObserved, FiringLineOpened,
    FatalThreatToObjective, DefenseActionEffective, ActivityImproves,
    IsolationRisk, OrderedOutcomeImproves, TradeGainExceedsLoss,
    EssentialDefenderLost, ForcingThreatAvailable, BetterEnemyReplyAvailable,
    LocalParticipationAdvantage, RearDefenseSafe, MutualSupportAvailable,
    ThreeLayerDefenseAvailable, SupportCellAvailable, WinningLegalSequence,
    NewlyObservedStrategicChange, ReassignmentImproves, ExistingDutySafe,
    ValuableKnownRegion, LegalTerritoryUseAvailable, EnemyGroupIsolated,
    EnemyReinforcementExcluded, SupportNetworkAvailable, AreaAttackExposure,
    SupportCutLegal, SafeCutPosition, LocalGlobalConflict,
    DecisionRelevantUnknown, ScoutInformationGainPositive,
    EconomyForecastAvailable, CompetingInvestmentAvailable,
    CounterAbilityObserved, CounterCapabilityAvailable,
    ReinforcementArrivalImproves, ReinforcementsTimely,
    OperationRoleMissing, ProductionMismatch, CitizenGrowthRespected,
    MultipleObservedFronts, SafeForceAllocation,
    AssemblyImprovesStrike, AssemblyHasFiniteDeadline,
    ExpeditionSupplyRisk, SupplyForecastAvailable,
    LegalFiringPositionAvailable, HeightAndLineOfSightVerified,
    WoundedFrontline, RotationKeepsRearSafe,
    DebuffImprovesAttack, DebuffEffectVerified,
    CounterResponseKnown, CounterLossSurvivable,
    SharedAPContention, AffordableRoleRecruitAvailable,
    LegalDisengageAvailable, ResumeConditionDefined,
    TwoKnownSideRoutes, CenterSurvives, FlankRestrictsEscape,
    InferiorObservedForce, DelayImprovesOutcome,
    TerrainChangesPlan, KnownTerrainRouteAvailable,
    AbilityFormationResponseImproves, ObservedTargetAvailable,
    StableObjectiveAvailable
}

public readonly struct AIKnowledgeRequirement
{
    public readonly AIStrategyPredicate Predicate;
    public readonly AITruthValue Expected;
    public AIKnowledgeRequirement(AIStrategyPredicate predicate, AITruthValue expected = AITruthValue.True)
    { Predicate = predicate; Expected = expected; }
}

/// <summary>Immutable design data. No score or game-state mutation is attached to a name.</summary>
public sealed class AIStrategyPrinciple
{
    public string Id { get; }
    public int Version { get; }
    public AIStrategyCategory Category { get; }
    public string Label { get; }
    public string Principle { get; }
    public IReadOnlyList<AIOperationGoal> GoalTags { get; }
    public IReadOnlyList<AIKnowledgeRequirement> Requirements { get; }
    public IReadOnlyList<string> OperatorIds { get; }
    public string EffectGroup { get; }
    public IReadOnlyList<string> ExpectedEffects { get; }
    public IReadOnlyList<string> FailureConditions { get; }
    public IReadOnlyList<string> CounterResponseTags { get; }
    public IReadOnlyList<string> FallbackOperatorIds { get; }
    public AIStrategyCostModel CostModel { get; }
    public string ReferenceLabel { get; }
    public AIStrategyEditorialStatus EditorialStatus { get; }
    public int MinimumThreatLevel { get; }

    public AIStrategyPrinciple(string id, int version, AIStrategyCategory category, string label,
        string principle, AIOperationGoal[] goals, AIKnowledgeRequirement[] requirements,
        string[] operators, string effectGroup, string[] expectedEffects, string[] failures,
        string[] responses, string[] fallbacks, AIStrategyCostModel costs,
        string referenceLabel, AIStrategyEditorialStatus editorialStatus, int minimumThreatLevel = 10)
    {
        Id = id; Version = version; Category = category; Label = label; Principle = principle;
        GoalTags = Freeze(goals); Requirements = Freeze(requirements); OperatorIds = Freeze(operators);
        EffectGroup = effectGroup; ExpectedEffects = Freeze(expectedEffects); FailureConditions = Freeze(failures);
        CounterResponseTags = Freeze(responses); FallbackOperatorIds = Freeze(fallbacks); CostModel = costs;
        ReferenceLabel = referenceLabel; EditorialStatus = editorialStatus; MinimumThreatLevel = minimumThreatLevel;
    }
    static IReadOnlyList<T> Freeze<T>(T[] values) => Array.AsReadOnly(values == null ? Array.Empty<T>() : (T[])values.Clone());
}
