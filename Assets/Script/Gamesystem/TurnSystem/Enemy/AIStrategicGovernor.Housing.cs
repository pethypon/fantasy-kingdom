using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    // Owner: existing governor; lifetime: one board generation. Maximum: available housing definitions/upgrades.
    // Prune/clear: generation or operation demand changes. No turn-history or independent action driver is added.
    readonly Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, int), AIHousingAssessment> housingAssessments
        = new Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, int), AIHousingAssessment>();
    readonly List<UnitData> housingRecruits = new List<UnitData>(32);
    AIBoardState housingBoard;
    int housingGeneration = -1, housingOperationDemand = -1;
    int housingConfigRevision = -1;
    int housingContentRevision = -1;
    int housingPlanRevision = -1;
    public int HousingRecoveryStartedTurn { get; private set; } = -1;
    public int HousingRecoveryLastProgressTurn { get; private set; } = -1;
    public int HousingRecoveryDeferredTurns { get; private set; }
    public string HousingRecoveryStatus { get; private set; } = "housing_not_deferred";
    float recoveryBreadStock, recoveryBreadIncome, recoveryWheatIncome, recoveryWaterIncome;

    public AIHousingAssessment HousingAssessment(AIAction action, AIBoardState board)
    {
        var config = AIHousingConfig.Active;
        if (!config.Enabled || action == null || board == null
            || action.ActionType != AIActionType.Build && action.ActionType != AIActionType.Upgrade) return default;
        var kind = ConstructionKind(action);
        if (kind != FacilityKind.House && kind != FacilityKind.LuxuryHouse) return default;
        Evaluate(board);
        int operationDemand = Mathf.Clamp(board.OperationRequiredCitizenDemand, 0, 100000);
        if (housingBoard != board || housingGeneration != board.Generation || housingOperationDemand != operationDemand
            || housingConfigRevision != config.Revision || housingContentRevision != GameContentRevision.Revision
            || housingPlanRevision != BasicResources.PlanRevision)
        {
            housingBoard = board; housingGeneration = board.Generation; housingOperationDemand = operationDemand;
            housingConfigRevision = config.Revision;
            housingContentRevision = GameContentRevision.Revision;
            housingPlanRevision = BasicResources.PlanRevision;
            housingAssessments.Clear(); board.CollectRecruitDefinitions(housingRecruits);
        }
        var actor = action.ActionType == AIActionType.Upgrade ? action.Unit ?? action.TargetUnit : null;
        var key = (action.ActionType, kind, action.FacilityDefinition, actor, actor != null ? actor.Level : 0);
        if (housingAssessments.TryGetValue(key, out var cached)) return cached;
        var result = AssessHousing(action, board, config, operationDemand);
        housingAssessments[key] = result;
        if (config.EnableDecisionLogs && DevelopmentLog.IsEnabled(AILogLevel.Verbose))
            DevelopmentLog.Log("[AI住宅投資] " + (result.Needed ? "採用候補 " : "却下 ") + result.Describe());
        return result;
    }

    AIHousingAssessment AssessHousing(AIAction action, AIBoardState board, AIHousingConfig config, int operationDemand)
    {
        var actor = action.ActionType == AIActionType.Upgrade ? action.Unit ?? action.TargetUnit : null;
        var recipe = actor != null ? FacilityData.GetLevel(actor, actor.Level + 1)
            : action.FacilityDefinition != null ? action.FacilityDefinition.GetLevel(1) : FacilityData.GetLevel(action.Facility, 1);
        int gain = Mathf.Max(0, recipe.SpecialValue - (actor != null ? FacilityData.GetLevel(actor, actor.Level).SpecialValue : 0));
        var cost = actor != null ? recipe.UpgradeCost : action.FacilityDefinition != null ? action.FacilityDefinition.GetInfo().BuildCost
            : FacilityData.Table.TryGetValue(action.Facility, out var info) ? info.BuildCost : default;
        int capacity = Mathf.Max(1, forecast.PreparedCitizenCapacity);
        int free = Mathf.Clamp(board.EnemyResources?.Citizen ?? 0, 0, capacity);
        // Citizen is spendable available stock. This ratio is used slots, not persistent population.
        float used = Mathf.Clamp01((capacity - free) / (float)capacity);
        float plannedBuilding = Mathf.Max(ProductionDemand.PlannedBuildingCitizens, BasicResources.PlannedBuildingCitizens);
        float plannedRecruit = Mathf.Max(ProductionDemand.PlannedRecruitCitizens, BasicResources.PlannedRecruitCitizens);
        float otherPlan = BasicResources.PlannedUnclassifiedCitizens;
        float planned = Mathf.Max(plannedBuilding + plannedRecruit, otherPlan);
        UnitData defender = CheapestCapacityBlockedDefender(board, action, capacity, gain, cost, config, out string defenseReject);
        bool emergency = Mode == StrategicMode.EmergencyDefense && defender != null;
        float military = 0;
        if (board.AlivePlayerUnits.Count > 0)
        {
            int cheapest = int.MaxValue;
            foreach (var unit in housingRecruits)
                if (CombatRecruit(unit) && unit.costCitizen > 0) cheapest = Mathf.Min(cheapest, unit.costCitizen);
            if (cheapest != int.MaxValue) military = cheapest * Mathf.Clamp(config.ShortTermCombatRecruits, 0, 8);
        }
        // Operation needs and ordinary short-term recruitment refer to the same army, not two armies.
        float recruitCapacityStock = HasPreparedRecruitment(board) ? RecruitmentRequiredCitizenStock : plannedRecruit;
        float futureArmy = Mathf.Max(Mathf.Max(operationDemand, military), recruitCapacityStock);
        float required = Mathf.Max(plannedBuilding + Mathf.Max(recruitCapacityStock, futureArmy), otherPlan) + config.FreeTarget;
        if (emergency) required = Mathf.Max(required, defender.costCitizen);
        bool blocked = required > capacity + .001f;
        string reason = null;
        if (gain <= 0) reason = "housing_no_capacity_gain";
        else if (!blocked) reason = free < required ? "housing_existing_growth_can_recover_stock" : "housing_capacity_sufficient";
        else if (Economy.BreadPaymentFailed || Economy.UpkeepPaymentFailed || Economy.ImportantProductionStopped
            || Economy.State >= EconomicState.Crisis || MaterialSupplyNeedsRecovery()
            || BasicResources.HasRequiredFoodSupplyGap) reason = "housing_supply_recovery_first";
        // A normal recruitment forecast must not hide the precise emergency rejection
        // (cannot wait / cannot maintain this defender / direct defense already available).
        if (reason == null && Mode == StrategicMode.EmergencyDefense && !emergency)
            reason = defenseReject ?? "housing_direct_defense_first";
        if (reason == null)
        {
            var projected = forecast.SimulateInvestment(board, action, config.Horizon);
            if (projected.BreadPaymentFailed || projected.UpkeepPaymentFailed || projected.ImportantProductionStopped
                || projected.BreadCoverageTurns < BreadReserveTurns) reason = "housing_build_forecast_unsafe";
            if (reason == null && (RecruitmentCitizenDemand > 0 || DevelopmentPlan.Count > 0))
            {
                var recruitment = ForecastRecruitment(board, action);
                if (recruitment.Reason != null && recruitment.Reason != "recruit_citizen_growth_pending"
                    && recruitment.Reason != "recruit_capacity_shortage" && recruitment.Reason != "recruit_resources_pending")
                    reason = "housing_build_forecast_unsafe";
            }
        }
        TrackHousingRecovery(board, blocked, gain, reason, config);
        float need = Mathf.InverseLerp(config.ConsiderRatio, Mathf.Max(config.ConsiderRatio + .001f, config.UrgentRatio), used);
        // Reward the fraction of the actual shortage this investment resolves, not tiny houses with a huge remaining gap.
        float future = Mathf.Clamp01(gain / Mathf.Max(1, required - capacity));
        float opportunity = MaterialReservePressure(board, cost);
        float score = need * AIHousingConfig.Weight(config.PopulationNeedWeight)
            + future * AIHousingConfig.Weight(config.FuturePopulationNeedWeight)
            + AIHousingConfig.Weight(config.EconomicStabilityWeight)
            + (futureArmy > 0 ? AIHousingConfig.Weight(config.MilitaryExpansionWeight) : 0)
            - opportunity * AIHousingConfig.Weight(config.ResourceRiskPenalty);
        if (!blocked) score -= AIHousingConfig.Weight(config.ExcessHousingPenalty);
        if (Mode == StrategicMode.EmergencyDefense && !emergency) score -= AIHousingConfig.Weight(config.EmergencyPenalty);
        // Current capacity already includes every existing house: reevaluation prevents stale same-turn repeats.
        float expandedCapacity = Mathf.Min(int.MaxValue, (float)capacity + gain);
        float excessGain = Mathf.Clamp01((expandedCapacity - required) / Mathf.Max(1, expandedCapacity));
        score -= excessGain * AIHousingConfig.Weight(config.RepeatedHousePenalty);
        if (reason != null) score = -Mathf.Max(AIHousingConfig.Weight(config.ExcessHousingPenalty), Mathf.Abs(score));
        return new AIHousingAssessment(reason == null, reason == null && emergency, blocked, free, capacity, gain,
            used, planned, plannedBuilding, plannedRecruit, otherPlan, operationDemand, military, required, score,
            reason ?? (emergency ? "housing_defense_capacity_blocked" : "housing_planned_capacity_shortfall"));
    }

    void TrackHousingRecovery(AIBoardState board, bool blocked, int gain, string reason, AIHousingConfig config)
    {
        if (!blocked)
        {
            HousingRecoveryStartedTurn = HousingRecoveryLastProgressTurn = -1;
            HousingRecoveryDeferredTurns = 0; HousingRecoveryStatus = "housing_capacity_sufficient"; return;
        }
        if (gain <= 0) return;
        if (reason != "housing_supply_recovery_first")
        {
            if (reason == null && HousingRecoveryStartedTurn >= 0) HousingRecoveryStatus = "housing_recovery_resumed";
            return;
        }
        float stock = ProductionDemand.Get(ResourceKind.Bread).Stock;
        float bread = ProductionDemand.PermanentProduction(ResourceKind.Bread);
        float wheat = ProductionDemand.PermanentProduction(ResourceKind.Wheat);
        float water = ProductionDemand.PermanentProduction(ResourceKind.Water);
        if (HousingRecoveryStartedTurn < 0 || board.TurnCount < HousingRecoveryStartedTurn)
            HousingRecoveryStartedTurn = HousingRecoveryLastProgressTurn = board.TurnCount;
        if (stock > recoveryBreadStock || bread > recoveryBreadIncome || wheat > recoveryWheatIncome || water > recoveryWaterIncome)
            HousingRecoveryLastProgressTurn = board.TurnCount;
        recoveryBreadStock = stock; recoveryBreadIncome = bread; recoveryWheatIncome = wheat; recoveryWaterIncome = water;
        HousingRecoveryDeferredTurns = Mathf.Max(0, board.TurnCount - HousingRecoveryStartedTurn);
        // Expiry is an explicit diagnostic, never permission to build an unsafe house.
        HousingRecoveryStatus = board.TurnCount - HousingRecoveryLastProgressTurn >= config.Horizon
            ? "housing_supply_recovery_stalled" : "housing_supply_recovery_in_progress";
    }

    bool MaterialSupplyNeedsRecovery()
    {
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            if (i == (int)ResourceKind.Citizen) continue;
            var need = ProductionDemand.Get((ResourceKind)i);
            if (need.MandatoryDemandPerTurn > .001f && need.NetPerTurn < -.001f && need.ProjectedStock < need.SafetyReserve) return true;
            if (AIBasicResourceSettings.Active.enabled && BasicResources.Get((ResourceKind)i)?.State == BasicResourceState.Critical) return true;
        }
        return false;
    }
    float MaterialReservePressure(AIBoardState board, FacilityData.ResourceCost cost)
    {
        var bundle = StrategicEconomyForecast.BuildCost(cost); float pressure = 0;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            if (i == (int)ResourceKind.Citizen) continue;
            var need = ProductionDemand.Get((ResourceKind)i); float spent = StrategicEconomyForecast.Amount(bundle, i);
            if (spent <= 0) continue;
            pressure = Mathf.Max(pressure, Mathf.Clamp01((need.SafetyReserve - (need.Stock - spent)) / Mathf.Max(1, need.SafetyReserve)));
        }
        return pressure;
    }
    static bool CombatRecruit(UnitData unit) => unit != null && unit.IsValidForAuthoring && unit.kind != Kind.Scout && unit.kind != Kind.Priest
        && unit.kind != Kind.King && unit.kind != Kind.Boss && unit.kind != Kind.Crystal && unit.kind != Kind.SubCrystal
        && unit.actionProfile?.CanAttack != false;
    UnitData CheapestCapacityBlockedDefender(AIBoardState board, AIAction investment, int capacity, int capacityGain,
        FacilityData.ResourceCost houseCost, AIHousingConfig config, out string rejectReason)
    {
        rejectReason = null;
        if (Mode != StrategicMode.EmergencyDefense || board.AlivePlayerUnits.Count == 0) return null;
        int survivalTurns = config.EmergencyWait;
        foreach (var target in critical)
        {
            float incoming = IncomingDamage(board, target, target.transform.position, null);
            if (IsSevereThreat(target, incoming))
                survivalTurns = Mathf.Min(survivalTurns, Mathf.FloorToInt((target.HP - 1) / Mathf.Max(1, incoming)));
        }
        UnitData best = null;
        foreach (var unit in housingRecruits)
        {
            if (!CombatRecruit(unit) || unit.costCitizen <= capacity || unit.costCitizen > (long)capacity + capacityGain
                || best != null && unit.costCitizen >= best.costCitizen) continue;
            var resources = board.EnemyResources;
            if (resources == null || unit.costCitizen > (long)resources.Citizen - houseCost.Citizen + config.EmergencyWait
                || unit.costWood > resources.Wood - houseCost.Wood || unit.costStone > resources.Stone - houseCost.Stone
                || unit.costIron > resources.Iron - houseCost.Iron || unit.costMagic > resources.MagicOre - houseCost.MagicOre
                || unit.costWater > resources.Water - houseCost.Water || unit.costBread > resources.Bread
                || (long)Mathf.Max(0, unit.costAP) + Mathf.Max(0, investment.APCost) > board.EnemyAP) continue;
            int wait = (int)System.Math.Max(1, (long)unit.costCitizen - resources.Citizen + houseCost.Citizen);
            if (wait > survivalTurns) { rejectReason = "housing_defense_cannot_wait"; continue; }
            var projected = forecast.SimulateInvestmentWithRecruit(board, investment, unit, wait, config.Horizon);
            if (projected.BreadPaymentFailed || projected.UpkeepPaymentFailed || projected.ImportantProductionStopped
                || projected.BreadCoverageTurns < BreadReserveTurns)
            { rejectReason = "housing_defense_recruit_forecast_unsafe"; continue; }
            best = unit;
        }
        return best;
    }
}
