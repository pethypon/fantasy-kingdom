#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

public static class AIPlaytestExplorationTests
{
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[AIPlaytestExploration] FAIL " + id); Debug.Log("[AIPlaytestExploration] PASS " + id); }

    public static void Run()
    {
        var root = new GameObject("Exploration contract fixture");
        try
        {
            var map = root.AddComponent<MapCreate>(); map.maxX = map.maxZ = 2;
            var vision = root.AddComponent<VisionGenerator>(); vision.Awake();
            Check("EXB01-bounds-limit-corner-information", AIBoardQuery.EstimateNewVisionCells(vision, Vector3.zero, Team.Enemy, map) == 4);
            for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++) vision.AddExplored(Team.Enemy, new Vector3Int(x, 0, z));
            Check("EXB02-explored-corner-does-not-invent-outside-information", AIBoardQuery.EstimateNewVisionCells(vision, Vector3.zero, Team.Enemy, map) == 0);
            Check("EXB04-factions-have-separate-exploration", AIBoardQuery.EstimateNewVisionCells(vision, Vector3.zero, Team.Player, map) == 4);
            foreach (var outside in new[] { new Vector3(-1, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 0, 2) })
                Check("EXB03-outside-candidate-" + outside, AIBoardQuery.EstimateNewVisionCells(vision, outside, Team.Player, map) == 0);

            var actor = root.AddComponent<Status>(); actor.kind = Kind.Scout; actor.team = Team.Enemy; actor.MaxHP = 20; actor.HP = 20;
            var history = new AITurnMovementHistory();
            Vector3 a = Vector3.zero, b = Vector3.right;
            history.Record(actor, a, b, true);
            history.Record(actor, b, a, true, 0);
            var method = typeof(AITurnMovementHistory).GetMethod("FruitlessReturn", BindingFlags.Instance | BindingFlags.NonPublic);
            Check("EXR02-unsuccessful-information-return-is-retained", (bool)method.Invoke(history, new object[] { actor, GridHelper.ToGridXZ(a) }));
            history.Record(actor, a, b, true);
            history.Record(actor, b, a, true, 1);
            Check("EXR03-observed-information-clears-failed-prediction", !(bool)method.Invoke(history, new object[] { actor, GridHelper.ToGridXZ(a) }));
            Check("EXR04-explicit-retreat-keeps-return-exception", !history.ShouldReject(new AIAction { Unit = actor, ActionType = AIActionType.Retreat, TargetPos = b }, null));
            history.Record(actor, a, b, true, 0); history.Clear();
            Check("EXR06-next-turn-releases-all-state", history.Count == 0 && !(bool)method.Invoke(history, new object[] { actor, GridHelper.ToGridXZ(b) }));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
#endif
