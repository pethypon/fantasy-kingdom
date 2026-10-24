using UnityEngine;

public sealed partial class AIExplorationPlanner
{
    // A little information at a waypoint is not completion of its whole region.
    // Only the bounded known frontier cache is inspected; hidden terrain/occupants are never queried.
    bool TryContinueRegion(ExplorationActor actor, ExploreObjective objective, ExplorationMovementHistory history)
    {
        if (objective.IsIntelRevisit || !regions.TryGetValue(objective.FrontierRegionId, out var cache)) return false;
        bool found = false; float best = float.NegativeInfinity; Vector3Int next = default;
        foreach (var cell in cache.FrontierCells)
        {
            if (cell == XZ(objective.TargetCell) || blocked.Contains(cell) || !terrain.TryGetValue(cell, out var target)
                || Distance(actor.Cell, target) <= Mathf.Max(0, settings.CompletionDistance)
                || observation.CanReach != null && !observation.CanReach(actor.UnitId, target)) continue;
            float danger = observation.EstimateDanger != null ? Mathf.Max(0, observation.EstimateDanger(actor.UnitId, target)) : 0;
            if (danger / Mathf.Max(1, actor.HP) >= settings.MaximumFrontierDangerFraction) continue;
            int unknown = 0;
            foreach (var offset in neighbours) if (Inside(cell + offset) && !memory.ContainsKey(cell + offset)) unknown++;
            if (unknown == 0) continue;
            float score = unknown * settings.UnknownGainWeight - Distance(actor.Cell, target) * settings.TravelCostWeight;
            if (memory.TryGetValue(cell, out var seen) && seen.LastVisitedTurn >= 0
                && turn - seen.LastVisitedTurn <= settings.RecentVisitTurns) score -= settings.RecentVisitPenalty;
            if (!found || score > best || score == best && CompareCell(cell, XZ(next)) < 0)
            { found = true; best = score; next = target; }
        }
        if (!found) return false;
        objective.TargetCell = next; objective.LastProgressTurn = turn; objective.LastReason = "region_exploration_continues";
        objective.BestDistance = objective.LastDistance = MeasureProgress(actor, next, out bool route, out int revision, out var waypoint);
        objective.UsesKnownRouteDistance = route; objective.RouteMetricRevision = revision; objective.RouteWaypoint = waypoint;
        history.RebaseDistances(objective.LastDistance);
        RecordDecision(actor, objective, history, objective.LastReason, false, null);
        return true;
    }
}
