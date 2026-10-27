using System;
using System.Collections.Generic;

public enum AIStrategyOperatorKind { ActionSelection, PureEvaluation, ObservationBarrier, OperationAssignment, EconomicDemand }

/// <summary>Translation contract only. Every execution still requires a current official AIAction.</summary>
public sealed class AIStrategyOperator
{
    public string Id { get; }
    public AIStrategyOperatorKind Kind { get; }
    public IReadOnlyList<AIActionType> ActionTypes { get; }
    public IReadOnlyList<AIStrategyPredicate> RequiredFacts { get; }
    public string ExpectedObservedEffect { get; }
    public string AbortCondition { get; }
    public bool IsDirectlyExecutable => Kind == AIStrategyOperatorKind.ActionSelection;
    public bool RequiresCurrentFormalValidation => true;
    internal AIStrategyOperator(string id, AIStrategyOperatorKind kind, AIActionType[] actions,
        AIStrategyPredicate[] facts, string effect, string abort)
    {
        Id = id; Kind = kind; ActionTypes = Array.AsReadOnly((AIActionType[])actions.Clone());
        RequiredFacts = Array.AsReadOnly((AIStrategyPredicate[])facts.Clone());
        ExpectedObservedEffect = effect; AbortCondition = abort;
    }
    public bool Accepts(AIActionType action)
    { foreach (var candidate in ActionTypes) if (candidate == action) return true; return false; }
}

