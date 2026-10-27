#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Optional isolated-project real-board fixtures. NOT RUN. Never invoked by import/CoreLogicTests.</summary>
public static class AIR4WorldModelPlayTests
{
    static void Check(string name, bool condition)
    { if (!condition) throw new Exception("[R4WorldPlay] FAIL " + name); Debug.Log("[R4WorldPlay] PASS " + name); }
    public static void PlayMode(GameSystems live)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
            using (var fixture = new EconomyR2Tests.Fixture(live, team))
            {
                var cells = fixture.AdjacentCells(); var own = fixture.Unit(); own.transform.position = cells[0];
                var hidden = fixture.Opponent(cells[2], 10); var board = fixture.Board();
                var builder = new AIWorldModelBuilder(); var before = builder.Build(board, "R4Fixture" + team, 1);
                Check("W01-hidden-opponent-excluded-" + team, before.ObservedOpponents.Count == 0);
                hidden.HP = 1; hidden.ATK = 99999; hidden.transform.position = cells[1];
                Check("W06-one-generation-cache-" + team, ReferenceEquals(before, builder.Build(board, "R4Fixture" + team, 1)) && builder.BuildCount == 1);
                board.Refresh(); var after = builder.Build(board, "R4Fixture" + team, 1);
                Check("W03-generation-invalidates-" + team, !ReferenceEquals(before, after) && !before.IsCurrent(board) && builder.BuildCount == 2);
                Check("W01-hidden-current-state-does-not-leak-" + team, after.ObservedOpponents.Count == 0
                    && after.OwnUnits.Count == before.OwnUnits.Count && after.LastKnownOpponents.Count == 0);
                Check("W04-W08-actor-owned-economy-" + team, after.ActorTeam == team
                    && after.OwnEconomy.Wood == fixture.Resources.Wood && after.OwnAP == fixture.State.GetAP(team));
            }
    }
}
#endif
