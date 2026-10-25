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
}
#endif