public static class AIStrategyOperatorCatalog
{
    static readonly Dictionary<string, AIStrategyOperator> index = Build();
    static readonly IReadOnlyList<AIStrategyOperator> all = Freeze();
    public static IReadOnlyList<AIStrategyOperator> All => all;
    public static bool TryGet(string id, out AIStrategyOperator value)
    { if (id == null) { value = null; return false; } return index.TryGetValue(id, out value); }
    static IReadOnlyList<AIStrategyOperator> Freeze()
    {
        var list = new List<AIStrategyOperator>(index.Values);
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); return list.AsReadOnly();
    }
    static Dictionary<string, AIStrategyOperator> Build()
    {
        var map = new Dictionary<string, AIStrategyOperator>(StringComparer.Ordinal);
        Add(map, "REPOSITION;IMPROVE_POSITION;FIND_FIRING_POSITION;USE_KNOWN_TERRAIN", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Move, AIActionType.DefenseRepos, AIActionType.Rotate },
            new[] { AIStrategyPredicate.LegalMoveAvailable }, "ActualPositionOrFacingChanged", "MoveOrAPInvalidated");
        Add(map, "ATTACK;FOCUS_FIRE;PRESS_ADVANTAGE;FINISH_SEQUENCE", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Attack, AIActionType.SkillUse },
            new[] { AIStrategyPredicate.LegalAttackAvailable, AIStrategyPredicate.ObservedTargetAvailable },
            "ActualTargetHPOrProtectionChanged", "TargetNotObservedOrAttackInvalidated");
        Add(map, "OPEN_LINE", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Move, AIActionType.DefenseRepos },
            new[] { AIStrategyPredicate.LegalMoveAvailable, AIStrategyPredicate.FiringLineOpened },
            "OfficialRangedTargetsRegeneratedAfterMove", "NoActualLineOpened");
        Add(map, "DEBUFF_THEN_ATTACK", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.SkillUse },
            new[] { AIStrategyPredicate.DebuffImprovesAttack, AIStrategyPredicate.DebuffEffectVerified },
            "ActualTargetStatusChangedBeforeAttackRegeneration", "DebuffFailedOrTargetChanged");
        Add(map, "BLOCK_THREAT;DEFEND;FORM_DEFENSE", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.DefenseRepos, AIActionType.Support, AIActionType.Move, AIActionType.SkillUse },
            new[] { AIStrategyPredicate.DefenseActionEffective }, "FormalObjectiveRiskReduced", "ObjectiveRiskNotReduced");
        Add(map, "FORM_SUPPORT;BUILD_SUPPORT_NET;ADAPT_FORMATION", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Support, AIActionType.Move, AIActionType.DefenseRepos, AIActionType.SkillUse },
            new[] { AIStrategyPredicate.SupportCellAvailable }, "ActualLegalSupportCoverageChanged", "SupportActorUnavailable");
        Add(map, "DIVERSION;FEINT_CENTER", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Move, AIActionType.Surround, AIActionType.Attack },
            new[] { AIStrategyPredicate.SafeDiversionAvailable }, "ActualThreatPositionReachedThenObserveOpponent", "ActorUnsafeOrResponseAbsent");
        Add(map, "FLANK;SURROUND;PINCH;CUT_SUPPORT", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Move, AIActionType.Surround },
            new[] { AIStrategyPredicate.LegalMoveAvailable, AIStrategyPredicate.KnownTerrainRouteAvailable },
            "ActualApproachOrObservedEnemyActivityChanged", "RouteOrSupportInvalidated");
        Add(map, "SCOUT_FOR_ANSWER", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Move },
            new[] { AIStrategyPredicate.DecisionRelevantUnknown, AIStrategyPredicate.ScoutInformationGainPositive },
            "NewLawfullyObservedFact", "NoInformationGainOrScoutUnsafe");
        Add(map, "ROTATE_FRONTLINE;DISENGAGE", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Retreat, AIActionType.DefenseRepos, AIActionType.Move },
            new[] { AIStrategyPredicate.LegalDisengageAvailable }, "ActualSurvivalOrRearCoverageImproved", "RetreatCausesImmediateDefeat");
        Add(map, "HOLD", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Wait, AIActionType.DefenseRepos },
            new[] { AIStrategyPredicate.StableObjectiveAvailable }, "ActualObjectiveCoverageMaintained", "ObjectiveChangedOrDeadlineExpired");
        Add(map, "CONTROL_REGION", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Move, AIActionType.SubCrystal, AIActionType.Build },
            new[] { AIStrategyPredicate.ValuableKnownRegion, AIStrategyPredicate.LegalTerritoryUseAvailable },
            "FormalTerritoryVisionOrResourceAccessChanged", "NoRealRegionValueOrPlacementIllegal");
        Add(map, "REBUILD", AIStrategyOperatorKind.ActionSelection,
            new[] { AIActionType.Build, AIActionType.Upgrade, AIActionType.Summon },
            new[] { AIStrategyPredicate.EconomyForecastAvailable }, "ActualPaidBuildUpgradeOrSummon", "OfficialResourceOrCitizenConstraint");
        Add(map, "ASSEMBLE;SYNCHRONIZE;REGROUP;CONCENTRATE;REASSIGN_ROLE;ALLOCATE_FRONT;SWITCH_STANCE;DELAY", AIStrategyOperatorKind.OperationAssignment,
            Array.Empty<AIActionType>(), new[] { AIStrategyPredicate.StableObjectiveAvailable },
            "ExistingOperationOrSquadAssignmentUpdatedThenLegalActionSelected", "AssignmentInvalidOrFiniteDeadlineExpired");
        Add(map, "REINFORCE;RECRUIT_MISSING_ROLE;PLAN_PRODUCTION;COUNTER_COMPOSITION", AIStrategyOperatorKind.EconomicDemand,
            Array.Empty<AIActionType>(), new[] { AIStrategyPredicate.OperationRoleMissing, AIStrategyPredicate.EconomyForecastAvailable },
            "BoundedExistingRecruitmentDemandThenOfficialPaidSummon", "NoRoleNeedOrForecastUnsafe");
        Add(map, "OBSERVE_RESPONSE", AIStrategyOperatorKind.ObservationBarrier,
            Array.Empty<AIActionType>(), Array.Empty<AIStrategyPredicate>(),
            "SubsequentCurrentObservationSupportsOrRefutesExpectedResponse", "ResponseAbsentOrObservationExpired");
        Add(map, "THREATEN_TWO;SPLIT_THREAT;EVALUATE_RESPONSES;ORDER_ACTIONS;TRADE_EVALUATE;FORCE_RESPONSE;ISOLATE_TARGET;GLOBAL_TRADEOFF;ALLOCATE_RESOURCES;CHECK_SUPPLY;BUDGET_COUNTER;SHARE_AP", AIStrategyOperatorKind.PureEvaluation,
            Array.Empty<AIActionType>(), Array.Empty<AIStrategyPredicate>(),
            "BoundedValuePlanComparisonOnly", "MissingEvidenceOrPlanningBudgetExpired");
        return map;
    }
    static void Add(Dictionary<string, AIStrategyOperator> map, string names, AIStrategyOperatorKind kind,
        AIActionType[] actions, AIStrategyPredicate[] facts, string effect, string abort)
    {
        foreach (var name in names.Split(';')) map.Add(name, new AIStrategyOperator(name, kind, actions, facts, effect, abort));
    }
}
