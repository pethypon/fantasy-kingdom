using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIExplorationPlanner
{
    bool territoryReconRequested;
    Vector3Int territoryReconDirection;
    readonly List<Vector3Int> territoryReconCandidates = new List<Vector3Int>(64);

    // An explicit national reconnaissance request outranks unrelated generic frontiers.
    // Candidate entrances remain observed, passable, safe and reachable by a proved known route.
    bool TryAssignTerritoryRecon(ExplorationActor actor, ExplorationMovementHistory history)
    {
        if (!territoryReconRequested || emergency) return false;
        territoryReconCandidates.Clear();
        int limit = Mathf.Clamp(settings.MaxCandidateRegions, 1, 512);
        foreach (var region in regions.Values)
        {
            if (region.Frontier == null || reservedRegions.Contains(region.Frontier.RegionId) || cooldowns.ContainsKey(region.Frontier.RegionId)) continue;
            foreach (var cell in region.FrontierCells)
            {
                if (cell == XZ(actor.Cell) || blocked.Contains(cell)) continue;
                int index = 0;
                while (index < territoryReconCandidates.Count && CompareReconCell(territoryReconCandidates[index], cell, actor) < 0) index++;
                if (index >= limit) continue;
                territoryReconCandidates.Insert(index, cell);
                if (territoryReconCandidates.Count > limit) territoryReconCandidates.RemoveAt(limit);
            }
        }
        foreach (var cell in territoryReconCandidates)
        {
            int id = RegionId(cell);
            if (!regions.TryGetValue(id, out var cache) || cache.Frontier == null) continue;
            var source = cache.Frontier;
            var candidate = new ExplorationFrontierRegion { RegionId = id, RepresentativeCell = terrain[cell],
                UnknownCellsBehind = source.UnknownCellsBehind, FrontierCellCount = source.FrontierCellCount,
                IsIntelRevisit = source.IsIntelRevisit, Importance = source.Importance, OldestSeenTurn = source.OldestSeenTurn, ImportantCell = source.ImportantCell };
            var score = ScoreFrontier(candidate, actor, history);
            if (!string.IsNullOrEmpty(score.RejectReason)) continue;
            float remaining = MeasureProgress(actor, candidate.RepresentativeCell, out bool proved, out int revision, out var waypoint);
            if (!proved || remaining <= 0) continue;
            var objective = new ExploreObjective { ObjectiveId = nextObjectiveId++, FrontierRegionId = id,
                TargetCell = candidate.RepresentativeCell, AssignedUnitId = actor.UnitId, ActorLifeId = actor.ActorLifeId,
                CreatedTurn = turn, LastProgressTurn = turn, State = ObjectiveState.Active,
                BestDistance = remaining, LastDistance = remaining, LastUnknownCount = source.UnknownCellsBehind,
                LastSampleTurn = turn, IsIntelRevisit = candidate.IsIntelRevisit, LastReason = "territory_recon_assigned",
                UsesKnownRouteDistance = true, RouteMetricRevision = revision, RouteWaypoint = waypoint };
            objectives[actor.ActorLifeId] = objective; reservedRegions.Add(id);
            BeginLeg(objective);
            source.LastAssignedTurn = turn; cache.LastAssignedTurn = turn;
            history.Clear(); history.Add(actor.Cell, turn, 0, remaining);
            RecordDecision(actor, objective, history, "territory_recon_assigned", false, new List<ExplorationCandidateScore> { score });
            return true;
        }
        return false;
    }
    int CompareReconCell(Vector3Int a, Vector3Int b, ExplorationActor actor)
    {
        int direction = ProgressDistance(a, territoryReconDirection).CompareTo(ProgressDistance(b, territoryReconDirection));
        if (direction != 0) return direction;
        int travel = ProgressDistance(actor.Cell, a).CompareTo(ProgressDistance(actor.Cell, b));
        return travel != 0 ? travel : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z);
    }
}
