using System;
using System.Collections.Generic;
using UnityEngine;

public enum BasicResourceState { Critical, Shortage, Stable, Surplus }
public enum EconomyFoundationState { OpeningFoundation, StableGrowth, ResourceRecovery }
public enum ResourceSpendPriority { EssentialDefense, Foundation, Strategic, Optional }

[Serializable]
public sealed class AIBasicResourceSettings
{
    public bool enabled = true;
    [Range(.05f, 1f)] public float criticalCoverage = .65f;
    [Range(.1f, 2f)] public float recoveryCoverage = .90f;
    [Range(.1f, 3f)] public float stableCoverage = 1f;
    [Range(1f, 5f)] public float surplusCoverage = 1.6f;
    [Range(1, 8)] public int stableTurns = 2;
    [Range(1, 8)] public int minimumRecoveryTurns = 2;
    [Range(1f, 3f)] public float switchPressureRatio = 1.3f;
    [Min(0)] public float deficitWeight = 70f;
    [Min(0)] public float blockedPlanWeight = 55f;
    [Min(0)] public float incomeGapWeight = 45f;
    [Min(0)] public float zeroProducerWeight = 65f;
    [Min(0)] public float openingWeight = 30f;
    [Min(0)] public float surplusPenaltyWeight = 75f;
    [Range(0, 1)] public float planWeight = .75f;
    [Range(0, 1)] public float reserveCostFraction = .5f;
    [Range(0, 1)] public float foundationReserveFraction = .25f;
    [Min(1)] public float paybackWindowTurns = 8f;
    [Min(0)] public float constructionPressureWeight = 30f;
    [Min(0)] public float recoverySpendPenalty = 60f;
    public FacilityData.ProductionBundle minimumReserve = new FacilityData.ProductionBundle { Wood = 30, Stone = 30, Iron = 3, MagicOre = 2, Wheat = 3, Bread = 5, Water = 20, Citizen = 3 };
    static readonly AIBasicResourceSettings defaults = new AIBasicResourceSettings();
    public static AIBasicResourceSettings Active => AIEconomySettings.Active.basicResources ?? defaults;
}

public sealed class BasicResourceSnapshot
{
    public ResourceKind ResourceType;
    public int CurrentStock, ProducerCount;
    public float IncomePerTurn, ExpectedIncome, PlannedDemand, ReserveTarget, ProjectedAvailable, ProjectedDemand;
    public float Coverage, Deficit, BlockedPlanValue, PressureScore;
    public float PermanentProductionPerTurn, PotentialProductionPerTurn, MandatoryDemandPerTurn, IncomeGap;
    public BasicResourceState State;
    public override string ToString() => $"{ResourceType} stock={CurrentStock} income={IncomePerTurn:F1} planned={PlannedDemand:F1} "
        + $"reserve={ReserveTarget:F1} coverage={Coverage:F2} blocked={BlockedPlanValue:F2} pressure={PressureScore:F1} state={State}";
}

public readonly struct BasicResourcePlan
{
    public readonly FacilityData.ProductionBundle Cost;
    public readonly float StrategicValue, Urgency;
    public BasicResourcePlan(FacilityData.ProductionBundle cost, float strategicValue = 1f, float urgency = 1f)
    { Cost = cost; StrategicValue = AIEconomySettings.NonNegative(strategicValue); Urgency = AIEconomySettings.NonNegative(urgency); }
    public BasicResourcePlan(FacilityData.ResourceCost cost, float strategicValue = 1f, float urgency = 1f)
        : this(AIBasicResourceEconomy.ToBundle(cost), strategicValue, urgency) { }
}

