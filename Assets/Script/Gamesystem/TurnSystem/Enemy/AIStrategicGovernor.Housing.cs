using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    // Owner: existing governor; lifetime: one board generation. Maximum: available housing definitions/upgrades.
    // Prune/clear: generation or operation demand changes. No turn-history or independent action driver is added.
    readonly Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, int), AIHousingAssessment> housingAssessments
        = new Dictionary<(AIActionType, FacilityKind, FacilityDefinitionData, Status, int), AIHousingAssessment>();
    readonly List<UnitData> housingRecruits = new List<UnitData>(32);
    readonly HashSet<string> housingAPLives = new HashSet<string>();
    readonly HashSet<Status> housingAPActors = new HashSet<Status>();
    readonly HashSet<Vector3Int> housingAPMoveCells = new HashSet<Vector3Int>();
    AIBoardState housingBoard;
    int housingGeneration = -1, housingOperationDemand = -1;
    int housingConfigRevision = -1;
    int housingContentRevision = -1;
    int housingPlanRevision = -1;
    int housingSquadRevision = -1;
    public AIHousingAPPlan HousingAPPlan { get; private set; }
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
            || housingPlanRevision != BasicResources.PlanRevision || housingSquadRevision != (board.Operations?.SquadRevision ?? -1))
        {
            housingBoard = board; housingGeneration = board.Generation; housingOperationDemand = operationDemand;
            housingConfigRevision = config.Revision;
            housingContentRevision = GameContentRevision.Revision;
            housingPlanRevision = BasicResources.PlanRevision;
            housingSquadRevision = board.Operations?.SquadRevision ?? -1;
            housingAssessments.Clear(); board.CollectRecruitDefinitions(housingRecruits);
            HousingAPPlan = PrepareHousingAPPlan(board, config);
        }
        var actor = action.ActionType == AIActionType.Upgrade ? action.Unit ?? action.TargetUnit : null;
        var key = (action.ActionType, kind, action.FacilityDefinition, actor, actor != null ? actor.Level : 0);
        if (housingAssessments.TryGetValue(key, out var cached)) return cached;
        var result = AssessHousing(action, board, config, operationDemand);
        housingAssessments[key] = result;
        if ((config.EnableDecisionLogs || AIEconomySettings.Active.enableDecisionLogs) && DevelopmentLog.IsEnabled(AILogLevel.Verbose))
            DevelopmentLog.Log($"[AI住宅投資] Turn={board.TurnCount} Team={board.ActorTeam} AP={board.EnemyAP} "
                + $"GrowthBlocked={ProductionDemand.NaturalCitizenGrowthPerTurn <= 0 && result.FreeCitizens < result.Capacity} "
                + $"CandidateAssessed=True BuildOrUpgrade={action.ActionType}/{kind} FoodForecast={Economy.NetBreadPerTurn:F1} "
                + $"UpkeepForecast={!Economy.UpkeepPaymentFailed} " + (result.Needed ? "採用候補 " : "却下 ") + result.Describe());
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
        // These are ordered purchases of real missing roles, not a second speculative army.
        for (int i = 0; i < Mathf.Min(RecruitmentPlan.Count, Mathf.Clamp(config.ShortTermCombatRecruits, 0, 8)); i++)
            if (CombatRecruit(RecruitmentPlan[i].Definition) && HousingRecruitCanBeFunded(RecruitmentPlan[i].Definition, board, config))
                military = Mathf.Max(military, RecruitmentPlan[i].Definition.costCitizen);
        // Operation needs and ordinary short-term recruitment refer to the same army, not two armies.
        float recruitCapacityStock = HasPreparedRecruitment(board) ? HousingRecruitCapacityStock(board, config) : plannedRecruit;
        float futureArmy = Mathf.Max(Mathf.Max(operationDemand, military), recruitCapacityStock);
        float required = RequiredHousingStock(plannedBuilding, recruitCapacityStock, operationDemand, military, otherPlan, config.FreeTarget,
            HousingAPPlan.RequiredCitizens);
        if (board.TurnCount >= Mathf.Clamp(config.CitizenReserveStartTurn, 1, 100))
            required = Mathf.Max(required, Mathf.Clamp(config.CitizenReserveTarget, 0, 30));
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
            if (reason == null && required > capacity
                && !HousingAPFoodReserveSafe(board, Mathf.CeilToInt(required), config))
                reason = "housing_ap_food_reserve_unsafe";
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
            reason ?? (emergency ? "housing_defense_capacity_blocked" : HousingAPPlan.RequiredCitizens > capacity
                ? "housing_ap_capacity_shortfall" : "housing_planned_capacity_shortfall"), HousingAPPlan, cost);
    }

    // Construction, an ordered recruit, external operations and the AP stock use the same
    // replenish-able pool. Never require the whole shopping list in stock at the same time.
    internal static float RequiredHousingStock(float building, float recruit, float operation, float military,
        float other, int reserve, int apCitizens)
    {
        float purchase = Mathf.Max(Mathf.Max(Mathf.Max(building, recruit), Mathf.Max(operation, military)), Mathf.Max(0, other));
        return Mathf.Max(purchase + Mathf.Max(0, reserve), Mathf.Max(0, apCitizens));
    }

    float HousingRecruitCapacityStock(AIBoardState board, AIHousingConfig config)
    {
        float required = 0;
        foreach (var need in RecruitmentPlan)
            if (HousingRecruitCanBeFunded(need.Definition, board, config))
                required = Mathf.Max(required, (float)System.Math.Min(int.MaxValue,
                    (long)Mathf.Max(0, need.Definition.costCitizen) + RecruitmentCitizenReserve) - config.FreeTarget);
        foreach (var need in DevelopmentPlan)
            if (HousingRecruitCanBeFunded(need.Definition, board, config))
                required = Mathf.Max(required, (float)System.Math.Min(int.MaxValue,
                    (long)Mathf.Max(0, need.Definition.costCitizen) + RecruitmentCitizenReserve) - config.FreeTarget);
        return required;
    }

    bool HousingRecruitCanBeFunded(UnitData definition, AIBoardState board, AIHousingConfig config)
    {
        if (definition == null || !definition.IsValidForAuthoring || !definition.AvailableFor(board.ActorTeam)
            || definition.costAP < 0 || definition.costAP > APSystem.MaximumAP) return false;
        var cost = AIBasicResourceEconomy.UnitCost(definition); var upkeep = definition.GetFixedUpkeep();
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            if (i == (int)ResourceKind.Citizen) continue;
            float spent = StrategicEconomyForecast.Amount(cost, i), ongoing = StrategicEconomyForecast.Amount(upkeep, i);
            if (spent <= 0 && ongoing <= 0) continue;
            if (EconomySystem.IsResourceCapacityEnforced(forecast.PreparedResourceCapacity - FactionState.BaseResourceCap)
                && spent > forecast.PreparedResourceCapacity) return false;
            var resource = ProductionDemand.Get((ResourceKind)i);
            float net = ProductionDemand.PermanentProduction((ResourceKind)i) - resource.MandatoryDemandPerTurn;
            float available = resource.Stock + Mathf.Min(0, net) * config.Horizon + Mathf.Max(0, net) * config.DevelopmentHorizon;
            if (available - spent - ongoing * config.Horizon < resource.SafetyReserve) return false;
        }
        return true;
    }

    /// <summary>Use the formal reset clamp and authored bonus. Never change the live APData.</summary>
    internal static int HousingAPAtCitizenStock(FactionState.APData source, int citizens)
    {
        if (source == null) return 0;
        return new FactionState.APData { Reset = source.Reset, Minus = source.Minus,
            Plus = (int)System.Math.Min(GameConstants.MaxAP, (long)Mathf.Max(0, citizens) * EconomySystem.CitizenAPBonus) }.Maximum;
    }

    internal static int HousingCitizensForAP(FactionState.APData source, int desired)
    {
        if (source == null || desired <= HousingAPAtCitizenStock(source, 0)) return 0;
        desired = Mathf.Clamp(desired, 0, APSystem.MaximumAP);
        // The actual population bonus saturates at GameConstants.MaxAP, including authored zero bonus.
        if (HousingAPAtCitizenStock(source, GameConstants.MaxAP) < desired) return 0;
        int low = 0, high = GameConstants.MaxAP;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (HousingAPAtCitizenStock(source, middle) >= desired) high = middle;
            else low = middle + 1;
        }
        return low;
    }

    AIHousingAPPlan PrepareHousingAPPlan(AIBoardState board, AIHousingConfig config)
    {
        var ap = board.OwnAPData;
        int free = Mathf.Max(0, board.EnemyResources?.Citizen ?? 0), capacity = forecast.PreparedCitizenCapacity;
        int stockAP = HousingAPAtCitizenStock(ap, free), capacityAP = HousingAPAtCitizenStock(ap, capacity);
        if (!config.EnableAPCapacityPlanning || ap == null)
            return new AIHousingAPPlan(0, 0, stockAP, capacityAP, 0, 0, "housing_ap_planning_disabled");
        housingAPLives.Clear(); housingAPActors.Clear(); housingAPMoveCells.Clear();
        int actions = 0; long demand = 0;
        PrepareArmyAdvanceTargets(board);
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actions >= config.APActionLimit || AITurnBudget.Expired) break;
            if (actor == null || !actor.IsAlive || actor.type != Type.Unit || actor.team != board.ActorTeam
                || MatchObjectiveRules.IsCommander(actor) || StatusEffectSystem.IsStunned(actor)
                || !housingAPActors.Add(actor) || !string.IsNullOrEmpty(actor.ReflectionLifeId) && !housingAPLives.Add(actor.ReflectionLifeId)) continue;
            int cost = -1;
            int repeatCost = -1;
            if (BoardActionProfile.For(actor)?.CanAttack != false && !SpecialSkillRuntime.AttackBlocked(actor)
                && board.GetAttackTargets(actor).Count > 0)
            {
                // First action after a fatigue reset. The official ability adjustment is reused;
                // free-action state can only reduce this conservative pending demand.
                cost = SpecialSkillRuntime.HasFreeAttackAP(actor) ? 0
                    : Mathf.Max(1, SpecialSkillRuntime.AttackAPAdjustment(actor, APSystem.BaseAttackCost));
                var ability = SpecialSkillRuntime.Definition(actor);
                if (cost > 0 && ability?.enabledForPlay == true && ability.ability == UnitSpecialSkill.GuardianLifeLink
                    && ability.guardianAttackCostReset == AbilityResetScope.OwnerTurn)
                    cost = Mathf.Max(1, cost - Mathf.Max(0, SpecialSkillRuntime.State(actor).guardianAttackCount));
                var apSystem = board.SpecialSystems?.APSystem;
                if (config.EnableRepeatAttackAPPlanning && apSystem != null && !AIHealingCapability.HasHealing(actor))
                    foreach (var target in board.GetAttackTargets(actor))
                        if (!AIActionEvaluator.CanFinishSpecialTarget(actor, target, board)
                            && AIActionEvaluator.EstimateEffectiveSpecialImpact(actor, target, board, out _) > 0)
                        {
                            repeatCost = apSystem.CalcAttackCostPreview(actor, 1 + GameConstants.GetExtraFatiguePerAction(actor.kind), false);
                            if (ability?.enabledForPlay == true && ability.ability == UnitSpecialSkill.GuardianLifeLink
                                && ability.guardianAttackCostReset == AbilityResetScope.OwnerTurn)
                                repeatCost = Mathf.Max(1, repeatCost - Mathf.Max(0, SpecialSkillRuntime.State(actor).guardianAttackCount) + 1);
                            break;
                        }
            }
            if (cost < 0) cost = HousingPendingMoveCost(actor, board);
            if (cost < 0 || cost > APSystem.MaximumAP) continue;
            demand += cost; actions++;
            if (repeatCost >= 0 && repeatCost <= APSystem.MaximumAP && actions < config.APActionLimit)
            { demand += repeatCost; actions++; }
        }
        // One real, maintainable pending recruit shares the same AP pool with the army.
        // This is neither a permanent maximum-AP target nor an extra/free summon.
        if (config.EnableRecruitmentAPPlanning && actions > 0 && actions < config.APActionLimit)
            foreach (var need in RecruitmentPlan)
                if (HousingRecruitCanBeFunded(need.Definition, board, config))
                { demand += need.Definition.costAP; actions++; break; }
        int desired = (int)System.Math.Min(APSystem.MaximumAP, demand);
        if (HousingAPAtCitizenStock(ap, GameConstants.MaxAP) < desired)
            return new AIHousingAPPlan(actions, desired, stockAP, capacityAP, 0, 0, "housing_ap_bonus_cannot_reach_target");
        if (config.EnableIncrementalAPPreparation && desired > capacityAP)
            desired = Mathf.Min(desired, HousingAPAtCitizenStock(ap, Mathf.Min(GameConstants.MaxAP, free + config.APPreparationHorizon)));
        int required = HousingCitizensForAP(ap, desired);
        int wait = Mathf.Max(0, required - free); // EconomySystem grows at most one citizen per paid turn.
        string reason = required <= capacity ? stockAP < desired ? "housing_ap_existing_growth_can_recover_stock" : "housing_ap_capacity_sufficient"
            : wait > config.APPreparationHorizon ? "housing_ap_growth_outside_horizon" : "housing_ap_capacity_shortfall";
        // A large missing stock is a growth delay, not permission to buy arbitrarily distant capacity.
        if (wait > config.APPreparationHorizon) required = 0;
        return new AIHousingAPPlan(actions, desired, stockAP, capacityAP, required, wait, reason);
    }

    int HousingPendingMoveCost(Status actor, AIBoardState board)
    {
        if (actor.HP < actor.MaxHP * .6f || BoardActionProfile.For(actor)?.CanMove == false
            || StatusEffectSystem.IsMovementBlocked(actor) || SpecialSkillRuntime.MovementBlocked(actor)) return -1;
        bool hasAim = false; Vector3 aim = default;
        if (board.Operations != null && board.Operations.TryGetSquad(actor.ReflectionLifeId, out var squad))
        {
            if (squad.Phase == AIOperationSquadPhase.Suspended) return -1;
            foreach (var member in squad.Members)
                if (member.ActorLifeId == actor.ReflectionLifeId)
                {
                    if (member.Recovering) return -1;
                    if (member.HasSlot) { hasAim = true; aim = new Vector3(member.SlotX, 0, member.SlotZ); }
                    break;
                }
            if (!hasAim) { hasAim = true; aim = new Vector3(squad.GoalX, 0, squad.GoalZ); }
        }
        var objective = Exploration.GetObjective(actor.GetInstanceID());
        if (!hasAim && objective != null && objective.State != ObjectiveState.Completed
            && objective.State != ObjectiveState.Failed && objective.State != ObjectiveState.Suspended)
        { hasAim = true; aim = objective.TargetCell; }
        if (!hasAim && armyAdvanceTargets.Count == 0 && !HasActiveTerritoryAdvance) return -1;
        int best = int.MaxValue; Vector3Int bestCell = default;
        foreach (var destination in board.GetValidMoves(actor))
        {
            var cell = GridHelper.ToGridXZ(destination);
            if (housingAPMoveCells.Contains(cell) || !board.IsTerrainKnown(destination)) continue;
            bool progress = hasAim ? Distance(destination, aim) < Distance(actor.transform.position, aim)
                : ArmyAdvanceMakesProgress(actor.transform.position, destination)
                    || HasActiveTerritoryAdvance && TerritoryIntel.IsProgress(actor, destination, board);
            if (!progress || IncomingDamage(board, actor, destination, null) >= actor.HP * .25f) continue;
            int cost = board.CalcMoveCost(actor, destination);
            // Formal fixed-price movement excludes fatigue (assassin/cave/free move).
            bool fixedCost = cost == 0 || SpecialSkillRuntime.HasAssassinPassive(actor)
                || board.MapCreate != null && board.MapCreate.Environment.IsCaveConnection(actor.transform.position, destination);
            if (!fixedCost) cost = Mathf.Max(1, cost - Mathf.Max(0, actor.Fatigue));
            if (cost < best) { best = cost; bestCell = cell; }
        }
        if (best == int.MaxValue) return -1;
        housingAPMoveCells.Add(bestCell); return best;
    }

    bool HousingAPFoodReserveSafe(AIBoardState board, int citizens, AIHousingConfig config)
    {
        var bread = ProductionDemand.Get(ResourceKind.Bread);
        int current = Mathf.Max(0, board.EnemyResources?.Citizen ?? 0);
        int turns = Mathf.Max(config.Horizon, Mathf.Min(config.APPreparationHorizon, Mathf.Max(1, citizens - current)));
        // Forecast already includes existing upkeep, recipes and current population. Protect the
        // extra food after the AP stock arrives as well; temporary/chance income is not promised.
        float additionalFood = Mathf.Max(0, citizens - current) * EconomySystem.CitizenBreadCost;
        float dependableNet = ProductionDemand.PermanentProduction(ResourceKind.Bread) - bread.MandatoryDemandPerTurn - additionalFood;
        return bread.Stock + Mathf.Min(0, dependableNet) * (turns + BreadReserveTurns) >= bread.SafetyReserve;
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
