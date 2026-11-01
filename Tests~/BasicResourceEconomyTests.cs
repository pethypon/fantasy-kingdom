using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Foundation-policy regressions use transient recipes and real transactions, never saved rule assets.</summary>
public static class BasicResourceEconomyTests
{
    static void Check(string label, bool value)
    {
        if (!value) throw new InvalidOperationException("[BasicResourceEconomy] FAIL " + label);
        Debug.Log("[BasicResourceEconomy] PASS " + label);
    }

    public static void PlayMode(GameSystems systems)
    {
        BlockedPlans(systems);
        FundedPlanDoesNotNeedSurplusProduction(systems);
        ProjectedReserveDoesNotCreateProducerDemand(systems);
        MandatoryProducerCapital(systems);
        TemporaryIncome(systems);
        ReserveAndEmergency(systems);
        RecoveryHysteresis(systems);
        FeasibleAlternative(systems);
        ExistingSupplyUpgrade(systems);
        AlternateProducersAndInvestment(systems);
        OpeningProductionBeforeAssistanceExpires(systems);
        CriticalOpeningAndFundedLateWarning(systems);
        BothPerspectivesAndAP(systems);
        AdditionalResourcePlansAndReserves(systems);
        CitizenCapacityAndFood(systems);
        ChanceSupplyIsNotGuaranteed(systems);
        NeededProducerAtCatalogTail(systems);
        Debug.Log("[BasicResourceEconomy] ALL PASSED");
    }

