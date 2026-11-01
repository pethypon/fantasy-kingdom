#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class AIStandingArmyTests
{
    static void Check(string id, bool ok)
    { if (!ok) throw new InvalidOperationException("[AIStandingArmy] FAIL " + id); Debug.Log("[AIStandingArmy] PASS " + id); }
    public static void Run(GameSystems live, TurnGenerator turn)
    {
        var field = typeof(AIHousingConfig).GetField("active", BindingFlags.Static | BindingFlags.NonPublic);
        var old = field.GetValue(null);
        var config = ScriptableObject.CreateInstance<AIHousingConfig>();
        config.EnableDevelopmentGrowth = false; config.EnableStandingSupport = true;
        config.MinimumStandingFighters = 4; config.StandingArmyStartTurn = 6;
        config.CitizenReserveTarget = 8; config.CitizenReserveStartTurn = 12;
        field.SetValue(null, config);
        try
        {
            foreach (var team in new[] { Team.Player, Team.Enemy })
            using (var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1))
            {
                foreach (var actor in f.OwnActors()) actor.HP = actor.MaxHP;
                var board = f.Board(8);
                Check(team + " healthy early army plans healing before casualties",
                    board.Governor.RecruitmentPlan.Any(x => x.Role == "healer" && x.Reason == "standing_army_healing_support"));
                var candidates = AIActionEvaluator.EvaluateAll(f.Commander.Personality, board, null, TurnStrategy.Balanced);
                var healer = candidates.FirstOrDefault(x => x.ActionType == AIActionType.Summon && x.SummonDefinition == f.Healer);
                Check(team + " planned healer is a legal paid candidate", healer != null && healer.StrategicPriority == 3);
                int citizens = f.Resources.Citizen, count = f.OwnActors().Count;
                var chosen = f.Select(board, new List<AIAction> { healer });
                Check(team + " official selection and executor train the support unit",
                    chosen == healer && f.Executor.Execute(chosen, board) && f.OwnActors().Count == count + 1
                    && f.Resources.Citizen == citizens - f.Healer.costCitizen);
                foreach (var scout in f.OwnActors().Where(x => x.kind == Kind.Scout)) scout.HP = 0;
                board = f.Board(9);
                Check(team + " missing scout has an explicit replacement order",
                    board.Governor.RecruitmentPlan.Any(x => x.Role == "scout"));
                f.State.SetAP(team, 40); board = f.Board(12);
                var house = f.EconomyFixture.BuildAction(f.House, board);
                var assessment = board.Governor.HousingAssessment(house, board);
                Check(team + " funded civilian reserve creates housing demand", assessment.Needed && assessment.RequiredStock >= 8);
                citizens = f.Resources.Citizen; int capacity = f.State.GetCitizenCap(team);
                Check(team + " housing changes capacity without granting free citizens", f.Executor.Execute(house, board)
                    && f.State.GetCitizenCap(team) > capacity && f.Resources.Citizen == citizens);
                for (int i = 0; i < 4; i++)
                { f.State.GetNation(team).TurnsAlive++; f.EconomyFixture.Economy.ProcessTurn(team); }
                Check(team + " ordinary economic turns grow actual citizens", f.Resources.Citizen > citizens
                    && f.Resources.Citizen <= f.State.GetCitizenCap(team));
            }
            Debug.Log("[AIStandingArmy] COMPLETE");
        }
        finally { field.SetValue(null, old); UnityEngine.Object.DestroyImmediate(config); GameContentRevision.NotifyChanged(); }
    }
}
#endif
