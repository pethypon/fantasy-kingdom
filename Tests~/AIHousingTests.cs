#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Real economy/governor/planner fixtures. House amounts come from authored recipes, not test-only rules.</summary>
public static class AIHousingTests
{
    static int passed;
    static void Check(string name, bool value)
    {
        if (!value) throw new InvalidOperationException("[AIHousing] FAIL " + name);
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
            SupplyAndRisk(live);
            EmergencyAndFog(live, config);
            PurchaseAndReevaluation(live);
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
    static void EmergencyAndFog(GameSystems live, AIHousingConfig config)
    {
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            Stable(f); var house = House(f); var cells = f.AdjacentCells();
            f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]); f.Crystal.HP = 10; f.Crystal.DEF = 0;
            var foe = f.Opponent(cells[2], 50);
            var defender = f.UnitDefinition(); defender.kind = Kind.Knight; defender.costCitizen = 6; defender.costAP = 3;
            f.Units.UnitDataMap.Clear(); f.Units.UnitDataMap[Kind.Knight] = defender;
            var board = f.Board(); board.OperationRequiredCitizenDemand = 8;
            var action = f.BuildAction(house, board);
            Check("unobserved hostile presence cannot enable emergency housing", !board.Governor.HousingAssessment(action, board).EmergencySupply);
            board.AlivePlayerUnits.Add(foe); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var emergency = board.Governor.HousingAssessment(action, board);
            Check("T15 observed danger and a capacity-blocked recruit allow defensive housing", board.Governor.Mode == StrategicMode.EmergencyDefense
                && emergency.Needed && emergency.EmergencySupply);
            var candidates = new List<AIAction> { action }; board.Governor.Filter(candidates, board);
            Check("defensive housing has P1 without overriding P0 king survival/direct defense", candidates.Count == 1 && action.StrategicPriority == 1);
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
            Check("T17/T18 candidate is reevaluated and a second unnecessary house is rejected", !after.Needed
                && after.Reason == "housing_capacity_sufficient");
        }
    }
}
#endif
