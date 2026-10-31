#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Explicitly opt-in, artificial superior-force fixture for the real
/// autonomous match runner. Never used for ordinary-match/balance measurements.</summary>
public static class SquadAssaultSetup
{
    public static bool Apply(TurnGenerator turn)
    {
        if (Environment.GetEnvironmentVariable("FK_SQUAD_ASSAULT_SETUP") != "1") return false;
        var s = turn.Systems;
        var target = s.CrystalSystem.Enemycrystal.GetComponentInChildren<Status>();
        if (target == null || target.HP != target.MaxHP) throw new InvalidOperationException("Full-health normal objective required");
        var occupied = new List<Status>(); CombatRegistry.Collect(occupied);
        var blocked = new HashSet<Vector3Int>();
        foreach (var actor in occupied) if (actor != null && actor.IsAlive) blocked.Add(GridHelper.ToGridXZ(actor.transform.position));
        var start = GridHelper.ToGridXZ(target.transform.position);
        var depths = new Dictionary<Vector3Int, int>(); var queue = new Queue<Vector3Int>();
        depths[start] = 0; queue.Enqueue(start);
        var staging = new List<Vector3>();
        while (queue.Count > 0 && depths.Count < 4096)
        {
            var cell = queue.Dequeue(); int distance = depths[cell];
            s.MapCreate.TryGetHeight(cell.x, cell.z, out float y);
            var from = new Vector3(cell.x, y, cell.z);
            if (distance >= 7 && distance <= 9 && !blocked.Contains(cell)) staging.Add(from);
            if (distance >= 9) continue;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                var next = cell + new Vector3Int(dx, 0, dz);
                if (dx == 0 && dz == 0 || depths.ContainsKey(next)
                    || !s.MapCreate.TryGetHeight(next.x, next.z, out float height)) continue;
                var destination = new Vector3(next.x, height, next.z);
                if (!s.MapCreate.CanTraverse(from, destination)) continue;
                depths[next] = distance + 1; queue.Enqueue(next);
            }
        }
        if (staging.Count < 8) throw new InvalidOperationException("Not enough connected staging cells for the controlled squad fixture");
        // This setup is intentionally advantaged and is reported as such. No
        // runtime damage, HP, AP cost, decision budget or Fog rule is changed.
        var stock = s.FactionState.GetResources(Team.Player);
        // Explicit subsystem fixture override: keep the initial test reserve
        // from being clamped to 200 on the first economy tick. This is never
        // enabled in ordinary matches or written back to a rules asset.
        GameAuthoringRules.Active.enforceResourceCapacity = false;
        stock.Wood = stock.Stone = stock.Iron = stock.Water = stock.Bread = stock.Wheat = stock.MagicOre = 5000;
        var kinds = new[] { Kind.Knight, Kind.Knight, Kind.Knight, Kind.Crossbow, Kind.Crossbow, Kind.Scout, Kind.WhiteMage, Kind.DragonKnight };
        for (int index = 0; index < kinds.Length; index++)
        {
            var actor = s.SummonSystem.SpawnUnitForLoad(kinds[index], Team.Player, staging[index]);
            if (actor == null || actor.MaxHP <= 0) throw new InvalidOperationException("Normal unit spawn failed: " + kinds[index]);
            actor.HP = actor.MaxHP;
        }
        s.MoveGenerator.UnitPointCore();
        s.VisionGenerator.MarkVisionDirty(); s.VisionGenerator.VisionPoint(s.MapCreate, s.MoveGenerator, s.CrystalSystem);
        Debug.Log("[SquadAssault] CONTROLLED: eight normal-stat additional Player troops staged seven terrain steps from the full-health enemy main crystal; Player materials=5000 each and resource-cap enforcement disabled in this fixture only; citizen/AP/budgets/damage/Fog unchanged. Official sight only; no objective coordinates injected into AI. Not an ordinary match or win-rate result.");
        return true;
    }
}
#endif
