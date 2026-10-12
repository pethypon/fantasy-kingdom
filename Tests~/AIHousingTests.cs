#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Real economy/governor/planner fixtures. House amounts come from authored recipes, not test-only rules.</summary>
public static class AIHousingTests
{
    static int passed;
    static void Check(string name, bool value, string detail = null)
    {
        if (!value) throw new InvalidOperationException("[AIHousing] FAIL " + name + (detail == null ? "" : " / " + detail));
        passed++; Debug.Log("[AIHousing] PASS " + name);
    }
    public static void Run(GameSystems live)
    {
        passed = 0;
        var active = typeof(AIHousingConfig).GetField("active", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = active.GetValue(null);
        var config = ScriptableObject.CreateInstance<AIHousingConfig>(); config.ShortTermCombatRecruits = 0;
        active.SetValue(null, config);
        try
        {
            CapacityAndDemand(live);
            CombinedPlans(live);
            ExplicitPlanChanges(live);
            SupplyAndRisk(live);
            EmergencyAndFog(live, config);
            PurchaseAndReevaluation(live);
            UpgradeAndReevaluation(live);
            CapacityBounds(live);
            BoundedCache(live);
            Check("NaN settings use finite thresholds", SetInvalidThresholds(config));
        }
        finally { active.SetValue(null, previous); UnityEngine.Object.DestroyImmediate(config); }
        Debug.Log("[AIHousing] " + passed + " investment/economy/capacity checks passed");
    }
    static bool SetInvalidThresholds(AIHousingConfig config)
    { config.ConsiderCitizenRatio = float.NaN; config.UrgentCitizenRatio = float.PositiveInfinity; return !float.IsNaN(config.ConsiderRatio) && !float.IsInfinity(config.UrgentRatio); }

    static FacilityDefinitionData House(EconomyR2Tests.Fixture f, int gain = 8, int wood = 30)
    {
        var house = f.Definition(FacilityKind.House);
        var recipe = house.levels[0]; recipe.SpecialValue = gain; house.levels[0] = recipe;
        house.buildCost = new FacilityData.ResourceCost(wood: wood, stone: 20);
        house.buildAP = 2;
        return house;
    }
    static void Stable(EconomyR2Tests.Fixture f)
    {
        f.Rules.aiEconomy.basicResources.enabled = false;
        f.Food(40);
    }
    static void CapacityAndDemand(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f); var board = f.Board(); var action = f.BuildAction(house, board);
            var enough = board.Governor.HousingAssessment(action, board);
            Check("T11 spendable free citizens are not treated as occupied population", enough.FreeCitizens == 5
                && enough.Capacity == 5 && enough.UsedSlotRatio == 0 && !enough.Needed);
            Check("T22 excess housing explains why it is rejected", !board.Governor.AllowExecution(action, board)
                && action.StrategicRejectReason == "housing_capacity_sufficient" && enough.Describe().Contains("空き市民"));
            board.OperationRequiredCitizenDemand = 8;
            action.StrategicRejectReason = null;
            var future = board.Governor.HousingAssessment(action, board);
            Check("T13 a real operation's future citizens can justify advance housing", future.Needed && future.CapacityBlocked
                && future.OperationCitizens == 8 && future.RequiredStock > future.Capacity);
            Check("T21 housing decision diagnostics expose current and planned facts", future.Describe().Contains("作戦需要")
                && future.Reason == "housing_planned_capacity_shortfall");
            var candidates = new List<AIAction> { action }; board.Governor.Filter(candidates, board);
            Check("normal housing respects existing strategic priority tiers", candidates.Count == 1 && action.StrategicPriority == 3);
            float highFreeScore = future.Score;
            f.Resources.Citizen = 0; board.Refresh(); board.OperationRequiredCitizenDemand = 8;
            var used = board.Governor.HousingAssessment(action, board);
            Check("T12 high used-slot pressure raises score without reversing free citizen semantics", used.UsedSlotRatio == 1
                && used.Score > highFreeScore);
            board.OperationRequiredCitizenDemand = 0;
            var lowStock = board.Governor.HousingAssessment(action, board);
            Check("low citizen stock with sufficient existing capacity requests replenishment instead of houses", !lowStock.Needed
                && lowStock.Reason == "housing_existing_growth_can_recover_stock");
        }
    }
    static void SupplyAndRisk(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            f.Rules.aiEconomy.basicResources.enabled = false;
            f.Resources.Bread = 1; var house = House(f); var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var assessment = board.Governor.HousingAssessment(f.BuildAction(house, board), board);
            Check("T14 food payment failure blocks ordinary housing", !assessment.Needed && assessment.Reason == "housing_supply_recovery_first");
        }
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var consumer = f.Unit(upkeepWood: 40); var house = House(f);
            f.Resources.Wood = 20; var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            Check("material upkeep deficit outranks housing even when bread is plentiful", !board.Governor.HousingAssessment(f.BuildAction(house, board), board).Needed);
        }
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); f.Unit(upkeepWood: 10); f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 10));
            f.Building(f.Definition(FacilityKind.Mine, iron: 10));
            f.Resources.Wood = 70; var house = House(f, wood: 65); var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var assessment = board.Governor.HousingAssessment(f.BuildAction(house, board), board);
            Check("build cost cannot consume next turn's mandatory payment reserve", !assessment.Needed
                && assessment.Reason == "housing_build_forecast_unsafe");
        }
    }
    static void ExplicitPlanChanges(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f); var board = f.Board(); var action = f.BuildAction(house, board);
            var before = board.Governor.HousingAssessment(action, board); int generation = board.Generation;
            board.Governor.BasicResources.Evaluate(board, board.Governor.ProductionDemand, false,
                new[] { new BasicResourcePlan(new FacilityData.ProductionBundle { Citizen = 8 }) });
            var external = board.Governor.HousingAssessment(action, board);
            Check("existing explicit economy plans are retained as real housing demand", !before.Needed && external.Needed
                && external.PlannedOtherCitizens == 6 && external.RequiredStock == 8);
            board.Governor.BasicResources.Evaluate(board, board.Governor.ProductionDemand, false,
                new[] { new BasicResourcePlan(new FacilityData.ProductionBundle { Citizen = 2 }) });
            Check("housing cache reacts to plan changes within the same board generation", board.Generation == generation
                && !board.Governor.HousingAssessment(action, board).Needed);
        }
    }
    static void CombinedPlans(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f);
            var project = f.Definition(FacilityKind.LoggingCamp, wood: 10);
            project.buildCost = new FacilityData.ResourceCost(citizen: 4);
            f.Rules.aiEconomy.plannedDemandWeight = 1;
            f.Rules.aiEconomy.plannedBuildActions = f.Rules.aiEconomy.plannedSummonActions = 1;
            var scout = f.UnitDefinition(); scout.kind = Kind.Scout; scout.costCitizen = 3;
            f.Units.UnitDataMap.Clear(); f.Units.UnitDataMap[Kind.Scout] = scout;
            var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var demand = board.ProductionDemand;
            var assessment = board.Governor.HousingAssessment(f.BuildAction(house, board), board);
            Check("future demand uses actual construction and summon definition costs", demand.PlannedBuildingCitizens == 4
                && demand.PlannedRecruitCitizens == 1.5f);
            Check("operation army demand retains the simultaneous economic construction demand", assessment.Needed
                && assessment.PlannedBuildingCitizens >= 4
                && assessment.RequiredStock == assessment.PlannedBuildingCitizens + 8 + AIHousingConfig.Active.FreeTarget,
                assessment.Describe() + " 経済=" + board.Governor.Economy.State);
            Check("ordinary recruits and operation recruits are not counted as two separate armies", assessment.RequiredStock
                < assessment.PlannedCitizens + assessment.OperationCitizens + AIHousingConfig.Active.FreeTarget);
        }
    }
    static void EmergencyAndFog(GameSystems live, AIHousingConfig config)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f); var cells = f.AdjacentCells();
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]); f.Crystal.DEF = 0;
            var foe = f.Opponent(cells[2], 50);
            int threatenedHP = DamageCalculator.EstimateBaseDamage(foe.ATK, f.Crystal.DEF) + 1;
            f.Crystal.HP = threatenedHP;
            var defender = f.UnitDefinition(); defender.kind = Kind.Knight; defender.costCitizen = 6; defender.costAP = 3;
            f.Units.UnitDataMap.Clear(); f.Units.UnitDataMap[Kind.Knight] = defender;
            var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var action = f.BuildAction(house, board);
            Check("unobserved hostile presence cannot enable emergency housing", !board.Governor.HousingAssessment(action, board).EmergencySupply);
            board.AlivePlayerUnits.Add(foe); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var emergency = board.Governor.HousingAssessment(action, board);
            Check("T15 observed danger and a capacity-blocked recruit allow defensive housing", board.Governor.Mode == StrategicMode.EmergencyDefense
                && emergency.Needed && emergency.EmergencySupply,
                emergency.Describe() + " Mode=" + board.Governor.Mode + " incoming="
                + board.Governor.IncomingDamage(board, f.Crystal, f.Crystal.transform.position, null));
            Check("defense planning does not spend resources or invent citizens during preview", f.Resources.Wood == 400
                && f.Resources.Bread == 400 && f.Resources.Citizen == 5);
            var candidates = new List<AIAction> { action }; board.Governor.Filter(candidates, board);
            Check("defensive housing has P1 without overriding P0 king survival/direct defense", candidates.Count == 1 && action.StrategicPriority == 1);
            defender.useAuthoredUpkeep = true; defender.upkeepBread = 1000;
            board.Refresh(); board.AlivePlayerUnits.Add(foe); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var cannotMaintain = board.Governor.HousingAssessment(action, board);
            Check("emergency housing includes the planned defender's real maintenance cost", !cannotMaintain.Needed
                && cannotMaintain.Reason == "housing_defense_recruit_forecast_unsafe");
            defender.useAuthoredUpkeep = false; defender.upkeepBread = 0; f.Crystal.HP = 10;
            board.Refresh(); board.AlivePlayerUnits.Add(foe); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var cannotWait = board.Governor.HousingAssessment(action, board);
            Check("housing does not promise immediate defense while citizen replenishment takes a turn", !cannotWait.Needed
                && cannotWait.Reason == "housing_defense_cannot_wait");
            f.Crystal.HP = threatenedHP;
            defender.costCitizen = 2; board.Refresh(); board.AlivePlayerUnits.Add(foe); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var directDefense = board.Governor.HousingAssessment(action, board);
            Check("available defense without a capacity bottleneck suppresses optional housing", !directDefense.Needed
                && directDefense.Reason == "housing_direct_defense_first");
        }
    }
    static void PurchaseAndReevaluation(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f); var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var action = f.BuildAction(house, board);
            var before = board.Governor.HousingAssessment(action, board);
            Check("edited official house capacity is used by AI", before.CapacityGain == 8);
            var recipe = house.levels[0]; recipe.SpecialValue = 12; house.levels[0] = recipe;
            house.buildCost = new FacilityData.ResourceCost(wood: 45, stone: 20);
            board.Refresh(); var edited = board.Governor.HousingAssessment(action, board);
            Check("T19 refreshed board does not retain the old definition's gain", edited.CapacityGain == 12);
            recipe.SpecialValue = 14; house.levels[0] = recipe;
            GameContentRevision.NotifyChanged();
            var hotReloaded = board.Governor.HousingAssessment(action, board);
            Check("T19 content revision invalidates housing even without a new board generation", hotReloaded.CapacityGain == 14);
            recipe.SpecialValue = 12; house.levels[0] = recipe; GameContentRevision.NotifyChanged();
            int wood = f.Resources.Wood, stone = f.Resources.Stone;
            f.Catalog.buildings.Clear(); f.Catalog.buildings.Add(house);
            board.AffordableBuildings.Clear(); board.AffordableBuildings.Add(FacilityKind.House);
            AITurnBudget.Begin(3000);
            int purchased = (int)typeof(AIBuildPlanner).GetMethod("TryScoreSingleBuild", EconomyR2Tests.Fixture.Private)
                .Invoke(f.Planner(), new object[] { board, TurnStrategy.Balanced, 20 });
            Check("investment is executed by the existing paid build planner", purchased == 1 && f.Builder.GetBuildingCount(Team.Enemy, FacilityKind.House) == 1);
            Check("edited official house cost is really charged", f.Resources.Wood == wood - 45 && f.Resources.Stone == stone - 20);
            var after = board.Governor.HousingAssessment(action, board);
            Check("T16 actual built recipe raises forecast capacity immediately", after.Capacity == before.Capacity + 12);
            Check("T16 paid build immediately updates the real nation and HUD capacity", f.State.GetCitizenCap(Team.Enemy) == after.Capacity);
            Check("house capacity investment does not invent immediately spendable citizens", f.Resources.Citizen == 5);
            Check("T17/T18 candidate is reevaluated and a second unnecessary house is rejected", !after.Needed
                && !after.CapacityBlocked && after.Capacity >= after.RequiredStock
                && after.Reason == "housing_existing_growth_can_recover_stock", after.Describe());
        }
    }
    static void UpgradeAndReevaluation(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f, gain: 2);
            var next = house.levels[0]; next.SpecialValue = 8; next.HP = 200;
            next.UpgradeCost = new FacilityData.ResourceCost(wood: 10, stone: 5); next.UpgradeAP = 2;
            house.levels = new[] { house.levels[0], next };
            var actor = f.Building(house); var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var action = new AIAction { ActionType = AIActionType.Upgrade, Unit = actor, TargetUnit = actor,
                Facility = FacilityKind.House, FacilityDefinition = house, APCost = next.UpgradeAP };
            var assessment = board.Governor.HousingAssessment(action, board);
            Check("housing upgrade evaluates marginal capacity rather than granting the full total twice", assessment.Capacity == 7
                && assessment.CapacityGain == 6 && assessment.Needed);
            var executor = (AIActionExecutor)typeof(AIBuildPlanner).GetField("_executor", EconomyR2Tests.Fixture.Private).GetValue(f.Planner());
            int wood = f.Resources.Wood, stone = f.Resources.Stone, ap = board.EnemyAP;
            Check("housing upgrades use the existing paid execution transaction", executor.Execute(action, board)
                && actor.Level == 2 && f.Resources.Wood == wood - 10 && f.Resources.Stone == stone - 5
                && board.EnemyAP == ap - 2);
            var upgraded = board.Governor.HousingAssessment(action, board);
            Check("upgraded capacity is refreshed before a further housing decision", upgraded.Capacity == 13 && !upgraded.Needed);
            Check("housing upgrades immediately update the real capacity using the owning builder", f.State.GetCitizenCap(Team.Enemy) == upgraded.Capacity);
        }
    }
    static void BoundedCache(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f); var board = f.Board();
            var action = f.BuildAction(house, board);
            for (int i = 0; i < 500; i++)
            {
                board.OperationRequiredCitizenDemand = i % 2 == 0 ? 8 : 0;
                var assessment = board.Governor.HousingAssessment(action, board);
                if (assessment.Needed != (i % 2 == 0)) throw new InvalidOperationException("[AIHousing] stale operation demand at " + i);
            }
            var cache = (System.Collections.IDictionary)typeof(AIStrategicGovernor)
                .GetField("housingAssessments", EconomyR2Tests.Fixture.Private).GetValue(board.Governor);
            Check("500 changing operation plans neither retain stale decisions nor grow housing history", cache.Count == 1);
        }
    }
    static void CapacityBounds(GameSystems live)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f, gain: int.MaxValue);
            var first = f.Building(house); f.Building(house);
            var warehouse = f.Definition(FacilityKind.Warehouse); var recipe = warehouse.levels[0];
            recipe.SpecialValue = int.MaxValue; warehouse.levels[0] = recipe;
            f.Building(warehouse); f.Building(warehouse);
            f.Economy.RecalculateCapacity(f.Team);
            var board = f.Board(); var forecast = new StrategicEconomyForecast(); forecast.Prepare(board);
            var assessment = board.Governor.HousingAssessment(f.BuildAction(house, board), board);
            Check("large housing totals saturate identically in real nation, forecast and AI", forecast.PreparedCitizenCapacity == int.MaxValue
                && f.State.GetCitizenCap(f.Team) == int.MaxValue && assessment.Capacity == int.MaxValue);
            Check("large warehouse totals saturate identically in real nation and forecast", forecast.PreparedResourceCapacity == int.MaxValue
                && f.State.GetResourceCap(f.Team) == int.MaxValue);
            Check("saturated housing neither wraps nor triggers more houses", !assessment.Needed && !float.IsNaN(assessment.Score)
                && !float.IsInfinity(assessment.Score));
            // A smaller level for one of two huge houses must still retain the other house's full capacity.
            var smaller = house.levels[0]; smaller.SpecialValue = 3;
            house.levels = new[] { house.levels[0], smaller }; f.Resources.Citizen = 1000;
            f.Rules.breadPerCitizen = 0; GameContentRevision.NotifyChanged();
            board.Refresh(); forecast.Prepare(board); var baseline = forecast.Simulate(board);
            var downgrade = new AIAction { ActionType = AIActionType.Upgrade, Unit = first, TargetUnit = first,
                Facility = FacilityKind.House, FacilityDefinition = house };
            var changed = forecast.SimulateInvestment(board, downgrade, forecast.ForecastTurns);
            Check("candidate upgrades preserve the full sum before saturating capacity", baseline.NetBreadPerTurn < 40
                && changed.NetBreadPerTurn == baseline.NetBreadPerTurn);
            recipe.SpecialValue = 12; warehouse.levels[0] = recipe;
            house.levels = new[] { smaller }; GameContentRevision.NotifyChanged(); f.Economy.RecalculateCapacity(f.Team);
            forecast.Prepare(board);
            Check("balance revision can restore ordinary capacities after a saturated value", forecast.PreparedCitizenCapacity == FactionState.BaseCitizenCap + 6
                && forecast.PreparedCitizenCapacity == f.State.GetCitizenCap(f.Team)
                && forecast.PreparedResourceCapacity == FactionState.BaseResourceCap + 24
                && forecast.PreparedResourceCapacity == f.State.GetResourceCap(f.Team));
        }
    }
}
#endif