/// <summary>
/// Resource foundation policy. The governor owns this memory across turns; snapshots and candidate
/// assessments are reused within a board generation. Only own recipes and visible strategic information are read.
/// </summary>
public sealed class AIBasicResourceEconomy
{
    static readonly ResourceKind[] resources = { ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Iron, ResourceKind.MagicOre,
        ResourceKind.Wheat, ResourceKind.Bread, ResourceKind.Water, ResourceKind.Citizen };
    readonly BasicResourceSnapshot[] snapshots = CreateSnapshots();
    readonly bool[] critical = new bool[StrategicEconomyForecast.ResourceCount];
    readonly int[] blockedTurns = new int[StrategicEconomyForecast.ResourceCount];
    readonly List<BasicResourcePlan> plans = new List<BasicResourcePlan>(9);
    readonly List<Status> buildings = new List<Status>(32);
    readonly HashSet<(FacilityKind, FacilityDefinitionData, Status)> plannedSources = new HashSet<(FacilityKind, FacilityDefinitionData, Status)>();
    readonly Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, Kind, UnitData), float> scores
        = new Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, Kind, UnitData), float>();
    AIBoardState board;
    StrategicProductionDemand demand;
    int generation = -1, lastTurn = -1, stableTurnCount, recoveryStarted = -1;
    bool emergency;
    public EconomyFoundationState FoundationState { get; private set; } = EconomyFoundationState.OpeningFoundation;
    public ResourceKind PrimaryBottleneck { get; private set; } = ResourceKind.None;
    public int FeasibleSupplyAP { get; private set; }
    public IReadOnlyList<BasicResourceSnapshot> Resources => snapshots;
    public bool HasCriticalDeficit { get { foreach (bool value in critical) if (value) return true; return false; } }
    public bool HasShortage { get { foreach (var value in snapshots) if (value.State <= BasicResourceState.Shortage) return true; return false; } }
    public bool HasBlockedPlans { get { foreach (var value in snapshots) if (value.BlockedPlanValue > 0) return true; return false; } }

    static BasicResourceSnapshot[] CreateSnapshots()
    {
        var result = new BasicResourceSnapshot[resources.Length];
        for (int i = 0; i < result.Length; i++) result[i] = new BasicResourceSnapshot { ResourceType = resources[i] };
        return result;
    }

    public BasicResourceSnapshot Get(ResourceKind resource)
    {
        for (int i = 0; i < resources.Length; i++) if (resources[i] == resource) return snapshots[i];
        return null;
    }

    public void Evaluate(AIBoardState current, StrategicProductionDemand productionDemand, bool essentialDefense,
        IReadOnlyList<BasicResourcePlan> plannedActions = null)
    {
        if (current == null || productionDemand == null) return;
        if (board == current && generation == current.Generation && demand == productionDemand && emergency == essentialDefense && plannedActions == null) return;
        board = current; generation = current.Generation; demand = productionDemand; emergency = essentialDefense; scores.Clear();
        if (plannedActions == null) CollectPlans();
        var selected = plannedActions ?? plans;
        int horizon = Mathf.Clamp(AIEconomySettings.Active.forecastTurns, 1, 8);
        var policy = AIBasicResourceSettings.Active;
        for (int i = 0; i < resources.Length; i++)
        {
            var source = demand.Get(resources[i]); var target = snapshots[i];
            target.CurrentStock = Mathf.Max(0, Mathf.RoundToInt(source.Stock));
            target.PermanentProductionPerTurn = demand.PermanentProduction(resources[i]);
            target.PotentialProductionPerTurn = demand.PotentialProduction(resources[i]);
            target.MandatoryDemandPerTurn = source.MandatoryDemandPerTurn;
            target.IncomePerTurn = target.PotentialProductionPerTurn - target.MandatoryDemandPerTurn;
            target.ExpectedIncome = target.IncomePerTurn * horizon + demand.TemporaryIncome(resources[i]);
            target.ProducerCount = demand.PotentialSourceCount(resources[i]);
            target.PlannedDemand = 0; target.BlockedPlanValue = 0;
            target.ReserveTarget = Mathf.Max(source.SafetyReserve, StrategicEconomyForecast.Amount(policy.minimumReserve, (int)resources[i]));
            for (int p = 0; p < selected.Count; p++)
            {
                var plan = selected[p]; float cost = StrategicEconomyForecast.Amount(plan.Cost, (int)resources[i]);
                target.PlannedDemand += cost * Mathf.Clamp01(policy.planWeight);
                target.ReserveTarget = Mathf.Max(target.ReserveTarget, cost * Mathf.Clamp01(policy.reserveCostFraction));
                if (cost > target.CurrentStock)
                    target.BlockedPlanValue += plan.StrategicValue * plan.Urgency * Mathf.Clamp01((cost - target.CurrentStock) / Mathf.Max(1, cost));
            }
            float targetIncome = target.PlannedDemand / horizon
                + Mathf.Max(0, target.ReserveTarget - target.CurrentStock) / Mathf.Max(1, AIEconomySettings.Active.recoveryWindowTurns);
            target.IncomeGap = Mathf.Max(0, targetIncome - target.IncomePerTurn);
        }
        RefreshCitizenSupply(horizon, selected);
        UpdateState(current.TurnCount, essentialDefense);
        FindFeasibleSupplyAP();
    }

    /// <summary>Pure diagnostic entry for tuning and regression checks; does not touch a faction or scene.</summary>
    public void EvaluateSnapshots(IReadOnlyList<BasicResourceSnapshot> source, int turn, bool essentialDefense = false)
    {
        board = null; demand = null; generation = -1; emergency = essentialDefense; scores.Clear();
        FeasibleSupplyAP = 0;
        for (int i = 0; i < snapshots.Length; i++)
        {
            BasicResourceSnapshot match = null;
            for (int p = 0; source != null && p < source.Count; p++)
                if (source[p] != null && source[p].ResourceType == resources[i]) { match = source[p]; break; }
            var target = snapshots[i];
            target.CurrentStock = Mathf.Max(0, match?.CurrentStock ?? 0);
            target.ProducerCount = Mathf.Max(0, match?.ProducerCount ?? 0);
            target.IncomePerTurn = Finite(match?.IncomePerTurn ?? 0);
            target.ExpectedIncome = Finite(match?.ExpectedIncome ?? 0);
            target.PlannedDemand = Positive(match?.PlannedDemand ?? 0);
            target.ReserveTarget = Positive(match?.ReserveTarget ?? 0);
            target.BlockedPlanValue = Positive(match?.BlockedPlanValue ?? 0);
            target.PermanentProductionPerTurn = Positive(match?.PermanentProductionPerTurn ?? 0);
            target.PotentialProductionPerTurn = Positive(match?.PotentialProductionPerTurn ?? match?.PermanentProductionPerTurn ?? 0);
            target.MandatoryDemandPerTurn = Positive(match?.MandatoryDemandPerTurn ?? 0);
            target.IncomeGap = Mathf.Max(Positive(match?.IncomeGap ?? 0), target.PlannedDemand / Mathf.Clamp(AIEconomySettings.Active.forecastTurns, 1, 8) - target.IncomePerTurn);
        }
        UpdateState(turn, essentialDefense);
    }

    void UpdateState(int turn, bool essentialDefense)
    {
        var policy = AIBasicResourceSettings.Active;
        bool advanced = turn != lastTurn;
        // A new match may reuse its commander when domain reload is disabled.
        if (lastTurn > turn) { Array.Clear(critical, 0, critical.Length); Array.Clear(blockedTurns, 0, blockedTurns.Length); stableTurnCount = 0; recoveryStarted = -1; FoundationState = EconomyFoundationState.OpeningFoundation; PrimaryBottleneck = ResourceKind.None; }
        bool allStable = true, anyCritical = false;
        float strongest = 0; ResourceKind strongestResource = ResourceKind.None;
        float enter = Mathf.Clamp(Finite(policy.criticalCoverage), .05f, 1f);
        float exit = Mathf.Max(enter + .05f, Positive(policy.recoveryCoverage));
        for (int i = 0; i < snapshots.Length; i++)
        {
            var s = snapshots[i];
            s.ProjectedAvailable = Mathf.Max(0, s.CurrentStock + s.ExpectedIncome);
            s.ProjectedDemand = s.PlannedDemand + s.ReserveTarget;
            s.Coverage = s.ProjectedDemand > .001f ? s.ProjectedAvailable / s.ProjectedDemand : 2f;
            s.Deficit = Mathf.Max(0, s.ProjectedDemand - s.ProjectedAvailable);
            if (advanced) blockedTurns[i] = s.BlockedPlanValue > .001f ? Mathf.Min(8, blockedTurns[i] + 1) : 0;
            bool committedDemand = IsOpeningResource(s.ResourceType) || s.PlannedDemand > .001f || s.MandatoryDemandPerTurn > .001f || s.BlockedPlanValue > .001f;
            bool noSource = s.ProducerCount == 0 && s.IncomeGap > .001f && committedDemand;
            bool enterCritical = s.Coverage < enter && committedDemand || noSource && s.CurrentStock < s.ReserveTarget
                || s.BlockedPlanValue >= 1.5f && s.IncomeGap > .001f;
            critical[i] = enterCritical || critical[i] && (s.Coverage < exit || s.BlockedPlanValue > .001f);
            bool shortage = s.Coverage < exit || s.IncomeGap > .001f;
            bool surplus = s.Coverage >= Mathf.Max(exit, Positive(policy.surplusCoverage)) && !shortage && s.BlockedPlanValue <= .001f;
            s.State = critical[i] ? BasicResourceState.Critical : shortage ? BasicResourceState.Shortage : surplus ? BasicResourceState.Surplus : BasicResourceState.Stable;
            float normalizedDeficit = s.Deficit / Mathf.Max(1, s.ProjectedDemand);
            float normalizedIncomeGap = Mathf.Clamp01(s.IncomeGap / Mathf.Max(1, s.IncomeGap + Mathf.Max(0, s.IncomePerTurn)));
            float blocked = Mathf.Min(3, s.BlockedPlanValue) * (1 + Mathf.Min(5, blockedTurns[i]) * .15f);
            float opening = FoundationState == EconomyFoundationState.OpeningFoundation && shortage && IsOpeningResource(s.ResourceType) ? 1f : 0;
            float surplusScore = surplus ? Mathf.Clamp01(s.Coverage / Mathf.Max(1, policy.surplusCoverage) - 1) : 0;
            s.PressureScore = Mathf.Max(0, Positive(policy.deficitWeight) * normalizedDeficit + Positive(policy.blockedPlanWeight) * blocked
                + Positive(policy.incomeGapWeight) * normalizedIncomeGap + Positive(policy.zeroProducerWeight) * (noSource ? 1 : 0)
                + Positive(policy.openingWeight) * opening - Positive(policy.surplusPenaltyWeight) * surplusScore);
            allStable &= !critical[i] && !shortage && s.BlockedPlanValue <= .001f
                && s.Coverage >= Mathf.Max(exit, Positive(policy.stableCoverage))
                && (s.ProducerCount > 0 || s.IncomePerTurn > .001f || !committedDemand || s.ProjectedDemand <= .001f);
            anyCritical |= critical[i];
            if (s.PressureScore > strongest) { strongest = s.PressureScore; strongestResource = resources[i]; }
        }
        if (advanced) stableTurnCount = allStable ? stableTurnCount + 1 : 0;
        bool held = recoveryStarted >= 0 && turn - recoveryStarted < Mathf.Clamp(policy.minimumRecoveryTurns, 1, 8);
        float currentPressure = Get(PrimaryBottleneck)?.PressureScore ?? 0;
        bool moreSevere = Get(strongestResource)?.State == BasicResourceState.Critical && Get(PrimaryBottleneck)?.State != BasicResourceState.Critical;
        if (PrimaryBottleneck == ResourceKind.None || currentPressure <= .001f || !held && strongest > currentPressure * Mathf.Max(1, policy.switchPressureRatio) || moreSevere)
            PrimaryBottleneck = strongestResource;
        if (anyCritical && FoundationState != EconomyFoundationState.ResourceRecovery)
        { FoundationState = EconomyFoundationState.ResourceRecovery; recoveryStarted = turn; stableTurnCount = 0; }
        else if (FoundationState == EconomyFoundationState.StableGrowth && HasBlockedPlans)
        { FoundationState = EconomyFoundationState.ResourceRecovery; recoveryStarted = turn; stableTurnCount = 0; }
        else if (allStable && stableTurnCount >= Mathf.Clamp(policy.stableTurns, 1, 8) && !held)
        { FoundationState = EconomyFoundationState.StableGrowth; PrimaryBottleneck = ResourceKind.None; }
        lastTurn = turn;
    }

    void CollectPlans()
    {
        plans.Clear(); plannedSources.Clear(); board.CollectOwnBuildings(buildings);
        // Consider only useful economic projects, using their real authored/legacy costs.
        foreach (var resource in resources)
        {
            var current = demand.Get(resource);
            float desired = current.MandatoryDemandPerTurn + current.PlannedDemandPerTurn;
            float reserve = Mathf.Max(current.SafetyReserve, StrategicEconomyForecast.Amount(AIBasicResourceSettings.Active.minimumReserve, (int)resource));
            if (!IsOpeningResource(resource) && desired <= .001f && current.Stock >= reserve) continue;
            if (resource == ResourceKind.Citizen && CanFeedCitizenGrowth() && current.Stock < board.CitizenCapacity) continue;
            if (demand.PotentialSourceCount(resource) > 0 && demand.PotentialProduction(resource) >= desired + .001f && current.Stock >= reserve) continue;
            FacilityKind chosenKind = default; FacilityDefinitionData chosenDefinition = null; Status chosenActor = null;
            FacilityData.ResourceCost chosenCost = default; float best = float.PositiveInfinity;
            if (board.BuildablePositions.Count > 0) foreach (var entry in FacilityData.Table)
                ConsiderPlan(resource, entry.Key, null, null, entry.Value.BuildCost, FacilityData.GetLevel(entry.Key, 1), default,
                    ref best, ref chosenKind, ref chosenDefinition, ref chosenActor, ref chosenCost);
            var catalog = FacilityAuthoringCatalog.Loaded;
            if (board.BuildablePositions.Count > 0 && catalog?.buildings != null) foreach (var definition in catalog.buildings)
                if (definition != null && definition.IsAvailable(board.ActorTeam) && catalog.Find(definition.definitionId) == definition)
                    ConsiderPlan(resource, definition.behaviourKind, definition, null, definition.GetInfo().BuildCost, definition.GetLevel(1), default,
                        ref best, ref chosenKind, ref chosenDefinition, ref chosenActor, ref chosenCost);
            foreach (var actor in buildings)
            {
                if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam) continue;
                int maximum = actor.AuthoredFacility != null ? actor.AuthoredFacility.GetInfo().MaxLevel : FacilityData.GetMaxLevel(actor.facilityKind);
                int currentLevel = Mathf.Max(1, actor.Level);
                if (currentLevel >= maximum) continue;
                var next = FacilityData.GetLevel(actor, currentLevel + 1);
                ConsiderPlan(resource, actor.facilityKind, actor.AuthoredFacility, actor, next.UpgradeCost, next, FacilityData.GetLevel(actor, currentLevel),
                    ref best, ref chosenKind, ref chosenDefinition, ref chosenActor, ref chosenCost);
            }
            if (!float.IsInfinity(best) && plannedSources.Add((chosenKind, chosenDefinition, chosenActor))) plans.Add(new BasicResourcePlan(chosenCost, 1.5f));
        }
        // A single future unit is enough to represent development; never reserve an imagined army.
        var unit = board.ResolveUnitDefinition(board.AlivePlayerUnits.Count == 0 ? Kind.Scout : Kind.Knight);
        if (unit != null && !emergency && FoundationState != EconomyFoundationState.ResourceRecovery)
            plans.Add(new BasicResourcePlan(UnitCost(unit), .75f, .75f));
    }

    void FindFeasibleSupplyAP()
    {
        FeasibleSupplyAP = 0;
        if (!AIBasicResourceSettings.Active.enabled || board == null || demand == null) return;
        var candidate = new AIAction();
        if (board.BuildablePositions.Count > 0)
        {
            foreach (var kind in board.AffordableBuildings)
            {
                if (!FacilityData.Table.TryGetValue(kind, out var info)) continue;
                candidate.ActionType = AIActionType.Build; candidate.Facility = kind; candidate.FacilityDefinition = null; candidate.Unit = null; candidate.TargetUnit = null;
                ConsiderFeasibleSupply(candidate, info.APCost);
            }
            var catalog = FacilityAuthoringCatalog.Loaded;
            if (catalog?.buildings != null) foreach (var definition in catalog.buildings)
            {
                if (definition == null || !definition.IsAvailable(board.ActorTeam) || catalog.Find(definition.definitionId) != definition || !board.CanBuildDefinition(definition)) continue;
                candidate.ActionType = AIActionType.Build; candidate.Unit = null; candidate.TargetUnit = null;
                candidate.Facility = definition.behaviourKind; candidate.FacilityDefinition = definition;
                ConsiderFeasibleSupply(candidate, definition.GetInfo().APCost);
            }
        }
        foreach (var actor in buildings)
        {
            if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam) continue;
            candidate.ActionType = AIActionType.Upgrade; candidate.Unit = actor; candidate.TargetUnit = actor;
            candidate.Facility = actor.facilityKind; candidate.FacilityDefinition = actor.AuthoredFacility;
            if (TryRecipe(candidate, out var next, out _, out _)) ConsiderFeasibleSupply(candidate, next.UpgradeAP);
        }
    }
    void ConsiderFeasibleSupply(AIAction action, int ap)
    {
        if (ap < 0 || ap > board.EnemyAP || ActionPriority(action) > 2 || !AllowSpend(action, false, out _)) return;
        var cost = ActionCost(action);
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
            if (StrategicEconomyForecast.Amount(cost, i) > demand.Resources[i].Stock) return;
        if (OperatingFractionFor(action, cost) <= .001f) return;
        if (FeasibleSupplyAP == 0 || ap < FeasibleSupplyAP) FeasibleSupplyAP = ap;
    }

    void ConsiderPlan(ResourceKind resource, FacilityKind kind, FacilityDefinitionData definition, Status actor,
        FacilityData.ResourceCost cost, FacilityData.FacilityLevelData recipe, FacilityData.FacilityLevelData previous,
        ref float best, ref FacilityKind chosenKind, ref FacilityDefinitionData chosenDefinition, ref Status chosenActor, ref FacilityData.ResourceCost chosenCost)
    {
        float gain = ProductionGain(kind, recipe, previous, resource);
        if (gain <= .001f) return;
        var costBundle = ToBundle(cost); float investment = 0;
        foreach (var basic in resources) investment += StrategicEconomyForecast.Amount(costBundle, (int)basic);
        float score = investment / gain;
        if (score >= best) return;
        best = score; chosenKind = kind; chosenDefinition = definition; chosenActor = actor; chosenCost = cost;
    }

    public bool IsFoundationAction(AIAction action)
    {
        if (!AIBasicResourceSettings.Active.enabled || !TryRecipe(action, out var next, out var previous, out _)) return false;
        var kind = RecipeKind(action);
        foreach (var s in snapshots)
            if (s.PressureScore > .001f && ProductionGain(kind, next, previous, s.ResourceType) > .001f) return true;
        return false;
    }

    public int ActionPriority(AIAction action)
    {
        if (!IsFoundationAction(action)) return 4;
        foreach (var s in snapshots)
            if (s.State == BasicResourceState.Critical && RecipeGain(action, s.ResourceType) > .001f) return 1;
        return FoundationState == EconomyFoundationState.StableGrowth ? 3 : 2;
    }

    public float ScoreAction(AIAction action)
    {
        if (action == null || !AIBasicResourceSettings.Active.enabled) return 0;
        var actor = action.ActionType == AIActionType.Upgrade ? action.Unit ?? action.TargetUnit : null;
        var key = (action.ActionType, action.Facility, action.FacilityDefinition, actor, action.SummonKind, action.SummonDefinition);
        if (scores.TryGetValue(key, out float cached)) return cached;
        var cost = ActionCost(action); var policy = AIBasicResourceSettings.Active;
        float score = 0, gainTotal = 0, investment = 0;
        bool recipe = TryRecipe(action, out var next, out var previous, out _);
        float operation = recipe ? OperatingFraction(next, previous, cost) : 0;
        foreach (var s in snapshots)
        {
            int index = (int)s.ResourceType;
            float gain = recipe ? ProductionGain(RecipeKind(action), next, previous, s.ResourceType) * operation : 0;
            float spent = StrategicEconomyForecast.Amount(cost, index); investment += spent;
            if (gain > .001f)
            {
                gainTotal += gain;
                float required = Mathf.Max(1, s.IncomeGap);
                score += s.PressureScore * Mathf.Clamp(gain / required, 0, 1.5f);
                if (s.State == BasicResourceState.Surplus) score -= Positive(policy.surplusPenaltyWeight);
            }
            float reserveLoss = Mathf.Clamp01((s.ReserveTarget - Mathf.Max(0, s.CurrentStock - spent)) / Mathf.Max(1, s.ReserveTarget));
            score -= Positive(policy.constructionPressureWeight) * reserveLoss * Mathf.Clamp01(spent / Mathf.Max(1, s.CurrentStock));
            if (gain <= .001f && spent > 0 && s.State <= BasicResourceState.Shortage)
                score -= Positive(policy.recoverySpendPenalty) * Mathf.Clamp01(spent / Mathf.Max(1, s.CurrentStock));
        }
        if (gainTotal > .001f)
        {
            float payback = investment / gainTotal;
            score *= Mathf.Clamp(Positive(policy.paybackWindowTurns) / Mathf.Max(1, payback), .15f, 1);
        }
        scores[key] = score; return score;
    }

    public bool AllowSpend(AIAction action, bool essentialDefense, out string reason)
    {
        reason = null;
        if (action == null || essentialDefense || !AIBasicResourceSettings.Active.enabled) return true;
        var cost = ActionCost(action); bool urgentRecovery = IsUrgentRecoveryInvestment(action);
        bool foundation = IsFoundationAction(action) || urgentRecovery;
        foreach (var s in snapshots)
        {
            float spent = StrategicEconomyForecast.Amount(cost, (int)s.ResourceType);
            if (spent <= 0) continue;
            float remaining = s.CurrentStock - spent;
            if (remaining < 0) { reason = "basic_resource_unaffordable_" + s.ResourceType; return false; }
            float floor = s.ReserveTarget;
            if (foundation)
            {
                // A real supply improvement may invest its reserve. Never promise inputs that remain unavailable.
                if (OperatingFractionFor(action, cost) <= .001f) { reason = "basic_resource_supply_unavailable"; return false; }
                floor *= urgentRecovery ? 0 : Mathf.Clamp01(AIBasicResourceSettings.Active.foundationReserveFraction);
                if (RecipeGain(action, s.ResourceType) > .001f) floor = 0;
                remaining += Mathf.Max(0, s.ExpectedIncome);
            }
            if (remaining + .001f < floor)
            { reason = "basic_resource_reserve_" + s.ResourceType; return false; }
        }
        return true;
    }

    bool IsUrgentRecoveryInvestment(AIAction action)
    {
        if (demand == null || !TryRecipe(action, out var next, out var previous, out _)) return false;
        if (action.ActionType == AIActionType.Build && demand.EvaluateBuild(action).ImprovesCritical) return true;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
            if (demand.Resources[i].Urgency01 >= .999f && demand.Resources[i].ProductionDeficit > .001f
                && NetOutput(next, i) - NetOutput(previous, i) > .001f) return true;
        foreach (var s in snapshots)
            if (s.State == BasicResourceState.Critical && ProductionGain(RecipeKind(action), next, previous, s.ResourceType) > .001f) return true;
        return false;
    }

    public void NotifyEssentialSpend(AIAction action, bool alreadySpent = false)
    {
        var cost = ActionCost(action);
        foreach (var s in snapshots)
            if (StrategicEconomyForecast.Amount(cost, (int)s.ResourceType) > 0
                && s.CurrentStock - (alreadySpent ? 0 : StrategicEconomyForecast.Amount(cost, (int)s.ResourceType)) < s.ReserveTarget)
            { FoundationState = EconomyFoundationState.ResourceRecovery; recoveryStarted = lastTurn; stableTurnCount = 0; return; }
    }

    float OperatingFractionFor(AIAction action, FacilityData.ProductionBundle cost)
        => TryRecipe(action, out var next, out var previous, out _) ? OperatingFraction(next, previous, cost) : 0;
    float OperatingFraction(FacilityData.FacilityLevelData next, FacilityData.FacilityLevelData previous, FacilityData.ProductionBundle cost)
    {
        if (demand == null) return 1;
        float operation = 1; int horizon = Mathf.Clamp(AIEconomySettings.Active.forecastTurns, 1, 8);
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            float additional = StrategicEconomyForecast.Amount(next.Input, i) / (float)FacilityData.ProductionInterval(next)
                + StrategicEconomyForecast.Amount(next.Maintenance, i)
                - StrategicEconomyForecast.Amount(previous.Input, i) / (float)FacilityData.ProductionInterval(previous)
                - StrategicEconomyForecast.Amount(previous.Maintenance, i);
            if (additional <= .001f) continue;
            var source = demand.Resources[i];
            operation = Mathf.Min(operation, Mathf.Max(0, (source.Stock - StrategicEconomyForecast.Amount(cost, i)) / horizon + source.NetPerTurn) / additional);
        }
        return Mathf.Clamp01(operation);
    }

    float RecipeGain(AIAction action, ResourceKind resource)
        => TryRecipe(action, out var next, out var previous, out _) ? ProductionGain(RecipeKind(action), next, previous, resource) : 0;
    static FacilityKind RecipeKind(AIAction action)
    {
        var actor = action?.ActionType == AIActionType.Upgrade ? action.Unit ?? action.TargetUnit : null;
        return actor != null ? actor.facilityKind : action?.FacilityDefinition != null ? action.FacilityDefinition.behaviourKind : action?.Facility ?? default;
    }
    float ProductionGain(FacilityKind kind, FacilityData.FacilityLevelData next, FacilityData.FacilityLevelData previous, ResourceKind resource)
    {
        float gain = ExpectedNetOutput(next, (int)resource) - ExpectedNetOutput(previous, (int)resource);
        if (resource == ResourceKind.Citizen && (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse)
            && board != null && demand != null && demand.Get(ResourceKind.Citizen).Stock >= board.CitizenCapacity
            && next.SpecialValue > previous.SpecialValue && CanFeedCitizenGrowth())
            gain += Mathf.Min(1, next.SpecialValue - previous.SpecialValue);
        return gain;
    }
    void RefreshCitizenSupply(int horizon, IReadOnlyList<BasicResourcePlan> selectedPlans)
    {
        var citizen = Get(ResourceKind.Citizen);
        if (!CanFeedCitizenGrowth()) return;
        float population = demand.Get(ResourceKind.Citizen).Stock;
        float growthSpace = Mathf.Max(0, board.CitizenCapacity - population);
        float reachableSpending = 0;
        foreach (var plan in selectedPlans)
            if (plan.Cost.Citizen <= population) reachableSpending += Mathf.Max(0, plan.Cost.Citizen);
        float growthRate = Mathf.Min(1, (growthSpace + reachableSpending) / horizon);
        float explicitProduction = Mathf.Max(0, demand.PotentialProduction(ResourceKind.Citizen) - demand.NaturalCitizenGrowthPerTurn);
        citizen.PotentialProductionPerTurn = explicitProduction + Mathf.Max(demand.NaturalCitizenGrowthPerTurn, growthRate);
        citizen.ProducerCount = Mathf.Max(citizen.ProducerCount, growthSpace > 0 || reachableSpending > 0 ? 1 : 0);
        citizen.IncomePerTurn = citizen.PotentialProductionPerTurn - citizen.MandatoryDemandPerTurn;
        // Recurring growth after a planned purchase is useful income, but the currently held
        // population still respects its existing capacity. Authored direct population output remains distinct.
        citizen.ExpectedIncome = explicitProduction * horizon + Mathf.Min(growthSpace, Mathf.Max(demand.NaturalCitizenGrowthPerTurn, growthRate) * horizon)
            - citizen.MandatoryDemandPerTurn * horizon;
        float required = citizen.PlannedDemand / horizon + Mathf.Max(0, citizen.ReserveTarget - citizen.CurrentStock) / Mathf.Max(1, AIEconomySettings.Active.recoveryWindowTurns);
        citizen.IncomeGap = Mathf.Max(0, required - citizen.IncomePerTurn);
    }
    bool CanFeedCitizenGrowth()
    {
        if (demand == null) return false;
        var food = demand.Get(ResourceKind.Bread);
        int horizon = Mathf.Clamp(AIEconomySettings.Active.forecastTurns, 1, 8);
        return food.Stock / horizon + food.NetPerTurn >= 1 + EconomySystem.CitizenBreadCost;
    }
    static bool IsOpeningResource(ResourceKind resource)
        => resource == ResourceKind.Wood || resource == ResourceKind.Stone || resource == ResourceKind.Water;
    internal static float NetOutput(FacilityData.FacilityLevelData recipe, int resource)
        => (StrategicEconomyForecast.GuaranteedOutput(recipe, resource) - StrategicEconomyForecast.Amount(recipe.Input, resource))
            / FacilityData.ProductionInterval(recipe) - StrategicEconomyForecast.Amount(recipe.Maintenance, resource);
    internal static float ExpectedNetOutput(FacilityData.FacilityLevelData recipe, int resource)
        => (StrategicEconomyForecast.Amount(recipe.Output, resource)
            + StrategicEconomyForecast.Amount(recipe.BonusOutput1, resource) * Mathf.Clamp01(recipe.BonusChance1)
            + StrategicEconomyForecast.Amount(recipe.BonusOutput2, resource) * Mathf.Clamp01(recipe.BonusChance2)
            - StrategicEconomyForecast.Amount(recipe.Input, resource)) / FacilityData.ProductionInterval(recipe)
            - StrategicEconomyForecast.Amount(recipe.Maintenance, resource);
    public static bool TryRecipe(AIAction action, out FacilityData.FacilityLevelData next, out FacilityData.FacilityLevelData previous, out FacilityData.ResourceCost cost)
    {
        next = previous = default; cost = default;
        if (action?.ActionType == AIActionType.Build)
        {
            if (action.FacilityDefinition != null) { next = action.FacilityDefinition.GetLevel(1); cost = action.FacilityDefinition.GetInfo().BuildCost; }
            else if (FacilityData.Table.TryGetValue(action.Facility, out var info)) { next = FacilityData.GetLevel(action.Facility, 1); cost = info.BuildCost; }
            else return false;
            return true;
        }
        if (action?.ActionType != AIActionType.Upgrade) return false;
        var actor = action.Unit ?? action.TargetUnit;
        if (actor == null || actor.type != Type.Building && actor.type != Type.Wall || !actor.IsAlive) return false;
        int maximum = actor.AuthoredFacility != null ? actor.AuthoredFacility.GetInfo().MaxLevel : FacilityData.GetMaxLevel(actor.facilityKind);
        int currentLevel = Mathf.Max(1, actor.Level);
        if (currentLevel >= maximum) return false;
        previous = FacilityData.GetLevel(actor, currentLevel); next = FacilityData.GetLevel(actor, currentLevel + 1); cost = next.UpgradeCost; return true;
    }
    FacilityData.ProductionBundle ActionCost(AIAction action)
    {
        if (TryRecipe(action, out _, out _, out var cost)) return ToBundle(cost);
        if (action?.ActionType == AIActionType.SubCrystal && FacilityData.Table.TryGetValue(FacilityKind.SubCrystal, out var info)) return ToBundle(info.BuildCost);
        if (action?.ActionType != AIActionType.Summon) return default;
        var unit = action.SummonDefinition ?? board?.ResolveUnitDefinition(action.SummonKind);
        return unit != null ? UnitCost(unit) : default;
    }
    internal static FacilityData.ProductionBundle ToBundle(FacilityData.ResourceCost cost)
        => new FacilityData.ProductionBundle { Wood = cost.Wood, Stone = cost.Stone, Iron = cost.Iron, MagicOre = cost.MagicOre, Water = cost.Water, Citizen = cost.Citizen };
    internal static FacilityData.ProductionBundle UnitCost(UnitData unit)
        => new FacilityData.ProductionBundle { Wood = unit.costWood, Stone = unit.costStone, Iron = unit.costIron, MagicOre = unit.costMagic, Bread = unit.costBread, Water = unit.costWater, Citizen = unit.costCitizen };
    static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
    static float Positive(float value) => Mathf.Max(0, Finite(value));
}
