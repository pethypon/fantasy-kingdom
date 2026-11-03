using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIExplorationPlanner
{
    bool territoryReconRequested;
    bool territoryReconIsRevisit;
    Vector3Int territoryReconDirection;
    readonly List<Vector3Int> territoryReconCandidates = new List<Vector3Int>(64);

    // An explicit national reconnaissance request outranks unrelated generic frontiers.
    // Candidate entrances remain observed, passable, safe and reachable by a proved known route.
    bool TryAssignTerritoryRecon(ExplorationActor actor, ExplorationMovementHistory history)
    {
        if (!territoryReconRequested || emergency) return false;
        territoryReconCandidates.Clear();
        int limit = Mathf.Clamp(settings.MaxCandidateRegions, 1, 512);
        if (territoryReconIsRevisit)
        {
            // Previously observed main-base coordinates may be fully explored already.
            // Seek a known vantage without requiring a fresh-unknown frontier there.
            for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 4; dz++)
            {
                var cell = territoryReconDirection + new Vector3Int(dx, 0, dz);
                if (cell == territoryReconDirection || cell == XZ(actor.Cell) || !terrain.ContainsKey(cell) || blocked.Contains(cell)
                    || IsUnproductiveViewpoint(actor.ActorLifeId, RegionId(cell), cell, false)) continue;
                InsertReconCandidate(cell, actor, limit);
            }
        }
        foreach (var region in regions.Values)
        {
            if (territoryReconIsRevisit) break;
            if (region.Frontier == null || reservedRegions.Contains(region.Frontier.RegionId) || cooldowns.ContainsKey(region.Frontier.RegionId)) continue;
            foreach (var cell in region.FrontierCells)
            {
                if (cell == XZ(actor.Cell) || blocked.Contains(cell)) continue;
                if (IsUnproductiveViewpoint(actor.ActorLifeId, region.Frontier.RegionId, cell, false)) continue;
                InsertReconCandidate(cell, actor, limit);
            }
        }
        foreach (var cell in territoryReconCandidates)
        {
            int id = RegionId(cell);
            if (!regions.TryGetValue(id, out var cache) || cache.Frontier == null && !territoryReconIsRevisit) continue;
            var source = cache.Frontier ?? new ExplorationFrontierRegion { RegionId = id, IsIntelRevisit = true };
            var candidate = new ExplorationFrontierRegion { RegionId = id, RepresentativeCell = terrain[cell],
                UnknownCellsBehind = source.UnknownCellsBehind, FrontierCellCount = source.FrontierCellCount,
                IsIntelRevisit = territoryReconIsRevisit || source.IsIntelRevisit, Importance = source.Importance, OldestSeenTurn = source.OldestSeenTurn, ImportantCell = source.ImportantCell };
            var score = ScoreFrontier(candidate, actor, history, territoryReconIsRevisit);
            if (!string.IsNullOrEmpty(score.RejectReason)) continue;
            float remaining = MeasureProgress(actor, candidate.RepresentativeCell, out bool proved, out int revision, out var waypoint);
            if (!proved || remaining <= 0) continue;
            var objective = new ExploreObjective { ObjectiveId = AllocateObjectiveId(), FrontierRegionId = id,
                TargetCell = candidate.RepresentativeCell, AssignedUnitId = actor.UnitId, ActorLifeId = actor.ActorLifeId,
                CreatedTurn = turn, LastProgressTurn = turn, State = ObjectiveState.Active,
                BestDistance = remaining, LastDistance = remaining, LastUnknownCount = source.UnknownCellsBehind,
                LastSampleTurn = turn, IsIntelRevisit = candidate.IsIntelRevisit, LastReason = "territory_recon_assigned",
                HasRequiredObservation = territoryReconIsRevisit, RequiredObservationCell = territoryReconDirection,
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
    void InsertReconCandidate(Vector3Int cell, ExplorationActor actor, int limit)
    {
        foreach (var goal in objectives.Values)
            if (goal.ActorLifeId != actor.ActorLifeId && (goal.State == ObjectiveState.Active || goal.State == ObjectiveState.Suspended)
                && XZ(goal.TargetCell) == cell) return;
        int index = 0;
        while (index < territoryReconCandidates.Count && CompareReconCell(territoryReconCandidates[index], cell, actor) < 0) index++;
        if (index >= limit) return;
        territoryReconCandidates.Insert(index, cell);
        if (territoryReconCandidates.Count > limit) territoryReconCandidates.RemoveAt(limit);
    }
    int CompareReconCell(Vector3Int a, Vector3Int b, ExplorationActor actor)
    {
        int direction = ProgressDistance(a, territoryReconDirection).CompareTo(ProgressDistance(b, territoryReconDirection));
        if (direction != 0) return direction;
        int travel = ProgressDistance(actor.Cell, a).CompareTo(ProgressDistance(actor.Cell, b));
        return travel != 0 ? travel : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z);
    }
}
