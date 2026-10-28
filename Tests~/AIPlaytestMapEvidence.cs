#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Observer-only truth export. Never passed to a board, planner or action evaluator.</summary>
public static class AIPlaytestMapEvidence
{
    [Serializable] sealed class Evidence
    {
        public int Width, Depth, TraversableCells, PlayerComponentCells, EnemyComponentCells, TerrainRouteSteps = -1;
        public Vector3 PlayerBase, EnemyBase;
        public bool BasesConnected, PlayerBaseInTerrainList, EnemyBaseInTerrainList, R4Enabled, R4Shadow, R4MayInfluenceActions;
        public string Scope = "Read-only official CanTraverse over adjacent terrain cells at new-game start. Bases can be removed from SetPos, so reachability starts/ends on their legal adjacent deployment cells. Excludes occupancy, unit move patterns, AP and later construction. Diagnostic truth is not AI input.";
    }

    public static void CaptureInitial(TurnGenerator turn, string folder)
    {
        string path = Path.Combine(folder, "initial-map-connectivity.json");
        if (File.Exists(path) || turn?.Systems?.MapCreate == null) return;
        var map = turn.Systems.MapCreate;
        var cells = new Dictionary<Vector3Int, Vector3>();
        foreach (var cell in map.SetPos) cells[GridHelper.ToGridXZ(cell)] = cell;
        var player = turn.Systems.CrystalSystem.PCP;
        var enemy = turn.Systems.CrystalSystem.ECP;
        var fromPlayer = Reach(map, cells, player);
        var fromEnemy = Reach(map, cells, enemy);
        int steps = int.MaxValue;
        foreach (var cell in fromEnemy)
            if (cell.Value == 0 && fromPlayer.TryGetValue(cell.Key, out int distance)) steps = Math.Min(steps, distance);
        bool connected = steps != int.MaxValue;
        var config = AICognitiveAIConfig.Active;
        var evidence = new Evidence { Width = map.maxX, Depth = map.maxZ, TraversableCells = cells.Count,
            PlayerBase = player, EnemyBase = enemy, PlayerComponentCells = fromPlayer.Count, EnemyComponentCells = fromEnemy.Count,
            PlayerBaseInTerrainList = cells.ContainsKey(GridHelper.ToGridXZ(player)), EnemyBaseInTerrainList = cells.ContainsKey(GridHelper.ToGridXZ(enemy)),
            BasesConnected = connected, TerrainRouteSteps = connected ? steps : -1,
            R4Enabled = config.Enabled, R4Shadow = config.ShadowMode, R4MayInfluenceActions = config.MayInfluenceActions };
        File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
    }

    static Dictionary<Vector3Int, int> Reach(MapCreate map, Dictionary<Vector3Int, Vector3> cells, Vector3 start)
    {
        var distances = new Dictionary<Vector3Int, int>();
        var queue = new Queue<Vector3Int>();
        var origin = GridHelper.ToGridXZ(start);
        if (cells.ContainsKey(origin)) { distances.Add(origin, 0); queue.Enqueue(origin); }
        else
            foreach (var cell in cells)
                if (GridHelper.ChebyshevDistance(cell.Key, origin) <= 1 && map.CanTraverse(start, cell.Value))
                { distances.Add(cell.Key, 0); queue.Enqueue(cell.Key); }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var next = new Vector3Int(current.x + dx, 0, current.z + dz);
                if (distances.ContainsKey(next) || !cells.TryGetValue(next, out var destination)
                    || !map.CanTraverse(cells[current], destination)) continue;
                distances.Add(next, distances[current] + 1); queue.Enqueue(next);
            }
        }
        return distances;
    }
}
#endif
