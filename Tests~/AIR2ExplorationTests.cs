#if UNITY_EDITOR
using System;
using UnityEngine;

public static class AIR2ExplorationTests
{
    static int checks;
    static void Check(string name, bool result)
    { if (!result) throw new InvalidOperationException("[AIR2Exploration] FAIL " + name); checks++; Debug.Log("[AIR2Exploration] PASS " + name); }
    public static void Run(GameSystems live)
    {
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy }) Test(team);
        Debug.Log("[AIR2Exploration] ALL PASSED " + checks);
    }
    static void Test(Team team)
    {
        var settings = ScriptableObject.CreateInstance<ExplorationAISettings>();
        try
        {
            var planner = new AIExplorationPlanner(settings);
            var observation = new ExplorationObservation { Width = 24, Depth = 16, ActorTeam = team,
                OwnBase = new Vector3Int(0, 0, 0), ViewVersion = 1, CellVersion = 1, Turn = 1,
                CanReach = (_, cell) => cell.x < 8 && cell.z < 8 };
            for (int x = 0; x < 8; x++)
            {
                var cell = new Vector3Int(x, 1, 2);
                observation.KnownCells.Add(new ExplorationObservedCell(cell)); observation.VisibleCells.Add(GridHelper.ToGridXZ(cell));
            }
            var actor = new ExplorationActor(37, new Vector3Int(0, 1, 2), actorLifeId: "r2-scout-" + team);
            observation.Actors.Add(actor); planner.Update(observation, false);
            var before = planner.GetObjective(37);
            Check(team + " controlled scout receives a real known frontier", before != null && before.State == ObjectiveState.Active);
            int expedition = before.ObjectiveId, region = before.FrontierRegionId;
            actor.Cell = before.TargetCell;
            observation.Actors[0] = actor; observation.Turn = 2; observation.ViewVersion++;
            var reveal = actor.Cell + Vector3Int.forward;
            observation.KnownCells.Add(new ExplorationObservedCell(reveal)); observation.VisibleCells.Add(GridHelper.ToGridXZ(reveal));
            planner.Update(observation, false);
            var continued = planner.GetObjective(37);
            Check(team + " a one-cell reveal continues the same unfinished region expedition",
                continued.ObjectiveId == expedition && continued.FrontierRegionId == region && continued.State == ObjectiveState.Active
                && continued.TargetCell != actor.Cell && continued.LastReason == "region_exploration_continues");
            planner.Update(observation, true);
            Check(team + " real emergency still suspends the continued scout mission", planner.GetObjective(37).State == ObjectiveState.Suspended);
            observation.Turn = 3; planner.Update(observation, false);
            Check(team + " emergency resolution resumes the same expedition", planner.GetObjective(37).ObjectiveId == expedition
                && planner.GetObjective(37).State == ObjectiveState.Active);
            var saved = planner.CaptureState();
            var restored = new AIExplorationPlanner(settings); restored.RestoreState(JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(saved)));
            restored.Update(observation, false);
            Check(team + " save/load keeps the expedition's stable life identity and observed target",
                restored.GetObjective(37)?.ActorLifeId == actor.ActorLifeId && restored.GetObjective(37)?.ObjectiveId == expedition);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }
}
#endif
