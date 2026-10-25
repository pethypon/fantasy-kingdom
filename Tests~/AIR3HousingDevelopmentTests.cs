#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>P0-C behavioral repros. Missing new authoring fields are ignored by JsonUtility on the baseline,
/// so Red is a failed housing/recruitment behavior rather than a compilation failure.</summary>
public static class AIR3HousingDevelopmentTests
{
    static int passed, failed;
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[AIR3Housing] FAIL " + name); passed++; Debug.Log("[AIR3Housing] PASS " + name); }
    static void Case(string label, Action test)
    { try { test(); } catch (Exception e) { failed++; Debug.Log("[AIR3HousingCase] FAIL " + label + " " + e.Message); } }
    public static void Run(GameSystems live, TurnGenerator turn)
    {
        passed = failed = 0;
        var active = typeof(AIHousingConfig).GetField("active", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = active.GetValue(null);
        var config = ScriptableObject.CreateInstance<AIHousingConfig>(); config.ShortTermCombatRecruits = 0;
        active.SetValue(null, config);
        try
        {
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                Case(team + " HousingDemand20To100", () => FutureDemand(live, turn, team, config));
                Case(team + " HousingNoDemand", () => NoDemand(live, turn, team, config));
                Case(team + " HousingRecovery", () => Recovery(live, turn, team, config));
                Case(team + " HousingCandidateNotDropped", () => CandidateBudget(live, turn, team, config));
                Case(team + " RecruitmentFollowsHousing", () => PaidChain(live, turn, team, config));
                Case(team + " HousingDevelopment80EconomyTurns", () => LongEconomyChain(live, turn, team, config));
                Case(team + " FundedCitizenGrowthDoesNotHaltArmy", () => CitizenGrowthPlans(live, team));
            }
        }
        finally { active.SetValue(null, previous); UnityEngine.Object.DestroyImmediate(config); }
        Debug.Log("[AIR3Housing] checks=" + passed + " failedCases=" + failed);
        if (failed != 0) throw new InvalidOperationException("[AIR3Housing] " + failed + " failed cases");
    }
    static void Configure(AIHousingConfig config, int due)
    {
        JsonUtility.FromJsonOverwrite("{\"EnableDevelopmentGrowth\":false,\"DevelopmentPlanningTurns\":40,\"DevelopmentRoster\":[{\"Kind\":"
            + (int)Kind.WhiteMage + ",\"DesiredCount\":1,\"NeededFromTurn\":" + due + "}]}", config);
        GameContentRevision.NotifyChanged();
    }
    static UnitData Prepare(AIR2RecruitmentTests.Fixture f, int cost)
    {
        foreach (var actor in f.OwnActors()) actor.HP = actor.MaxHP;
        var healer = f.EconomyFixture.Unit(); healer.kind = Kind.Priest; healer.GrowthData = f.Healer;
        healer.HP = healer.MaxHP; healer.HasMovedThisTurn = true;
        return f.AddDefinition(Kind.WhiteMage, cost, 10);
    }
    static void FutureDemand(GameSystems live, TurnGenerator turn, Team team, AIHousingConfig config)
    {
        Configure(config, 60);
        using var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1); Prepare(f, 10);
        foreach (int ownTurn in new[] { 20, 40, 60, 80, 100 })
        {
            var board = f.Board(ownTurn); var action = f.EconomyFixture.BuildAction(f.House, board);
            var housing = board.Governor.HousingAssessment(action, board);
            Debug.Log("[AIR3HousingDemand] " + team + " T=" + ownTurn + " " + housing.Describe());
            Check(team + " future capacity requirement survives outside a three-purchase window at " + ownTurn,
                housing.Needed && housing.CapacityBlocked && housing.RequiredStock >= 13);
        }
    }
    static void NoDemand(GameSystems live, TurnGenerator turn, Team team, AIHousingConfig config)
    {
        Configure(config, 60);
        using var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1); Prepare(f, 2);
        var board = f.Board(40); var action = f.EconomyFixture.BuildAction(f.House, board);
        Check(team + " sequential small purchases do not manufacture simultaneous population demand",
            !board.Governor.HousingAssessment(action, board).Needed);
    }
    static void Recovery(GameSystems live, TurnGenerator turn, Team team, AIHousingConfig config)
    {
        Configure(config, 20);
        using var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1); Prepare(f, 6);
        f.Resources.Bread = 1; var food = f.EconomyFixture.Buildings.Where(a => a.facilityKind == FacilityKind.Bakery).ToList();
        foreach (var producer in food) producer.HP = 0;
        var board = f.Board(40); var action = f.EconomyFixture.BuildAction(f.House, board);
        var before = board.Governor.HousingAssessment(action, board);
        Check(team + " actual food failure postpones an otherwise real capacity investment", before.CapacityBlocked && !before.Needed
            && before.Reason == "housing_supply_recovery_first");
        foreach (var producer in food) producer.HP = producer.MaxHP;
        f.Resources.Bread = 400; board = f.Board(41);
        Check(team + " restored food resumes the same required capacity investment", board.Governor.HousingAssessment(action, board).Needed);
    }
    static void CandidateBudget(GameSystems live, TurnGenerator turn, Team team, AIHousingConfig config)
    {
        Configure(config, 20);
        using var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1); Prepare(f, 1);
        f.Resources.Citizen = 0;
        var consumer = f.EconomyFixture.Unit(upkeepWood: 35); consumer.HasMovedThisTurn = true;
        for (int i = 0; i < 20; i++)
        {
            var alternative = f.EconomyFixture.Definition(FacilityKind.LoggingCamp, wood: 10);
            alternative.buildCost = new FacilityData.ResourceCost(wood: 2); alternative.buildAP = 2;
        }
        f.EconomyFixture.Catalog.buildings.Remove(f.House); f.EconomyFixture.Catalog.buildings.Add(f.House);
        GameContentRevision.NotifyChanged();
        var board = f.Board(40); board.OperationRequiredCitizenDemand = 20;
        var needed = f.EconomyFixture.BuildAction(f.House, board);
        Check(team + " candidate-budget fixture contains a real safe housing requirement", board.Governor.HousingAssessment(needed, board).Needed);
        var actions = new List<AIAction>(); AITurnBudget.Begin(3000); AIActionGenerator.GenerateBuildCandidates(board, actions);
        Check(team + " required authored house survives the sixteen-definition shortlist", actions.Any(a => a.FacilityDefinition == f.House));
        Check(team + " candidate rescue preserves the bounded sixteen-definition budget",
            actions.Where(a => a.FacilityDefinition != null).Select(a => a.FacilityDefinition).Distinct().Count() <= 16);
    }
    static void PaidChain(GameSystems live, TurnGenerator turn, Team team, AIHousingConfig config)
    {
        Configure(config, 20);
        using var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1); var wanted = Prepare(f, 6);
        var board = f.Board(20); var action = f.EconomyFixture.BuildAction(f.House, board);
        var candidates = AIActionEvaluator.EvaluateAll(f.Commander.Personality, board, null, TurnStrategy.Balanced);
        var house = candidates.Find(a => a.FacilityDefinition == f.House);
        Check(team + " real future capacity request reaches standard housing candidates", house != null
            && board.Governor.HousingAssessment(house, board).Needed);
        int citizens = f.Resources.Citizen, wood = f.Resources.Wood, ap = f.State.GetAP(team);
        var selected = f.Select(board, new List<AIAction> { house, new AIAction { ActionType = AIActionType.Wait, Score = 10000 } });
        Check(team + " selected house is really charged without granting population", selected == house && f.Executor.Execute(house, board)
            && f.Resources.Citizen == citizens && f.Resources.Wood == wood - f.House.buildCost.Wood && f.State.GetAP(team) == ap - f.House.buildAP);
        for (int i = 1; i <= 4; i++)
        {
            f.EconomyFixture.Economy.ProcessTurn(team);
            Check(team + " real city replenishment remains one per turn " + i, f.Resources.Citizen == citizens + i);
        }
        f.State.SetAP(team, 40); board = f.Board(24);
        candidates = AIActionEvaluator.EvaluateAll(f.Commander.Personality, board, null, TurnStrategy.Balanced);
        var recruit = candidates.Find(a => a.ActionType == AIActionType.Summon && a.SummonDefinition == wanted);
        Check(team + " the promised role becomes a legal strategic recruit after natural growth", recruit != null && recruit.StrategicPriority <= 3);
        int before = f.OwnActors().Count; citizens = f.Resources.Citizen; ap = f.State.GetAP(team);
        selected = f.Select(board, new List<AIAction> { recruit, new AIAction { ActionType = AIActionType.Wait, Score = 10000 } });
        Check(team + " standard selection and execution fulfil the prepared future role", selected == recruit && f.Executor.Execute(recruit, board)
            && f.OwnActors().Count == before + 1 && f.Resources.Citizen == citizens - wanted.costCitizen && f.State.GetAP(team) == ap - wanted.costAP);
        board = f.Board(25);
        Check(team + " a fulfilled roster does not repeatedly request more houses", !board.Governor.HousingAssessment(action, board).Needed);
    }

    // Executes eighty real economy transactions. Combat, enemy turns and pathfinding are
    // deliberately outside this isolated test; its controlled casualty is not a battle result.
    static void LongEconomyChain(GameSystems live, TurnGenerator turn, Team team, AIHousingConfig config)
    {
        Configure(config, 60);
        config.DevelopmentRoster = new[]
        {
            new AIArmyDevelopmentTarget { Kind = Kind.WhiteMage, DesiredCount = 1, NeededFromTurn = 60 },
            new AIArmyDevelopmentTarget { Kind = Kind.Guardian, DesiredCount = 1, NeededFromTurn = 80 }
        };
        using var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1);
        var healer = Prepare(f, 10); var defender = f.AddDefinition(Kind.Guardian, 16, 8);
        GameContentRevision.NotifyChanged();
        int houses = 0, recruits = 0, processes = 0, naturalGrowth = 0;
        var houseRequest = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.House,
            FacilityDefinition = f.House, APCost = f.House.buildAP };
        BuildPaidHouse(f, f.Board(20), team, 20); houses++;
        Check(team + " first future purchase expands actual capacity five to thirteen without population grant",
            f.State.GetCitizenCap(team) == 13 && f.Resources.Citizen == 5);
        for (int ownTurn = 21; ownTurn <= 100; ownTurn++)
        {
            int population = f.Resources.Citizen, capacity = f.State.GetCitizenCap(team);
            // Mirrors the production clock advance used by TurnStartHelper, without running
            // unrelated skill ticks or timers against the live scene.
            f.State.GetNation(team).TurnsAlive++;
            f.EconomyFixture.Economy.ProcessTurn(team); processes++;
            int expectedGrowth = population < capacity ? 1 : 0;
            if (f.Resources.Citizen != population + expectedGrowth)
                throw new InvalidOperationException("[AIR3Housing] " + team + " actual population growth differs from one-per-turn at " + ownTurn);
            naturalGrowth += expectedGrowth;
            if (f.State.GetNation(team).StarvationCounter != 0 || f.OwnActors().Any(a => a.UpkeepUnpaidTurns != 0))
                throw new InvalidOperationException("[AIR3Housing] " + team + " real maintenance or food payment failed at " + ownTurn);
            f.State.SetAP(team, 40);
            var board = f.Board(ownTurn);
            var assessment = board.Governor.HousingAssessment(houseRequest, board);
            if (ownTurn == 40)
            {
                Check(team + " second future role enters its authored preparation horizon at forty", assessment.Needed
                    && assessment.CapacityBlocked && assessment.RequiredStock >= 19);
                BuildPaidHouse(f, board, team, ownTurn); houses++;
                Check(team + " second paid house raises real capacity thirteen to twenty-one", f.State.GetCitizenCap(team) == 21);
                board = f.Board(ownTurn);
            }
            else if (assessment.Needed)
                throw new InvalidOperationException("[AIR3Housing] " + team + " redundant capacity investment requested at " + ownTurn + " " + assessment.Describe());
            if (ownTurn == 60) { RecruitPaid(f, board, healer, team, ownTurn); recruits++; }
            if (ownTurn == 80)
            {
                CheckPlannedCitizenReserve(f, board, defender, team);
                RecruitPaid(f, board, defender, team, ownTurn); recruits++;
            }
            if (ownTurn == 90)
            {
                var casualty = f.OwnActors().Single(a => a.GrowthData == healer);
                casualty.HP = 0; casualty.gameObject.SetActive(false);
                board = f.Board(ownTurn);
                Check(team + " controlled own loss restores the still-authored future role requirement",
                    board.Governor.DevelopmentPlan.Any(n => n.Definition == healer && n.MissingCount == 1));
                RecruitPaid(f, board, healer, team, ownTurn); recruits++;
            }
            if (ownTurn % 20 == 0 || ownTurn == 90)
                Debug.Log("[AIR3HousingEconomy] team=" + team + " turn=" + ownTurn + " transactions=" + processes
                    + " citizens=" + f.Resources.Citizen + "/" + f.State.GetCitizenCap(team)
                    + " houses=" + houses + " paidRecruits=" + recruits + " resources=" + JsonUtility.ToJson(f.Resources));
        }
        Check(team + " eighty actual economy turns retain affordable replenishment and exact purchase counts",
            processes == 80 && houses == 2 && recruits == 3 && naturalGrowth == 46
            && f.Resources.Citizen == 15 && f.State.GetCitizenCap(team) == 21
            && f.OwnActors().Count(a => a.GrowthData == healer) == 1 && f.OwnActors().Count(a => a.GrowthData == defender) == 1);
        var finalBoard = f.Board(100);
        Check(team + " fulfilled long-term roster needs neither repeated housing nor imaginary replacement orders",
            finalBoard.Governor.DevelopmentPlan.Count == 0
            && !finalBoard.Governor.HousingAssessment(f.EconomyFixture.BuildAction(f.House, finalBoard), finalBoard).Needed);
    }

    static void BuildPaidHouse(AIR2RecruitmentTests.Fixture f, AIBoardState board, Team team, int ownTurn)
    {
        AITurnBudget.Begin(3000);
        var candidates = AIActionEvaluator.EvaluateAll(f.Commander.Personality, board, null, TurnStrategy.Balanced);
        var house = candidates.Find(a => a.FacilityDefinition == f.House && a.ActionType == AIActionType.Build);
        Check(team + " real housing candidate is needed at " + ownTurn,
            house != null && board.Governor.HousingAssessment(house, board).Needed);
        int citizens = f.Resources.Citizen, wood = f.Resources.Wood, stone = f.Resources.Stone, ap = f.State.GetAP(team);
        var selected = f.Select(board, new List<AIAction> { house, new AIAction { ActionType = AIActionType.Wait, Score = 10000 } });
        Check(team + " real selector and paid house execution preserve the ledger at " + ownTurn,
            selected == house && f.Executor.Execute(house, board) && f.Resources.Citizen == citizens
            && f.Resources.Wood == wood - f.House.buildCost.Wood && f.Resources.Stone == stone - f.House.buildCost.Stone
            && f.State.GetAP(team) == ap - f.House.buildAP);
    }

    static void RecruitPaid(AIR2RecruitmentTests.Fixture f, AIBoardState board, UnitData definition, Team team, int ownTurn)
    {
        AITurnBudget.Begin(3000);
        var candidates = AIActionEvaluator.EvaluateAll(f.Commander.Personality, board, null, TurnStrategy.Balanced);
        var recruit = candidates.Find(a => a.ActionType == AIActionType.Summon && a.SummonDefinition == definition);
        if (recruit == null || recruit.StrategicPriority > 3)
        {
            var probe = recruit ?? new AIAction { ActionType = AIActionType.Summon, SummonKind = definition.kind,
                SummonDefinition = definition, APCost = definition.costAP };
            Debug.Log("[AIR3HousingRecruitDiagnostic] team=" + team + " turn=" + ownTurn + " kind=" + definition.kind
                + " candidate=" + (recruit != null) + " priority=" + recruit?.StrategicPriority
                + " mode=" + board.Governor.Mode + " reason=" + board.Governor.RecruitmentBlockReason(probe, board)
                + " sameDefinition=" + (board.ResolveUnitDefinition(definition.kind) == definition)
                + " legal=" + board.CanSummonDefinition(definition) + " cells=" + board.SummonablePositions.Count
                + " resources=" + JsonUtility.ToJson(f.Resources));
            foreach (var need in board.Governor.RecruitmentPlan)
                Debug.Log("[AIR3HousingRecruitOrder] " + team + " " + need.Definition.kind + " " + need.Reason);
            foreach (var resource in board.Governor.BasicResources.Resources)
                if (resource.State <= BasicResourceState.Shortage) Debug.Log("[AIR3HousingRecruitResource] " + team + " " + resource);
        }
        Check(team + " future role reaches a legal strategic candidate at " + ownTurn, recruit != null && recruit.StrategicPriority <= 3);
        int citizens = f.Resources.Citizen, wood = f.Resources.Wood, stone = f.Resources.Stone, bread = f.Resources.Bread, ap = f.State.GetAP(team);
        int count = f.OwnActors().Count;
        var selected = f.Select(board, new List<AIAction> { recruit, new AIAction { ActionType = AIActionType.Wait, Score = 10000 } });
        Check(team + " real future-role purchase is charged and creates exactly one unit at " + ownTurn,
            selected == recruit && f.Executor.Execute(recruit, board) && f.OwnActors().Count == count + 1
            && f.Resources.Citizen == citizens - definition.costCitizen && f.Resources.Wood == wood - definition.costWood
            && f.Resources.Stone == stone - definition.costStone && f.Resources.Bread == bread - definition.costBread
            && f.State.GetAP(team) == ap - definition.costAP);
    }

    static void CitizenGrowthPlans(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        f.Units.UnitDataMap.Clear(); f.Resources.Citizen = 0; f.Food(100);
        f.Rules.aiEconomy.forecastTurns = 4;
        var board = f.Board(60); var economy = new AIBasicResourceEconomy();
        var purchase = new BasicResourcePlan(new FacilityData.ResourceCost(citizen: 1), .75f, .75f);
        var plans = new[] { purchase, purchase, purchase };
        economy.Evaluate(board, board.ProductionDemand, false, plans);
        var citizen = economy.Get(ResourceKind.Citizen);
        Check(team + " three small purchases use actual natural growth without inventing a critical blocked supply project",
            citizen.IncomePerTurn == 1 && citizen.BlockedPlanValue == 0 && citizen.State != BasicResourceState.Critical
            && citizen.PlannedDemand > 0 && f.Resources.Citizen == 0 && f.State.GetCitizenCap(team) == 5);
        economy = new AIBasicResourceEconomy();
        economy.Evaluate(board, board.ProductionDemand, false,
            new[] { new BasicResourcePlan(new FacilityData.ResourceCost(citizen: 6), 1, 1) });
        citizen = economy.Get(ResourceKind.Citizen);
        Check(team + " a purchase beyond real capacity still retains its shortage and blocked plan",
            citizen.BlockedPlanValue > 0 && citizen.State == BasicResourceState.Critical && f.Resources.Citizen == 0);
        foreach (var producer in f.Buildings.Where(a => a.facilityKind == FacilityKind.Bakery)) producer.HP = 0;
        f.Resources.Bread = 0; board = f.Board(61); economy = new AIBasicResourceEconomy();
        economy.Evaluate(board, board.ProductionDemand, false, plans);
        citizen = economy.Get(ResourceKind.Citizen);
        Check(team + " no natural growth credit is given when actual food cannot feed it",
            citizen.BlockedPlanValue > 1.5f && citizen.State == BasicResourceState.Critical && f.Resources.Citizen == 0);
    }

    static void CheckPlannedCitizenReserve(AIR2RecruitmentTests.Fixture f, AIBoardState board, UnitData definition, Team team)
    {
        var purchase = new AIAction { ActionType = AIActionType.Summon, SummonKind = definition.kind,
            SummonDefinition = definition, APCost = definition.costAP };
        var economy = board.Governor.BasicResources;
        Check(team + " a selected recruit may spend its own reserved citizen price while retaining the real minimum",
            economy.AllowSpend(purchase, false, out _) && f.Resources.Citizen == 21 && definition.costCitizen == 16
            && economy.Get(ResourceKind.Citizen).ReserveTarget >= 8);
        var unrelated = f.AddDefinition(Kind.Ninja, 16, 8);
        var optional = new AIAction { ActionType = AIActionType.Summon, SummonKind = unrelated.kind,
            SummonDefinition = unrelated, APCost = unrelated.costAP };
        Check(team + " an unrelated purchase receives no selected-citizen reservation exception",
            !economy.AllowSpend(optional, false, out string unrelatedReason) && unrelatedReason == "basic_resource_reserve_Citizen");
        int citizens = f.Resources.Citizen;
        f.Resources.Citizen = 18; board.Refresh(); board.Governor.Evaluate(board);
        Check(team + " a selected purchase still must retain the configured three citizens",
            !board.Governor.BasicResources.AllowSpend(purchase, false, out string minimumReason)
            && minimumReason == "basic_resource_reserve_Citizen");
        f.Resources.Citizen = citizens;
        int bread = f.Resources.Bread; f.Resources.Bread = definition.costBread + 4;
        board.Refresh(); board.Governor.Evaluate(board);
        Check(team + " the selected-citizen exception does not release food reserves",
            !board.Governor.BasicResources.AllowSpend(purchase, false, out string foodReason)
            && foodReason == "basic_resource_reserve_Bread");
        f.Resources.Bread = bread;
        int wood = f.Resources.Wood; f.Resources.Wood = definition.costWood - 1;
        board.Refresh(); board.Governor.Evaluate(board);
        Check(team + " the selected-citizen exception does not finance unavailable material",
            !board.Governor.BasicResources.AllowSpend(purchase, false, out string materialReason)
            && materialReason == "basic_resource_unaffordable_Wood");
        f.Resources.Wood = wood; board.Refresh(); board.Governor.Evaluate(board);
    }
}
#endif
