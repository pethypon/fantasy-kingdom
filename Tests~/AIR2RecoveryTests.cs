#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>R2 food/capital recovery contracts. Uses real authoring recipes and paid production transactions.</summary>
public static class AIR2RecoveryTests
{
    static int passed, failed;
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[AIR2Recovery] FAIL " + name); passed++; Debug.Log("[AIR2Recovery] PASS " + name); }
    static void Case(string name, Action test)
    { try { test(); } catch (Exception e) { failed++; Debug.Log("[AIR2RecoveryCase] FAIL " + name + " " + e.Message); } }
    public static void Run(GameSystems live)
    {
        passed = failed = 0;
        string rules = JsonUtility.ToJson(GameAuthoringRules.Active);
        var facilities = new List<FacilityDefinitionData>(FacilityAuthoringCatalog.Loaded.standardOverrides);
        var units = new Dictionary<Kind, UnitData>(live.UnitSetting.UnitDataMap);
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            Case(team + " prerequisite", () => FoodPrerequisite(live, team));
            Case(team + " opening", () => Opening(live, team, rules, facilities, units));
            Case(team + " optional military", () => OptionalMilitary(live, team));
        }
        Debug.Log("[AIR2Recovery] checks=" + passed + " failedCases=" + failed);
        if (failed != 0) throw new InvalidOperationException("[AIR2Recovery] " + failed + " failed cases");
    }
    static void FoodPrerequisite(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        f.Units.UnitDataMap.Clear(); f.Rules.aiEconomy.plannedDemandWeight = 0;
        f.Rules.aiEconomy.forecastTurns = f.Rules.aiEconomy.reserveTurns = 4;
        f.Resources.Citizen = 5; f.Resources.Wood = 50; f.Resources.Bread = 10; f.Resources.Wheat = 0;
        f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 10));
        f.Building(f.Definition(FacilityKind.Quarry, stone: 20));
        f.Building(f.Definition(FacilityKind.Well, water: 20));
        var bakery = f.Definition(FacilityKind.Bakery, bread: 100);
        bakery.buildCost = new FacilityData.ResourceCost(wood: 60);
        var recipe = bakery.levels[0]; recipe.Input.Wheat = 10; bakery.levels[0] = recipe;
        var field = f.Definition(FacilityKind.Field, wheat: 10);
        field.buildCost = new FacilityData.ResourceCost(wood: 20);
        var diversion = f.Definition(FacilityKind.Well, water: 1);
        diversion.buildCost = new FacilityData.ResourceCost(wood: 20);
        var unpayable = f.Definition(FacilityKind.Field, wheat: 10);
        unpayable.buildCost = new FacilityData.ResourceCost(wood: 45);
        GameContentRevision.NotifyChanged();
        var board = f.Board(20); var policy = board.Governor.BasicResources;
        var action = f.BuildAction(field, board);
        Check(team + " reproduction has mandatory bread loss with no existing wheat input demand",
            board.Governor.Economy.BreadPaymentFailed && board.ProductionDemand.Get(ResourceKind.Bread).MandatoryDemandPerTurn > 0
            && board.ProductionDemand.Get(ResourceKind.Wheat).MandatoryDemandPerTurn == 0);
        string stock = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(team);
        bool allowed = policy.AllowSpend(action, false, out string reason);
        Debug.Log("[AIR2RecoveryReproduction] " + team + " fieldAllowed=" + allowed + " reason=" + reason);
        Check(team + " productive prerequisite is not locked behind its downstream bakery's capital", allowed);
        Check(team + " unrelated funded construction still preserves the downstream capital",
            !policy.AllowSpend(f.BuildAction(diversion, board), false, out string diversionReason)
                && diversionReason.StartsWith("mandatory_supply_capital_", StringComparison.Ordinal));
        Check(team + " prerequisite investment unable to restore reserved capital remains blocked",
            !policy.AllowSpend(f.BuildAction(unpayable, board), false, out _));
        Check(team + " recovery assessment changes no real AP or resources", stock == JsonUtility.ToJson(f.Resources) && ap == f.State.GetAP(team));
        int wood = f.Resources.Wood;
        Check(team + " permitted prerequisite is a real charged construction", board.Governor.AllowExecution(action, board)
            && f.Builder.TryPlaceDefinition(Vector3Int.RoundToInt(action.TargetPos), field, team)
            && f.Resources.Wood == wood - field.buildCost.Wood && f.State.GetAP(team) == ap - field.buildAP);
        for (int i = 0; i < 3; i++) f.Economy.ProcessTurn(team);
        Check(team + " existing guaranteed income and the paid field really replenish bakery capital and input",
            f.Resources.Wood >= bakery.buildCost.Wood && f.Resources.Wheat >= bakery.GetLevel(1).Input.Wheat);
        board = f.Board(23); var bakeryAction = f.BuildAction(bakery, board);
        Check(team + " downstream bakery can then be built through the normal paid rules", board.Governor.AllowExecution(bakeryAction, board)
            && f.Builder.TryPlaceDefinition(Vector3Int.RoundToInt(bakeryAction.TargetPos), bakery, team));
        f.Economy.ProcessTurn(team);
        Check(team + " the completed supply chain produces bread and ends actual starvation", f.Resources.Bread > 0
            && f.State.GetNation(team).StarvationCounter == 0);
    }
    static void Opening(GameSystems live, Team team, string rules, List<FacilityDefinitionData> definitions, Dictionary<Kind, UnitData> units)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        JsonUtility.FromJsonOverwrite(rules, f.Rules); f.Catalog.standardOverrides = new List<FacilityDefinitionData>(definitions);
        typeof(UnitSetting).GetProperty("UnitDataMap").SetValue(f.Units, new Dictionary<Kind, UnitData>(units));
        f.Resources.Wood = 140; f.Resources.Stone = 130; f.Resources.Water = 60;
        f.Resources.Iron = 30; f.Resources.MagicOre = 12; f.Resources.Wheat = 0; f.Resources.Bread = 100;
        f.State.GetNation(team).TurnsAlive = 1;
        foreach (var kind in new[] { Kind.Knight, Kind.Archer, Kind.Scout })
        { var unit = f.Unit(); unit.kind = kind; unit.Level = 1; unit.GrowthData = units[kind]; }
        GameContentRevision.NotifyChanged();
        var board = f.Board(1); AITurnBudget.Begin(3000);
        var actions = AIActionEvaluator.EvaluateAll(f.Personality, board, null, TurnStrategy.Balanced);
        AIAction field = actions.Find(a => a.ActionType == AIActionType.Build && a.Facility == FacilityKind.Field);
        Check(team + " opening field remains a legal generated paid investment", field != null);
        Debug.Log("[AIR2OpeningFoodReproduction] " + team + " mode=" + board.Governor.Mode
            + " fieldPriority=" + field.StrategicPriority + " fieldScore=" + field.Score);
        Check(team + " required food input competes with military movement before starvation", field.StrategicPriority <= 3);
        var house = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.House,
            APCost = FacilityData.Table[FacilityKind.House].APCost, TargetPos = field.TargetPos };
        var assessment = board.Governor.HousingAssessment(house, board);
        Check(team + " capacity expansion cannot precede a missing permanent food chain", !assessment.Needed
            && assessment.Reason == "housing_supply_recovery_first");
    }
    static void OptionalMilitary(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        f.Units.UnitDataMap.Clear(); f.Rules.aiEconomy.plannedDemandWeight = 0; f.Food(40);
        f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 20));
        f.Resources.Stone = 1; f.Resources.Wood = f.Resources.Bread = f.Resources.Water = 200;
        var wall = f.Definition(FacilityKind.WoodWall); wall.buildCost = new FacilityData.ResourceCost(wood: 2);
        var board = f.Board(20); var action = f.BuildAction(wall, board);
        Check(team + " reproduction has real basic recovery while the payment forecast has not collapsed",
            board.Governor.Mode == StrategicMode.EconomicRecovery && board.Governor.Economy.State < EconomicState.Crisis);
        Check(team + " optional wall cannot bypass basic supply recovery", !board.Governor.AllowExecution(action, board)
            && action.StrategicRejectReason == "economic_recovery_blocks_military_build");
    }
}
#endif