    static void MandatoryProducerCapital(GameSystems systems)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Units.UnitDataMap.Clear(); f.Rules.aiEconomy.plannedDemandWeight = 0;
            f.Rules.aiEconomy.forecastTurns = f.Rules.aiEconomy.reserveTurns = 4;
            f.Food(20); f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 20));
            var quarry = f.Definition(FacilityKind.Quarry, stone: 6); f.Building(quarry);
            var mine = f.Definition(FacilityKind.Mine, iron: 3);
            mine.buildCost = new FacilityData.ResourceCost(wood: 45, stone: 45, water: 20, citizen: 2);
            var diversion = f.Definition(FacilityKind.Well, water: 5);
            diversion.buildCost = new FacilityData.ResourceCost(wood: 20, stone: 30);
            var consumer = f.Unit(); consumer.GrowthData.useAuthoredUpkeep = true;
            consumer.GrowthData.upkeepIron = 3; consumer.GrowthData.upkeepBread = 0;
            f.Resources.Wood = f.Resources.Bread = 100; f.Resources.Water = 60;
            f.Resources.Stone = 39; f.Resources.Iron = 8;
            var board = f.Board(20); var policy = board.Governor.BasicResources;
            var purchase = f.BuildAction(diversion, board); var repair = f.BuildAction(mine, board);
            string stock = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(team);
            Check(team + " impending iron upkeep failure needs a real unaffordable mine",
                board.Governor.ProductionDemand.Get(ResourceKind.Iron).MandatoryDemandPerTurn == 3
                && board.Governor.ProductionDemand.Get(ResourceKind.Iron).ProjectedStock < 3
                && f.Resources.Stone < mine.buildCost.Stone && policy.ActionPriority(repair) <= 2);
            Check(team + " competing foundation spending preserves the mine's actual stone capital",
                !policy.AllowSpend(purchase, false, out string reason)
                && reason == "mandatory_supply_capital_Iron_Stone");
            Check(team + " existing funded income cannot be reinvested forever before the payment deadline",
                !policy.AllowSpend(f.BuildAction(quarry, board), false, out _));
            Check(team + " relevant emergency spending remains legal", policy.AllowSpend(purchase, true, out _));
            Check(team + " reservation checks never manufacture resources or spend AP",
                stock == JsonUtility.ToJson(f.Resources) && ap == f.State.GetAP(team));
            f.Resources.Iron = 20; board.Refresh(); board.Governor.Evaluate(board);
            Check(team + " prepare supply before short cash forecast becomes critical",
                board.Governor.ProductionDemand.Get(ResourceKind.Iron).ProjectedStock >= 3
                && policy.IsPaymentDeadlineRecovery(repair));
            f.State.GetNation(team).TurnsAlive = 1; board.Refresh(); board.Governor.Evaluate(board);
            Check(team + " temporary opening support cannot hide a missing mandatory producer",
                board.Governor.ProductionDemand.TemporaryIncome(ResourceKind.Iron) > 0
                && policy.IsPaymentDeadlineRecovery(repair));
            f.State.GetNation(team).TurnsAlive = 20;
            f.Resources.Iron = 8; board.Refresh(); board.Governor.Evaluate(board);
            policy.Evaluate(board, board.Governor.ProductionDemand, false, Array.Empty<BasicResourcePlan>());
            policy.AllowSpend(purchase, false, out string manualPlanReason);
            Check(team + " explicit replacement plans clear the old mandatory investment cache",
                manualPlanReason == null || !manualPlanReason.StartsWith("mandatory_supply_capital_", StringComparison.Ordinal));
            board.Refresh(); board.Governor.Evaluate(board);
            Check(team + " a refreshed real diagnosis reinstates its one concrete producer reserve",
                !policy.AllowSpend(purchase, false, out string restoredReason)
                && restoredReason == "mandatory_supply_capital_Iron_Stone");
            // Existing quarry income can now reach 45 stone rather than being spent again
            // on an unrelated project. Execute the real build transaction before iron runs out.
            f.Economy.ProcessTurn(team); board.Refresh(); board.Governor.Evaluate(board);
            repair = f.BuildAction(mine, board);
            Check(team + " one paid turn funds the reserved producer without a resource grant",
                f.Resources.Stone == 45 && f.Resources.Iron == 5 && consumer.UpkeepUnpaidTurns == 0
                && board.Governor.AllowExecution(repair, board)
                && f.Builder.TryPlaceDefinition(Vector3Int.RoundToInt(repair.TargetPos), mine, team));
            for (int i = 0; i < 3; i++) { f.State.GetNation(team).TurnsAlive++; f.Economy.ProcessTurn(team); }
            Check(team + " producer investment sustains the actual army's upkeep",
                consumer.UpkeepUnpaidTurns == 0 && consumer.IsAlive && f.Resources.Iron == 5);
        }
    }

    static void HealthyProduction(EconomyR2Tests.Fixture f)
    {
        f.Food(20);
        f.Building(f.Definition(FacilityKind.LoggingCamp, wood: 10));
        f.Building(f.Definition(FacilityKind.Quarry, stone: 10));
        f.Rules.aiEconomy.plannedDemandWeight = 0;
    }

    static void BlockedPlans(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f);
            f.Resources.Wood = 20; f.Resources.Water = 40;
            var board = f.Board();
            var policy = new AIBasicResourceEconomy();
            var plans = new[]
            {
                new BasicResourcePlan(new FacilityData.ResourceCost(wood: 25), 1f),
                new BasicResourcePlan(new FacilityData.ResourceCost(water: 100), 4f, 2f),
                new BasicResourcePlan(new FacilityData.ResourceCost(water: 80), 3f)
            };
            string resources = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(f.Team);
            policy.Evaluate(board, board.Governor.ProductionDemand, false, plans);
            var wood = policy.Get(ResourceKind.Wood); var water = policy.Get(ResourceKind.Water);
            Check("important blocked water plans outrank a smaller wood stock", f.Resources.Wood < f.Resources.Water
                && water.BlockedPlanValue > wood.BlockedPlanValue && water.PressureScore > wood.PressureScore
                && policy.PrimaryBottleneck == ResourceKind.Water);
            Check("diagnosis and plan evaluation spend no resources or AP", resources == JsonUtility.ToJson(f.Resources)
                && ap == f.State.GetAP(f.Team));
            Check("coverage and deficit use forecast availability and planned demand", Near(water.Coverage,
                water.ProjectedAvailable / Mathf.Max(.001f, water.ProjectedDemand))
                && Near(water.Deficit, Mathf.Max(0, water.ProjectedDemand - water.ProjectedAvailable)));
        }
    }

    static void FundedPlanDoesNotNeedSurplusProduction(GameSystems systems)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Rules.aiEconomy.plannedDemandWeight = 0;
            f.Rules.aiEconomy.reserveTurns = f.Rules.aiEconomy.forecastTurns = 4;
            f.Food(20);
            f.Building(f.Definition(FacilityKind.Quarry, stone: 20));
            var camp = f.Definition(FacilityKind.LoggingCamp, wood: 42); f.Building(camp);
            var mine = f.Definition(FacilityKind.Mine, iron: 20);
            mine.buildCost = new FacilityData.ResourceCost(wood: 45, stone: 45, water: 20, citizen: 2);
            var consumer = f.Unit(upkeepWood: 10);
            consumer.GrowthData.useAuthoredUpkeep = true; consumer.GrowthData.upkeepIron = 10;
            f.Resources.Wood = 40; f.Resources.Stone = f.Resources.Water = f.Resources.Bread = 200;
            f.Resources.Iron = 100;
            var board = f.Board(60); var governor = board.Governor;
            var wood = governor.BasicResources.Get(ResourceKind.Wood);
            var surplus = f.BuildAction(camp, board);
            Check(team + " real mine investment is temporarily short of wood despite ample existing income",
                f.Resources.Wood < mine.buildCost.Wood && wood.BlockedPlanValue > 0 && wood.PressureScore > 0
                && wood.IncomePerTurn > 30 && wood.Deficit <= .001f && wood.IncomeGap <= .001f);
            Check(team + " blocked purchase pressure cannot exempt another surplus camp",
                !governor.BasicResources.IsFoundationAction(surplus)
                && !governor.BuildAssessment(surplus, board).ImprovesDeficit
                && !governor.AllowExecution(surplus, board)
                && surplus.StrategicRejectReason == "production_not_needed");
            string stocks = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(team);
            var candidates = new List<AIAction>(); AIActionGenerator.GenerateBuildCandidates(board, candidates);
            governor.Filter(candidates, board);
            Check(team + " common candidate path excludes the unneeded extra camp without spending capital",
                !candidates.Exists(action => action.FacilityDefinition == camp)
                && stocks == JsonUtility.ToJson(f.Resources) && ap == f.State.GetAP(team));

            // Change the real producer recipe, not the diagnostic snapshot: supply is now
            // genuinely below the same live unit's maintenance and the camp must be legal.
            var recipe = camp.levels[0]; recipe.Output.Wood = 2; camp.levels[0] = recipe;
            board.Refresh(); governor.Evaluate(board);
            var recovery = f.BuildAction(camp, board);
            wood = governor.BasicResources.Get(ResourceKind.Wood);
            Check(team + " actual wood shortfall still enables a necessary producer",
                wood.IncomeGap > .001f && governor.BasicResources.IsFoundationAction(recovery)
                && governor.BuildAssessment(recovery, board).ImprovesDeficit
                && governor.AllowExecution(recovery, board));
        }
    }

    static void ProjectedReserveDoesNotCreateProducerDemand(GameSystems systems)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Rules.aiEconomy.plannedDemandWeight = 0;
            f.Rules.aiEconomy.reserveTurns = f.Rules.aiEconomy.forecastTurns = 4;
            // Isolate the real missing-producer investment from the separate future-recruit
            // plan that UnitSetting's automatically supplied Scout definition would add.
            f.Units.UnitDataMap.Clear();
            var buffer = f.Rules.aiEconomy.minimumOperationalBuffer; buffer.Wood = 40;
            f.Rules.aiEconomy.minimumOperationalBuffer = buffer;
            f.Food(20); f.Building(f.Definition(FacilityKind.Quarry, stone: 20));
            var camp = f.Definition(FacilityKind.LoggingCamp, wood: 28);
            camp.buildCost = new FacilityData.ResourceCost(wood: 1000, stone: 1000);
            camp.levels = new[] { camp.levels[0], new FacilityData.FacilityLevelData {
                HP = 150, Output = new FacilityData.ProductionBundle { Wood = 10000 },
                UpgradeCost = new FacilityData.ResourceCost(wood: 150, stone: 70, water: 40, citizen: 2), UpgradeAP = 3 } };
            var producer = f.Building(camp);
            var mine = f.Definition(FacilityKind.Mine, iron: 20);
            mine.buildCost = new FacilityData.ResourceCost(wood: 45, stone: 45, water: 20, citizen: 2);
            var consumer = f.Unit(upkeepWood: 4);
            consumer.GrowthData.useAuthoredUpkeep = true; consumer.GrowthData.upkeepIron = 4;
            f.Resources.Wood = 41; f.Resources.Iron = 8;
            f.Resources.Stone = f.Resources.Water = f.Resources.Bread = 200;
            var board = f.Board(60); var governor = board.Governor;
            var wood = governor.ProductionDemand.Get(ResourceKind.Wood);
            var foundation = governor.BasicResources.Get(ResourceKind.Wood);
            float requiredMineWood = mine.buildCost.Wood * f.Rules.aiEconomy.basicResources.planWeight;
            Check(team + " existing wood production refills a temporarily low reserve despite a separate iron crisis",
                wood.Stock < wood.SafetyReserve && wood.ProjectedStock >= wood.SafetyReserve && wood.NetPerTurn > 20
                && (governor.Economy.MandatoryFailureMask & (1 << (int)ResourceKind.Iron)) != 0
                && (governor.Economy.MandatoryFailureMask & (1 << (int)ResourceKind.Wood)) == 0);
            Check(team + $" unnecessary producer upgrade cannot inflate its own planned material demand (planned={foundation.PlannedDemand}, expected={requiredMineWood}, gap={foundation.IncomeGap}, deficit={foundation.Deficit})",
                Near(foundation.PlannedDemand, requiredMineWood) && foundation.IncomeGap <= .001f
                && foundation.Deficit <= .001f);
            var upgrade = new AIAction { ActionType = AIActionType.Upgrade, Unit = producer, TargetUnit = producer,
                Facility = FacilityKind.LoggingCamp, FacilityDefinition = camp, APCost = 3 };
            Check(team + " funded projected reserve leaves capital for the actually missing producer",
                !governor.BasicResources.IsFoundationAction(upgrade) && !EconomyHelper.ImprovesProductionDemand(upgrade, board));

            var recipe = camp.levels[0]; recipe.Output.Wood = 1; camp.levels[0] = recipe;
            board.Refresh(); governor.Evaluate(board);
            Check(team + " genuinely inadequate existing production still creates a supply investment plan",
                governor.ProductionDemand.Get(ResourceKind.Wood).NetPerTurn < 0
                && governor.BasicResources.Get(ResourceKind.Wood).PlannedDemand > requiredMineWood
                && governor.BasicResources.IsFoundationAction(upgrade));

            recipe.Output.Wood = 28; camp.levels[0] = recipe;
            f.Resources.Wood = 0; f.Resources.Iron = 200;
            board.Refresh(); governor.Evaluate(board);
            wood = governor.ProductionDemand.Get(ResourceKind.Wood);
            Check(team + " later reserve recovery does not erase a real current payment failure",
                wood.ProjectedStock >= wood.SafetyReserve
                && (governor.Economy.MandatoryFailureMask & (1 << (int)ResourceKind.Wood)) != 0
                && governor.BasicResources.Get(ResourceKind.Wood).PlannedDemand > requiredMineWood
                && governor.Mode == StrategicMode.EconomicRecovery);
        }
    }

    static void TemporaryIncome(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            f.Food(20); f.State.GetNation(f.Team).TurnsAlive = 1;
            var camp = f.Definition(FacilityKind.LoggingCamp, wood: 10);
            var quarry = f.Definition(FacilityKind.Quarry, stone: 10);
            var board = f.Board(1); var policy = board.Governor.BasicResources;
            var wood = policy.Get(ResourceKind.Wood); var stone = policy.Get(ResourceKind.Stone);
            Check("temporary crystal income does not masquerade as a permanent producer", wood.ProducerCount == 0
                && stone.ProducerCount == 0 && wood.ExpectedIncome > 0 && Near(wood.IncomePerTurn, 0));
            Check("opening supply foundations are valuable before crystal assistance expires", policy.IsFoundationAction(f.BuildAction(camp, board))
                && policy.IsFoundationAction(f.BuildAction(quarry, board)) && policy.ScoreAction(f.BuildAction(camp, board)) > 0
                && policy.FoundationState != EconomyFoundationState.StableGrowth);
            f.State.GetNation(f.Team).TurnsAlive = 10; board.Refresh(); board.Governor.Evaluate(board);
            Check("expired crystal assistance contributes no forecast income", Near(board.Governor.ProductionDemand.TemporaryIncome(ResourceKind.Wood), 0));
        }
    }

    static void ReserveAndEmergency(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f);
            var buffer = f.Rules.aiEconomy.minimumOperationalBuffer; buffer.Wood = 30;
            f.Rules.aiEconomy.minimumOperationalBuffer = buffer; f.Resources.Wood = 70;
            var wall = f.Definition(FacilityKind.StoneWall); wall.buildCost = new FacilityData.ResourceCost(wood: 60);
            var board = f.Board(); var action = f.BuildAction(wall, board); var policy = board.Governor.BasicResources;
            Check("ordinary spending preserves wood reserved for the next economic action", !policy.AllowSpend(action, false, out string reason)
                && !string.IsNullOrEmpty(reason));
            string resources = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(f.Team);
            Check("essential defense may use a foundation reserve", policy.AllowSpend(action, true, out _));
            policy.NotifyEssentialSpend(action);
            Check("emergency reserve use schedules resource recovery", policy.FoundationState == EconomyFoundationState.ResourceRecovery);
            Check("spend classification alone never grants free resources", resources == JsonUtility.ToJson(f.Resources)
                && ap == f.State.GetAP(f.Team));

            var cells = f.AdjacentCells(); f.Crystal.transform.position = cells[0]; f.SetOwnCrystalPosition(cells[0]);
            f.Crystal.HP = 10; f.Crystal.DEF = 0; var opponent = f.Opponent(cells[2], 50);
            board = f.Board(); action = f.BuildAction(wall, board); action.TargetPos = cells[1];
            Check("unobserved threats do not unlock reserve exceptions", !board.Governor.AllowExecution(action, board));
            board.AlivePlayerUnits.Add(opponent); board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            action = f.BuildAction(wall, board); action.TargetPos = cells[1];
            Check("an observed lethal crystal threat permits relevant defense", board.Governor.Mode == StrategicMode.EmergencyDefense
                && board.Governor.AllowExecution(action, board));
            action.TargetPos = cells[0] + Vector3.forward * 20;
            Check("reserve defense exceptions cannot finance an unrelated remote wall", !board.Governor.AllowExecution(action, board));
        }
    }

    static BasicResourceSnapshot Snapshot(ResourceKind kind, int stock = 100, float income = 10, int producers = 1,
        float reserve = 30, float planned = 0)
        => new BasicResourceSnapshot { ResourceType = kind, CurrentStock = stock, IncomePerTurn = income,
            PermanentProductionPerTurn = Mathf.Max(0, income), ExpectedIncome = income * 4,
            ProducerCount = producers, ReserveTarget = reserve, PlannedDemand = planned };

    static void RecoveryHysteresis(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var source = new[] { Snapshot(ResourceKind.Wood), Snapshot(ResourceKind.Stone), Snapshot(ResourceKind.Water) };
            var policy = new AIBasicResourceEconomy();
            policy.EvaluateSnapshots(source, 1);
            for (int i = 0; i < 20; i++) policy.EvaluateSnapshots(source, 1);
            Check("repeated evaluations in one turn cannot complete the opening phase", policy.FoundationState == EconomyFoundationState.OpeningFoundation);
            policy.EvaluateSnapshots(source, 2);
            Check("stable supply across turns releases ordinary strategic growth", policy.FoundationState == EconomyFoundationState.StableGrowth);
            source[0] = Snapshot(ResourceKind.Wood, stock: 30, income: 0, reserve: 100);
            policy.EvaluateSnapshots(source, 3);
            Check("severe coverage loss starts resource recovery", policy.Get(ResourceKind.Wood).State == BasicResourceState.Critical
                && policy.FoundationState == EconomyFoundationState.ResourceRecovery && policy.PrimaryBottleneck == ResourceKind.Wood);
            source[0] = Snapshot(ResourceKind.Wood, stock: 70, income: 0, reserve: 100);
            policy.EvaluateSnapshots(source, 4);
            Check("recovering just above the entry threshold does not clear critical state", policy.Get(ResourceKind.Wood).State == BasicResourceState.Critical);
            source[0] = Snapshot(ResourceKind.Wood, stock: 95, income: 0, reserve: 100);
            policy.EvaluateSnapshots(source, 5);
            Check("critical coverage exits only after the higher recovery threshold", policy.Get(ResourceKind.Wood).State != BasicResourceState.Critical);
            source[0] = Snapshot(ResourceKind.Wood);
            policy.EvaluateSnapshots(source, 6); policy.EvaluateSnapshots(source, 7);
            Check("recovered stable supply eventually returns to growth", policy.FoundationState == EconomyFoundationState.StableGrowth);

            source[0] = Snapshot(ResourceKind.Wood, stock: 30, income: 0, reserve: 100);
            source[2] = Snapshot(ResourceKind.Water, stock: 31, income: 0, reserve: 100);
            policy = new AIBasicResourceEconomy(); policy.EvaluateSnapshots(source, 1);
            source[2] = Snapshot(ResourceKind.Water, stock: 29, income: 0, reserve: 100);
            policy.EvaluateSnapshots(source, 2);
            Check("small pressure changes preserve the current recovery plan", policy.PrimaryBottleneck == ResourceKind.Wood);
            var both = f.Definition(FacilityKind.LoggingCamp, wood: 4, water: 4);
            var onlyWood = f.Definition(FacilityKind.LoggingCamp, wood: 4);
            var bothAction = new AIAction { ActionType = AIActionType.Build, Facility = both.behaviourKind, FacilityDefinition = both };
            var woodAction = new AIAction { ActionType = AIActionType.Build, Facility = onlyWood.behaviourKind, FacilityDefinition = onlyWood };
            Check("a secondary critical resource still contributes to candidate value", policy.ScoreAction(bothAction) > policy.ScoreAction(woodAction));
        }
    }

    static void FeasibleAlternative(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            f.Food(20); f.Rules.aiEconomy.plannedDemandWeight = 0; f.Resources.Wood = 10;
            var blocked = f.Definition(FacilityKind.LoggingCamp, wood: 10); blocked.buildCost = new FacilityData.ResourceCost(wood: 50);
            var alternative = f.Definition(FacilityKind.LoggingCamp, wood: 8); alternative.buildCost = new FacilityData.ResourceCost(stone: 2);
            f.Catalog.buildings.Clear(); f.Catalog.buildings.Add(blocked); f.Catalog.buildings.Add(alternative);
            f.State.SetAP(f.Team, alternative.buildAP);
            var board = f.Board(); board.AffordableBuildings.Clear();
            int wood = f.Resources.Wood, ap = f.State.GetAP(f.Team); AITurnBudget.Begin(2000);
            int built = (int)typeof(AIBuildPlanner).GetMethod("TryScoreBuildPhase", EconomyR2Tests.Fixture.Private)
                .Invoke(f.Planner(), new object[] { board, TurnStrategy.EconomyBuild, 20 });
            int purchasedAlternatives = 0, purchasedBlocked = 0;
            foreach (var building in f.Buildings)
            {
                if (building.AuthoredFacility == alternative) purchasedAlternatives++;
                if (building.AuthoredFacility == blocked) purchasedBlocked++;
            }
            Check("deadlock chooses an affordable authored supply path instead of retrying an impossible camp", built > 0
                && purchasedAlternatives > 0 && purchasedBlocked == 0);
            Check("alternative construction consumes its actual resources and AP", f.Resources.Wood == wood
                && f.State.GetAP(f.Team) == ap - purchasedAlternatives * alternative.buildAP);
            f.Economy.ProcessTurn(f.Team);
            Check("alternative supply is earned through the real production system", f.Resources.Wood == wood
                + purchasedAlternatives * alternative.GetLevel(1).Output.Wood);
        }
    }

    static void ExistingSupplyUpgrade(GameSystems systems)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Food(20); f.Rules.aiEconomy.plannedDemandWeight = 0; f.Resources.Wood = 80;
            f.Building(f.Definition(FacilityKind.Quarry, stone: 10));
            var camp = f.Definition(FacilityKind.LoggingCamp, wood: 5);
            camp.levels = new[] { camp.levels[0], new FacilityData.FacilityLevelData {
                HP = 150, Output = new FacilityData.ProductionBundle { Wood = 20 },
                UpgradeCost = new FacilityData.ResourceCost(wood: 3, water: 2), UpgradeAP = 3 } };
            var building = f.Building(camp); f.Unit(upkeepWood: 10); f.State.SetAP(team, 3);
            var board = f.Board(); board.BuildablePositions.Clear();
            var candidates = new List<AIAction>(); AIActionGenerator.GenerateUpgradeCandidates(board, candidates);
            var upgrade = candidates.Find(action => action.Unit == building);
            Check(team + " existing supply upgrades remain possible without vacant construction tiles", upgrade != null
                && upgrade.ActionType == AIActionType.Upgrade && board.Governor.BasicResources.IsFoundationAction(upgrade));
            int wood = f.Resources.Wood, water = f.Resources.Water, generation = board.Generation;
            var executor = (AIActionExecutor)typeof(AIBuildPlanner).GetField("_executor", EconomyR2Tests.Fixture.Private).GetValue(f.Planner());
            Check(team + " supply upgrade executes through the actual building transaction", executor.ExecuteUpgrade(upgrade, board));
            Check(team + " upgrade consumes authored resource and AP costs once", building.Level == 2
                && f.Resources.Wood == wood - 3 && f.Resources.Water == water - 2 && f.State.GetAP(team) == 0);
            Check(team + " upgrade refreshes exact permanent production instead of adding a duplicate recipe", board.Generation > generation
                && Near(board.Governor.ProductionDemand.PermanentProduction(ResourceKind.Wood), 20));
            string resources = JsonUtility.ToJson(f.Resources); building.team = team == Team.Player ? Team.Enemy : Team.Player;
            Check(team + " the same action cannot upgrade an opponent facility", !executor.ExecuteUpgrade(upgrade, board)
                && resources == JsonUtility.ToJson(f.Resources));
            building.team = team;
        }
    }

    static void AlternateProducersAndInvestment(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            f.Food(20); f.Rules.aiEconomy.plannedDemandWeight = 0;
            f.Building(f.Definition(FacilityKind.Field, wood: 20, stone: 20));
            var camp = f.Definition(FacilityKind.LoggingCamp, wood: 10); var board = f.Board();
            var policy = board.Governor.BasicResources; var action = f.BuildAction(camp, board);
            Check("authored alternative recipes count as permanent supply without a fixed camp requirement",
                board.GetBuildingCount(FacilityKind.LoggingCamp) == 0 && policy.Get(ResourceKind.Wood).ProducerCount > 0
                && !policy.IsFoundationAction(action));
            float unnecessary = policy.ScoreAction(action);
            foreach (var building in f.Buildings) if (building.facilityKind == FacilityKind.Field && building.AuthoredFacility.GetLevel(1).Output.Wood > 0) building.HP = 0;
            f.Resources.Wood = 120; f.Unit(upkeepWood: 10); board.Refresh(); board.Governor.Evaluate(board);
            var cheap = f.Definition(FacilityKind.LoggingCamp, wood: 8); cheap.buildCost = new FacilityData.ResourceCost(stone: 2);
            var slow = f.Definition(FacilityKind.LoggingCamp, wood: 2); slow.buildCost = new FacilityData.ResourceCost(wood: 100);
            board.Refresh(); board.Governor.Evaluate(board); policy = board.Governor.BasicResources;
            float cheapScore = policy.ScoreAction(f.BuildAction(cheap, board)), slowScore = policy.ScoreAction(f.BuildAction(slow, board));
            Check("critical supply is more valuable than a surplus producer", cheapScore > unnecessary);
            Check("production investment accounts for cost and improvement instead of unconditional output bonuses", cheapScore > slowScore);
        }
    }

    static void BothPerspectivesAndAP(GameSystems systems)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            f.Food(20); f.Resources.Wood = 10; f.Resources.Stone = 30; f.Resources.Water = 40;
            var camp = f.Definition(FacilityKind.LoggingCamp, wood: 10); var board = f.Board();
            Check(team + " foundation diagnosis uses actual actor resources", board.ActorTeam == team
                && board.Governor.BasicResources.Get(ResourceKind.Wood).CurrentStock == f.Resources.Wood);
            int ap = f.State.GetAP(team), buildings = f.Buildings.Count; string resources = JsonUtility.ToJson(f.Resources);
            AITurnBudget.Begin(0); int built = f.Planner().TryEarlyBuildPhase(board, TurnStrategy.EconomyBuild, 20);
            Check(team + " expired turn budget cannot spend foundation resources", built == 0 && f.Buildings.Count == buildings
                && f.State.GetAP(team) == ap && resources == JsonUtility.ToJson(f.Resources));
            var action = f.BuildAction(camp, board); var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) board.Governor.BasicResources.ScoreAction(action);
            watch.Stop();
            Check(team + " cached three-resource candidate assessment remains bounded", watch.Elapsed.TotalMilliseconds < 1000);
            Debug.Log($"[BasicResourceEconomy] actor={team} candidate1000Ms={watch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    static void OpeningProductionBeforeAssistanceExpires(GameSystems systems)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            // A supplied bread chain isolates foundation investment; wood/stone/water use ordinary game recipes and costs.
            f.Building(f.Definition(FacilityKind.Bakery, bread: 20));
            f.Resources.Wood = 70; f.Resources.Stone = 60; f.Resources.Water = 30;
            var governor = new AIStrategicGovernor(); var planner = f.Planner(); int woodTurn = 0, stoneTurn = 0, waterTurn = 0;
            var watch = Stopwatch.StartNew();
            for (int turn = 1; turn <= 16; turn++)
            {
                f.State.GetNation(team).TurnsAlive = turn; f.State.ResetAPForTurn(team);
                var board = f.Board(turn); board.Governor = governor; governor.Evaluate(board); AITurnBudget.Begin(1000);
                int built = planner.TryEarlyBuildPhase(board, TurnStrategy.EconomyBuild, turn);
                planner.TryLateBuildPhase(board, built, turn); f.Economy.ProcessTurn(team);
                board.Refresh(); governor.Evaluate(board);
                var demand = governor.ProductionDemand;
                if (woodTurn == 0 && demand.PermanentProduction(ResourceKind.Wood) > 0) woodTurn = turn;
                if (stoneTurn == 0 && demand.PermanentProduction(ResourceKind.Stone) > 0) stoneTurn = turn;
                if (waterTurn == 0 && demand.PermanentProduction(ResourceKind.Water) > 0) waterTurn = turn;
                Check(team + " foundation turn " + turn + " meets mandatory payments", !demand.HasMandatoryPaymentFailure
                    && f.Resources.Wood >= 0 && f.Resources.Stone >= 0 && f.Resources.Water >= 0);
                if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("Basic-resource sixteen-turn fixture exceeded its bounded runtime");
            }
            Check(team + " affordable permanent supply is established before temporary crystal income expires", woodTurn > 0
                && woodTurn < 10 && stoneTurn > 0 && stoneTurn < 10 && waterTurn > 0 && waterTurn < 10);
            Check(team + " real economy stays solvent after assistance expires", f.Resources.Wood > 0
                && f.Resources.Stone > 0 && f.Resources.Water > 0);
            Debug.Log($"[BasicResourceEconomy] opening actor={team} turns=16 firstWood={woodTurn} firstStone={stoneTurn} firstWater={waterTurn} finalWood={f.Resources.Wood} finalStone={f.Resources.Stone} finalWater={f.Resources.Water} wallMs={watch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    static void CriticalOpeningAndFundedLateWarning(GameSystems systems)
    {
        // Use the same rule values, facility overrides and recruitment costs as the actual match.
        // The general economy fixture intentionally replaces these with cheap legacy recipes.
        string liveRules = JsonUtility.ToJson(GameAuthoringRules.Active);
        var liveFacilities = new List<FacilityDefinitionData>(FacilityAuthoringCatalog.Loaded.standardOverrides);
        var liveUnits = new Dictionary<Kind, UnitData>(systems.UnitSetting.UnitDataMap);
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            using (var f = new EconomyR2Tests.Fixture(systems, team))
            {
                JsonUtility.FromJsonOverwrite(liveRules, f.Rules);
                f.Catalog.standardOverrides = new List<FacilityDefinitionData>(liveFacilities);
                typeof(UnitSetting).GetProperty("UnitDataMap").SetValue(f.Units, new Dictionary<Kind, UnitData>(liveUnits));
                GameContentRevision.NotifyChanged();
                f.Resources.Wood = 140; f.Resources.Stone = 130; f.Resources.Water = 60;
                f.Resources.Iron = 30; f.Resources.MagicOre = 12; f.Resources.Wheat = 0; f.Resources.Bread = 100;
                f.State.GetNation(team).TurnsAlive = 1;
                foreach (var kind in new[] { Kind.Knight, Kind.Archer, Kind.Scout })
                {
                    var unit = f.Unit(); unit.kind = kind; unit.Level = 1; unit.GrowthData = liveUnits[kind];
                }
                var board = f.Board(1);
                foreach (var resource in board.Governor.BasicResources.Resources)
                    Debug.Log("[BasicResourceOpening] " + team + " " + resource);
                // R2 no longer invents an extra Scout when the role is already filled. The old
                // Critical flag depended on that hypothetical unit's 0.8 citizen demand. Assert
                // the real invariant: current food obligations need a reachable permanent chain.
                Check(team + " initial capital and temporary support do not erase real food obligations",
                    board.Governor.BasicResources.HasRequiredFoodSupplyGap
                    && board.ProductionDemand.Get(ResourceKind.Bread).MandatoryDemandPerTurn
                        > board.ProductionDemand.PermanentProduction(ResourceKind.Bread));
                var actions = new List<AIAction>(); AIActionGenerator.GenerateBuildCandidates(board, actions); board.Governor.Filter(actions, board);
                bool supplyPriority = false, foodPriority = false;
                foreach (var action in actions)
                {
                    if (board.Governor.BasicResources.IsFoundationAction(action) && action.StrategicPriority <= 3) supplyPriority = true;
                    if (board.Governor.BasicResources.IsRequiredFoodSupplyAction(action) && action.StrategicPriority <= 3) foodPriority = true;
                }
                Check(team + " food supply can compete with movement before optional expansion", foodPriority);
                Check(team + " a feasible opening producer retains a strategic priority before payment failure", supplyPriority);
            }
            using (var f = new EconomyR2Tests.Fixture(systems, team))
            {
                HealthyProduction(f); var unit = f.Unit(); unit.GrowthData.upkeepMagic = 1; f.Resources.MagicOre = 30;
                var mine = f.Definition(FacilityKind.Mine); var recipe = mine.levels[0];
                recipe.BonusChance1 = .25f; recipe.BonusOutput1.MagicOre = 8; mine.levels[0] = recipe; f.Building(mine);
                f.State.GetNation(team).TurnsAlive = 60;
                var board = f.Board(60);
                Check(team + " late probabilistic magic income remains a guaranteed-supply warning", board.Governor.Economy.State == EconomicState.Warning
                    && !board.Governor.Economy.UpkeepPaymentFailed);
                Check(team + " funded late magic warning does not restore permanent economic recovery", !board.Governor.BasicResources.HasCriticalDeficit
                    && board.Governor.Mode != StrategicMode.EconomicRecovery);
            }
        }
    }

    static bool Near(float a, float b) => Mathf.Abs(a - b) < .05f;

    static void SetStock(FactionState.ResourceData resources, ResourceKind kind, int amount)
    {
        switch (kind)
        {
            case ResourceKind.Wood: resources.Wood = amount; break;
            case ResourceKind.Stone: resources.Stone = amount; break;
            case ResourceKind.Iron: resources.Iron = amount; break;
            case ResourceKind.MagicOre: resources.MagicOre = amount; break;
            case ResourceKind.Wheat: resources.Wheat = amount; break;
            case ResourceKind.Bread: resources.Bread = amount; break;
            case ResourceKind.Water: resources.Water = amount; break;
            case ResourceKind.Citizen: resources.Citizen = amount; break;
        }
    }

    static FacilityData.ProductionBundle Bundle(ResourceKind kind, int amount)
    {
        var value = new FacilityData.ProductionBundle();
        switch (kind)
        {
            case ResourceKind.Iron: value.Iron = amount; break;
            case ResourceKind.MagicOre: value.MagicOre = amount; break;
            case ResourceKind.Wheat: value.Wheat = amount; break;
            case ResourceKind.Bread: value.Bread = amount; break;
            case ResourceKind.Citizen: value.Citizen = amount; break;
        }
        return value;
    }

    static void AdditionalResourcePlansAndReserves(GameSystems systems)
    {
        foreach (var resource in new[] { ResourceKind.Iron, ResourceKind.MagicOre, ResourceKind.Wheat, ResourceKind.Bread, ResourceKind.Citizen })
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); SetStock(f.Resources, resource, resource == ResourceKind.Citizen ? 2 : 5);
            var board = f.Board(); var policy = new AIBasicResourceEconomy();
            var planned = new BasicResourcePlan(Bundle(resource, resource == ResourceKind.Citizen ? 12 : 120), 4f, 2f);
            string resources = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(f.Team);
            policy.Evaluate(board, board.Governor.ProductionDemand, false, new[] { planned });
            var diagnosis = policy.Get(resource);
            Check(resource + " participates in blocked-plan pressure beyond wood, stone and water", diagnosis != null
                && diagnosis.PlannedDemand > 0 && diagnosis.BlockedPlanValue > 0 && diagnosis.Deficit > 0
                && diagnosis.PressureScore > 0 && diagnosis.State <= BasicResourceState.Shortage
                && policy.PrimaryBottleneck == resource);
            Check(resource + " expanded diagnosis never grants resources or spends AP", resources == JsonUtility.ToJson(f.Resources)
                && ap == f.State.GetAP(f.Team));
        }

        foreach (var resource in new[] { ResourceKind.Iron, ResourceKind.MagicOre, ResourceKind.Bread, ResourceKind.Citizen })
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Building(f.Definition(FacilityKind.Mine, iron: 20, magic: 20));
            var reserve = f.Rules.aiEconomy.basicResources.minimumReserve;
            reserve.Iron = 6; reserve.MagicOre = 6; reserve.Bread = 10; reserve.Citizen = 4;
            f.Rules.aiEconomy.basicResources.minimumReserve = reserve;
            SetStock(f.Resources, resource, resource == ResourceKind.Citizen ? 5 : resource == ResourceKind.Bread ? 12 : 8);
            var unit = f.UnitDefinition();
            unit.costWood = unit.costStone = unit.costIron = unit.costMagic = unit.costBread = unit.costWater = unit.costCitizen = 0;
            if (resource == ResourceKind.Iron) unit.costIron = 4;
            if (resource == ResourceKind.MagicOre) unit.costMagic = 4;
            if (resource == ResourceKind.Bread) unit.costBread = 6;
            if (resource == ResourceKind.Citizen) unit.costCitizen = 2;
            var board = f.Board(); var policy = new AIBasicResourceEconomy();
            policy.Evaluate(board, board.Governor.ProductionDemand, false, Array.Empty<BasicResourcePlan>());
            var action = new AIAction { ActionType = AIActionType.Summon, SummonKind = unit.kind, SummonDefinition = unit };
            Check(resource + " reserves constrain an ordinary unit purchase", !policy.AllowSpend(action, false, out string reason)
                && reason == "basic_resource_reserve_" + resource);
            Check(resource + " essential defense may use the same reserve", policy.AllowSpend(action, true, out _));
        }

        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Resources.Wheat = 5;
            foreach (var actor in f.Buildings) if (actor.facilityKind == FacilityKind.Field) actor.HP = 0;
            var bakery = f.Definition(FacilityKind.Bakery, bread: 10); var recipe = bakery.levels[0];
            recipe.Input.Wheat = 10; bakery.levels[0] = recipe; var board = f.Board();
            string resources = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(f.Team);
            var action = f.BuildAction(bakery, board);
            Check("unfunded wheat inputs cannot be promised by a new bread producer", !board.Governor.AllowExecution(action, board)
                && action.StrategicRejectReason == "build_causes_forecast_failure");
            Check("rejecting wheat input failure leaves the real economy untouched", resources == JsonUtility.ToJson(f.Resources)
                && ap == f.State.GetAP(f.Team));
        }
    }

    static void CitizenCapacityAndFood(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Resources.Citizen = 2; f.Resources.Bread = 10;
            var board = f.Board(); var exactPopulation = board.Governor.ProductionDemand.Get(ResourceKind.Citizen);
            // Foundation availability also counts replenishment after planned citizen purchases.
            // With no purchases, both the conservative forecast and opportunity model must respect the present cap.
            var growthOnly = new AIBasicResourceEconomy();
            growthOnly.Evaluate(board, board.Governor.ProductionDemand, false, Array.Empty<BasicResourcePlan>());
            var citizen = growthOnly.Get(ResourceKind.Citizen);
            Check("citizen prediction respects available capacity and food", citizen != null && citizen.CurrentStock == 2
                && citizen.ProjectedAvailable > 2 && citizen.ProjectedAvailable <= FactionState.BaseCitizenCap
                && exactPopulation.ProjectedStock > 2 && exactPopulation.ProjectedStock <= FactionState.BaseCitizenCap);
            int bread = f.Resources.Bread; f.Economy.ProcessTurn(f.Team);
            Check("real citizen growth pays its bread and population food costs", f.Resources.Citizen == 3
                && f.Resources.Bread == bread + 20 - 1 - 3 * EconomySystem.CitizenBreadCost);
        }

        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Resources.Citizen = FactionState.BaseCitizenCap; f.Resources.Bread = 80;
            var house = f.Definition(FacilityKind.House); var recipe = house.levels[0]; recipe.SpecialValue = 3; house.levels[0] = recipe;
            var board = f.Board(); var plans = new[] { new BasicResourcePlan(new FacilityData.ProductionBundle { Citizen = 8 }, 4f, 2f) };
            board.Governor.BasicResources.Evaluate(board, board.Governor.ProductionDemand, false, plans);
            var action = f.BuildAction(house, board);
            Check("a supplied house relieves a real capacity-limited citizen plan", board.Governor.BasicResources.IsFoundationAction(action)
                && board.Governor.BasicResources.ScoreAction(action) > 0);
            int wood = f.Resources.Wood, stone = f.Resources.Stone, ap = f.State.GetAP(f.Team);
            var executor = (AIActionExecutor)typeof(AIBuildPlanner).GetField("_executor", EconomyR2Tests.Fixture.Private).GetValue(f.Planner());
            Check("capacity recovery uses a real house purchase", executor.ExecuteBuild(action, board));
            Check("house construction pays authored costs without creating citizens immediately", f.Resources.Wood == wood - house.buildCost.Wood
                && f.Resources.Stone == stone - house.buildCost.Stone && f.State.GetAP(f.Team) == ap - house.buildAP
                && f.Resources.Citizen == FactionState.BaseCitizenCap);
            int bread = f.Resources.Bread; f.Economy.ProcessTurn(f.Team);
            Check("expanded capacity admits one paid citizen on the actual economy turn", f.State.GetCitizenCap(f.Team) == FactionState.BaseCitizenCap + 3
                && f.Resources.Citizen == FactionState.BaseCitizenCap + 1
                && f.Resources.Bread == bread + 20 - 1 - f.Resources.Citizen * EconomySystem.CitizenBreadCost);
        }

        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Resources.Bread = 0;
            foreach (var actor in f.Buildings) if (actor.facilityKind == FacilityKind.Bakery) actor.HP = 0;
            var house = f.Definition(FacilityKind.House); var recipe = house.levels[0]; recipe.SpecialValue = 3; house.levels[0] = recipe;
            var board = f.Board(); board.Governor.BasicResources.Evaluate(board, board.Governor.ProductionDemand, false,
                new[] { new BasicResourcePlan(new FacilityData.ProductionBundle { Citizen = 8 }, 4f, 2f) });
            Check("empty bread stocks prevent fictitious house citizen output", !board.Governor.BasicResources.IsFoundationAction(f.BuildAction(house, board)));
            f.Building(house); int citizens = f.Resources.Citizen; f.Economy.ProcessTurn(f.Team);
            Check("additional housing alone cannot grow an unfed population", f.Resources.Citizen <= citizens && f.Resources.Bread == 0);
        }
    }

    static void ChanceSupplyIsNotGuaranteed(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Resources.MagicOre = 0;
            var mine = f.Definition(FacilityKind.Mine, iron: 10); var recipe = mine.levels[0];
            recipe.BonusChance1 = .25f; recipe.BonusOutput1.MagicOre = 8; mine.levels[0] = recipe; f.Building(mine);
            var board = f.Board(); var demand = board.Governor.ProductionDemand;
            Check("chance-only magic ore remains a recognized supply opportunity", demand.PotentialSourceCount(ResourceKind.MagicOre) > 0
                && Near(demand.PotentialProduction(ResourceKind.MagicOre), 2));
            Check("chance supply is separate from guaranteed permanent income", Near(demand.PermanentProduction(ResourceKind.MagicOre), 0)
                && Near(demand.Get(ResourceKind.MagicOre).ProductionPerTurn, 0));
            var unit = f.Unit(); unit.GrowthData.upkeepMagic = 1; board.Refresh(); board.Governor.Evaluate(board);
            Check("chance-only magic ore cannot finance guaranteed military maintenance", board.Governor.Economy.UpkeepPaymentFailed
                && (board.Governor.Economy.MandatoryFailureMask & (1 << (int)ResourceKind.MagicOre)) != 0);
        }
    }

    static void NeededProducerAtCatalogTail(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            HealthyProduction(f); f.Resources.MagicOre = 0; var unit = f.Unit(); unit.GrowthData.upkeepMagic = 1;
            f.Catalog.buildings.Clear();
            for (int i = 0; i < 24; i++) f.Definition(FacilityKind.LoggingCamp, wood: 5);
            var supply = f.Definition(FacilityKind.Mine, iron: 10); var recipe = supply.levels[0];
            recipe.BonusChance1 = .25f; recipe.BonusOutput1.MagicOre = 8; supply.levels[0] = recipe;
            var board = f.Board(); board.AffordableBuildings.Clear(); var actions = new List<AIAction>();
            AITurnBudget.Begin(2000); AIActionGenerator.GenerateBuildCandidates(board, actions);
            Check("needed chance supply at the catalog tail survives the definition bound", actions.Exists(action => action.FacilityDefinition == supply));
            Check("large authored catalogs still bound construction placement candidates", actions.Count <= 32);
            board.Governor.Filter(actions, board);
            Check("existing mandatory deficits may invest in a legal chance-based recovery path", actions.Exists(action => action.FacilityDefinition == supply));
        }
    }
}
