#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled native priority-request and same-board restore regressions; not complete matches.</summary>
public static class AITerritoryReconTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string label, bool condition)
    { if (!condition) throw new InvalidOperationException("[AITerritoryRecon] FAIL " + label); checks++; Debug.Log("[AITerritoryRecon] PASS " + label); }
    public static void Run(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.MapCreate == null) throw new InvalidOperationException("An initialized native match is required.");
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            SupersedeOppositeFrontier(team, false);
            SupersedeOppositeFrontier(team, true);
            SameBoardRestore(systems, team);
        }
        Debug.Log("[AITerritoryRecon] ALL PASSED " + checks + " (controlled topology and native board restore)");
    }

    static void SupersedeOppositeFrontier(Team team, bool fallback)
    {
        var settings = ScriptableObject.CreateInstance<ExplorationAISettings>();
        try
        {
            settings.RegionSize = 4; settings.EnableJsonlTelemetry = false;
            var control = new AIExplorationPlanner(settings); var requested = new AIExplorationPlanner(settings);
            var initial = new ExplorationObservation { Width = 40, Depth = 40, Turn = 10, ActorTeam = team,
                OwnBase = new Vector3Int(10, 0, 10), CellVersion = 1, ViewVersion = 1 };
            for (int x = 2; x <= 18; x++)
            { initial.KnownCells.Add(new ExplorationObservedCell(new Vector3Int(x, 1, 10))); initial.VisibleCells.Add(new Vector3Int(x, 0, 10)); }
            var actor = new ExplorationActor(171, new Vector3Int(10, 1, 10), fallback ? Kind.Knight : Kind.Scout,
                actorLifeId: "TerritoryRecon_" + team + "_" + fallback) { IsFallbackExplorer = fallback,
                MovementOffsets = new[] { Vector2Int.left, Vector2Int.right }, MovementDirectionIndependent = true };
            initial.Actors.Add(actor);
            initial.CanReach = (_, cell) => cell.x <= 4;
            control.Update(initial, false); requested.Update(initial, false);
            var old = requested.GetObjective(actor.UnitId);
            string label = team + (fallback ? " fallback" : " scout");
            Check(label + " starts with a real opposite-direction active frontier", old != null && old.State == ObjectiveState.Active && old.TargetCell.x <= 4);
            int previousId = old.ObjectiveId;
            initial.Turn = 11; initial.CanReach = null; initial.CellVersion++;
            initial.KnownCells.Clear(); initial.VisibleCells.Clear();
            initial.KnownCells.Add(new ExplorationObservedCell(new Vector3Int(25, 1, 10)));
            initial.VisibleCells.Add(new Vector3Int(25, 0, 10));
            initial.ImportantCells[new Vector3Int(25, 0, 10)] = 1;
            control.Update(initial, false);
            Check(label + " reproduction: importance alone retains the unrelated active frontier", control.GetObjective(actor.UnitId).ObjectiveId == previousId
                && control.GetObjective(actor.UnitId).TargetCell.x <= 4);
            initial.HasTerritoryReconDirection = true; initial.TerritoryReconDirection = new Vector3Int(25, 0, 10);
            requested.Update(initial, false);
            var next = requested.GetObjective(actor.UnitId);
            Check(label + " explicit territory request supersedes the unrelated frontier", next != null && next.State == ObjectiveState.Active
                && next.ObjectiveId != previousId && next.TargetCell.x > actor.Cell.x && next.TargetCell.x <= 18
                && next.LastReason == "territory_recon_assigned" && next.UsesKnownRouteDistance);
            Check(label + " route safety/deviation scoring now supports the directed legal step", requested.GetGoalProgress(actor.UnitId, new Vector3Int(11, 1, 10)) > 0
                && requested.GetMoveBonus(actor.UnitId, new Vector3Int(11, 1, 10)) > requested.GetMoveBonus(actor.UnitId, new Vector3Int(9, 1, 10)));
            int activeId = next.ObjectiveId;
            initial.Turn = 12; requested.Update(initial, false);
            Check(label + " unchanged request preserves its multi-turn objective", requested.GetObjective(actor.UnitId).ObjectiveId == activeId);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }

    static void SameBoardRestore(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var host = new GameObject("Territory same-board recon cache"); host.SetActive(false);
        try
        {
            var usable = live.MapCreate.SetPos.Where(p => !live.MapCreate.IsRiver((int)p.x, (int)p.z)
                && !live.MapCreate.IsHighMountain((int)p.x, (int)p.z) && !live.MapCreate.Environment.HasCell(p)).ToList();
            var first = usable[0]; var second = usable.Last(p => GridHelper.ChebyshevDistance(first, p) > 4);
            var vision = host.AddComponent<VisionGenerator>(); var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            territory.GetTerritory(team == Team.Player ? Team.Enemy : Team.Player).Add(first);
            var seen = new HashSet<Vector3Int> { GridHelper.ToGridXZ(first) };
            var explored = new HashSet<Vector3Int> { GridHelper.ToGridXZ(first), GridHelper.ToGridXZ(second) };
            foreach (var name in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(name, Hidden).SetValue(vision,
                    name == (team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? seen
                    : name == (team == Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision,
                null, null, f.State, null, 10, null, team) { MapCreate = live.MapCreate, TerritorySystem = territory };
            board.Refresh(); var intel = new EnemyTerritoryIntel(); intel.Observe(board);
            seen.Clear(); board.Refresh(); intel.Observe(board);
            Check(team + " caches an observed-territory hint outside sight", intel.TryGetReconTarget(board, out var before)
                && GridHelper.MatchXZ(before, GridHelper.ToGridXZ(first)));
            var saved = intel.CaptureState();
            saved.Regions.Clear(); var cell = GridHelper.ToGridXZ(second);
            var region = new EnemyTerritoryIntelRegion { OriginX = cell.x / 16 * 16, OriginZ = cell.z / 16 * 16,
                FirstObservedTurn = 10, LastConfirmedTurn = 10, Cells = new int[8] };
            int bit = cell.z % 16 * 16 + cell.x % 16; region.Cells[bit >> 5] |= 1 << (bit & 31); saved.Regions.Add(region);
            Check(team + " replacement save has equal cell count/time but distinct legal evidence", EnemyTerritoryIntel.IsValidState(saved, live.MapCreate.maxX, live.MapCreate.maxZ, team));
            int generation = board.Generation;
            intel.RestoreState(saved, team); intel.Observe(board);
            Check(team + " same-board same-generation restore cannot reuse the old hint", board.Generation == generation
                && intel.ObservedCellCount == 1 && intel.LastConfirmedTurn == 10 && intel.TryGetReconTarget(board, out var after)
                && GridHelper.MatchXZ(after, cell) && after != before);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
}
#endif
