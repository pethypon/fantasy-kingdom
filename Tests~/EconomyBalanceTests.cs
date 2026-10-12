#if UNITY_EDITOR
using System;
using UnityEngine;

public static class EconomyBalanceTests
{
    static int passed;
    static void Check(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("[EconomyBalance] FAIL " + name); passed++; }
    public static void Run(GameSystems live)
    {
        passed = 0;
        var rules = Resources.Load<GameAuthoringRules>(GameAuthoringRules.ResourcesPath);
        Check("production balance enabled", rules != null && rules.applyRules);
        Check("base storage limit is enforced without a warehouse", rules.enforceResourceCapacity);
        var player = rules.FindFaction(Team.Player).initialResources; var enemy = rules.FindFaction(Team.Enemy).initialResources;
        Check("both factions use the same opening budget", JsonUtility.ToJson(player) == JsonUtility.ToJson(enemy));
        Check("opening wood and stone banks reduced", player.Wood == 140 && player.Stone == 130);
        Check("material sinks are configured without taxing kings", rules.unitExtraUpkeep.Wood == 1 && rules.unitExtraUpkeep.Stone == 1 && rules.unitExtraUpkeep.Water == 1);
        Check("opening support ends rather than producing forever", EconomySystem.GetCrystalIncome(6).Wood == 0 && EconomySystem.GetCrystalIncome(30).Stone == 0);
        Check("foundation water cost no longer locks the opening", FacilityData.Table[FacilityKind.LoggingCamp].BuildCost.Water == 15
            && FacilityData.Table[FacilityKind.Quarry].BuildCost.Water == 15 && FacilityData.Table[FacilityKind.Mine].BuildCost.Water == 20);
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            f.Rules.crystalIncomeTurns = 4;
            f.Rules.crystalStartIncome = new FacilityData.ProductionBundle { Wood = 11, Stone = 7, Water = 5, Bread = 13, Iron = 3 };
            f.Rules.unitExtraUpkeep = new FacilityData.ProductionBundle { Wood = 2, Stone = 3, Water = 4 };
            f.State.GetNation(f.Team).TurnsAlive = 0;
            var unit = f.Unit(); unit.GrowthData.upkeepWood = unit.GrowthData.upkeepStone = unit.GrowthData.upkeepWater = 0;
            unit.GrowthData.upkeepMagic = 0; unit.Level = 1;
            var resources = f.Resources;
            resources.Wood = resources.Stone = resources.Water = resources.Bread = resources.Iron = 100;
            resources.MagicOre = resources.Wheat = 0; resources.Citizen = 5;
            var board = f.Board(); var forecast = new StrategicEconomyForecast(); forecast.ForecastTurns = 1;
            var predicted = forecast.Diagnose(board, false);
            f.Economy.ProcessTurn(f.Team);
            Check("real wood upkeep and support use authored amounts", resources.Wood == 109);
            Check("real stone upkeep and support use authored amounts", resources.Stone == 104);
            Check("real water upkeep and support use authored amounts", resources.Water == 101);
            Check("forecast sees the same support and maintenance", predicted.Get(ResourceKind.Wood).ProjectedStock == resources.Wood
                && predicted.Get(ResourceKind.Stone).ProjectedStock == resources.Stone && predicted.Get(ResourceKind.Water).ProjectedStock == resources.Water);
            var king = UnitStaticData.CreateUnitData(Kind.King);
            try { Check("royalty remains exempt", king.GetUpkeep(1).Wood == 0); }
            finally { UnityEngine.Object.DestroyImmediate(king); }
        }
        using (var f = new EconomyR2Tests.Fixture(live))
        {
            f.Rules.enforceResourceCapacity = false;
            f.Resources.Wood = 500;
            f.Economy.ProcessTurn(f.Team);
            Check("legacy disabled storage policy preserves existing uncapped behavior", f.Resources.Wood == 500);
            f.Rules.enforceResourceCapacity = true;
            f.Resources.Wood = f.Resources.Stone = f.Resources.Water = f.Resources.Iron = 500;
            var board = f.Board(); var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
            var predicted = forecast.Diagnose(board, false);
            f.Economy.ProcessTurn(f.Team);
            Check("real production obeys base capacity before any warehouse exists", f.Resources.Wood == FactionState.BaseResourceCap
                && f.Resources.Stone == FactionState.BaseResourceCap && f.Resources.Water == FactionState.BaseResourceCap && f.Resources.Iron == FactionState.BaseResourceCap);
            Check("forecast obeys the same base capacity", predicted.Get(ResourceKind.Wood).ProjectedStock == f.Resources.Wood
                && predicted.Get(ResourceKind.Stone).ProjectedStock == f.Resources.Stone);
            var warehouse = f.Definition(FacilityKind.Warehouse); var recipe = warehouse.levels[0];
            recipe.SpecialValue = 25; warehouse.levels[0] = recipe; f.Building(warehouse);
            f.Resources.Wood = f.Resources.Stone = 500; board.Refresh();
            predicted = forecast.Diagnose(board, false); f.Economy.ProcessTurn(f.Team);
            Check("authored warehouse gain extends actual storage", f.Resources.Wood == FactionState.BaseResourceCap + 25
                && f.State.GetResourceCap(f.Team) == FactionState.BaseResourceCap + 25);
            Check("forecast includes the exact authored warehouse capacity", predicted.Get(ResourceKind.Wood).ProjectedStock == f.Resources.Wood);
            var house = f.Definition(FacilityKind.House); recipe = house.levels[0];
            recipe.SpecialValue = int.MaxValue; house.levels[0] = recipe; f.Building(house);
            f.Rules.breadPerCitizen = 10; f.Resources.Citizen = int.MaxValue; f.Resources.Bread = 100;
            board.Refresh(); predicted = forecast.Diagnose(board, false); f.Economy.ProcessTurn(f.Team);
            Check("large authored citizen capacity cannot overflow food payment", f.Resources.Bread == 0
                && f.State.GetNation(f.Team).StarvationCounter == 1);
            Check("forecast detects the same large food shortage", predicted.Forecast.BreadPaymentFailed
                && predicted.Get(ResourceKind.Bread).MandatoryDemandPerTurn > int.MaxValue);
        }
        Debug.Log("[EconomyBalance] " + passed + " budget/maintenance/forecast checks passed");
    }
}
#endif
