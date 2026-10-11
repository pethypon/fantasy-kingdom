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
            || housingConfigRevision != config.Revision)
        {
            housingBoard = board; housingGeneration = board.Generation; housingOperationDemand = operationDemand;
            housingConfigRevision = config.Revision;
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
        float planned = Mathf.Max(0, ProductionDemand.Get(ResourceKind.Citizen).PlannedDemandPerTurn * forecast.ForecastTurns);
        planned = Mathf.Max(planned, BasicResources.Get(ResourceKind.Citizen)?.PlannedDemand ?? 0);
        UnitData defender = CheapestCapacityBlockedDefender(board, capacity, gain, cost, action.APCost, config);
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
        float futureArmy = Mathf.Max(operationDemand, military);
        float required = Mathf.Max(planned, futureArmy) + config.FreeTarget;
        if (emergency) required = Mathf.Max(required, defender.costCitizen);
        bool blocked = required > capacity + .001f;
        string reason = null;
        if (gain <= 0) reason = "housing_no_capacity_gain";
        else if (!blocked) reason = free < required ? "housing_existing_growth_can_recover_stock" : "housing_capacity_sufficient";
        else if (Economy.BreadPaymentFailed || Economy.UpkeepPaymentFailed || Economy.ImportantProductionStopped
            || Economy.State >= EconomicState.Crisis || MaterialSupplyNeedsRecovery()) reason = "housing_supply_recovery_first";
        if (reason == null)
        {
            var projected = forecast.SimulateInvestment(board, action, config.Horizon);
            if (projected.BreadPaymentFailed || projected.UpkeepPaymentFailed || projected.ImportantProductionStopped
                || projected.BreadCoverageTurns < BreadReserveTurns) reason = "housing_build_forecast_unsafe";
        }
        if (reason == null && Mode == StrategicMode.EmergencyDefense && !emergency) reason = "housing_direct_defense_first";

        float need = Mathf.InverseLerp(config.ConsiderRatio, Mathf.Max(config.ConsiderRatio + .001f, config.UrgentRatio), used);
        float future = Mathf.Clamp01((required - capacity) / Mathf.Max(1, gain));
        float opportunity = MaterialReservePressure(board, cost);
        float score = need * AIHousingConfig.Weight(config.PopulationNeedWeight)
            + future * AIHousingConfig.Weight(config.FuturePopulationNeedWeight)
            + AIHousingConfig.Weight(config.EconomicStabilityWeight)
            + (futureArmy > 0 ? AIHousingConfig.Weight(config.MilitaryExpansionWeight) : 0)
            - opportunity * AIHousingConfig.Weight(config.ResourceRiskPenalty);
        if (!blocked) score -= AIHousingConfig.Weight(config.ExcessHousingPenalty);
        if (Mode == StrategicMode.EmergencyDefense && !emergency) score -= AIHousingConfig.Weight(config.EmergencyPenalty);
        // Current capacity already includes every existing house: reevaluation prevents stale same-turn repeats.
        float excessGain = Mathf.Clamp01((capacity + gain - required) / Mathf.Max(1, capacity + gain));
        score -= excessGain * AIHousingConfig.Weight(config.RepeatedHousePenalty);
        if (reason != null) score = -Mathf.Max(AIHousingConfig.Weight(config.ExcessHousingPenalty), Mathf.Abs(score));
        return new AIHousingAssessment(reason == null, reason == null && emergency, blocked, free, capacity, gain,
            used, planned, operationDemand, military, required, score, reason ?? (emergency ? "housing_defense_capacity_blocked" : "housing_planned_capacity_shortfall"));
    }

    bool MaterialSupplyNeedsRecovery()
    {
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            if (i == (int)ResourceKind.Citizen) continue;
            var need = ProductionDemand.Get((ResourceKind)i);
            if (need.MandatoryDemandPerTurn > .001f && (need.NetPerTurn < -.001f || need.ProjectedStock < need.SafetyReserve)) return true;
            if (AIBasicResourceSettings.Active.enabled && BasicResources.Get((ResourceKind)i)?.State <= BasicResourceState.Shortage) return true;
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
    static bool CombatRecruit(UnitData unit) => unit != null && unit.kind != Kind.Scout && unit.kind != Kind.Priest
        && unit.kind != Kind.King && unit.kind != Kind.Boss && unit.kind != Kind.Crystal && unit.kind != Kind.SubCrystal
        && unit.actionProfile?.CanAttack != false;
    UnitData CheapestCapacityBlockedDefender(AIBoardState board, int capacity, int capacityGain, FacilityData.ResourceCost houseCost, int houseAP, AIHousingConfig config)
    {
        if (Mode != StrategicMode.EmergencyDefense || board.AlivePlayerUnits.Count == 0) return null;
        UnitData best = null;
        foreach (var unit in housingRecruits)
        {
            if (!CombatRecruit(unit) || unit.costCitizen <= capacity || unit.costCitizen > capacity + capacityGain
                || best != null && unit.costCitizen >= best.costCitizen) continue;
            var resources = board.EnemyResources;
            if (resources == null || unit.costCitizen > resources.Citizen - houseCost.Citizen + Mathf.Clamp(config.EmergencyRecruitWaitTurns, 1, 8)
                || unit.costWood > resources.Wood - houseCost.Wood || unit.costStone > resources.Stone - houseCost.Stone
                || unit.costIron > resources.Iron - houseCost.Iron || unit.costMagic > resources.MagicOre - houseCost.MagicOre
                || unit.costWater > resources.Water - houseCost.Water || unit.costBread > resources.Bread
                || Mathf.Max(0, unit.costAP) + Mathf.Max(0, houseAP) > board.EnemyAP) continue;
            best = unit;
        }
        return best;
    }
}
