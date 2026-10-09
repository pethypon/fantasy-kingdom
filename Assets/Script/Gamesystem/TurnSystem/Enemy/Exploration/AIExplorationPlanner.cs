using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Persistent strategic exploration, consuming faction observations rather than the hidden game state.</summary>
public sealed partial class AIExplorationPlanner
{
    sealed class RegionCache
    {
        public readonly HashSet<Vector3Int> Cells = new HashSet<Vector3Int>();
        public readonly HashSet<Vector3Int> FrontierCells = new HashSet<Vector3Int>();
        public ExplorationFrontierRegion Frontier;
        public int LastAssignedTurn = -1;
        public int LastVisitedTurn = -1;
        public int VisitCount;
    }
    static readonly Vector3Int[] neighbours = { Vector3Int.left, Vector3Int.right, new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1) };
    readonly ExplorationAISettings settings;
    readonly ExplorationTelemetry telemetry;
    readonly ExplorationKnownRoute knownRoute;
    readonly Dictionary<Vector3Int, ExplorationCellMemory> memory = new Dictionary<Vector3Int, ExplorationCellMemory>();
    readonly Dictionary<Vector3Int, Vector3Int> terrain = new Dictionary<Vector3Int, Vector3Int>();
    readonly HashSet<Vector3Int> blocked = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> visible = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> previousVisible = new HashSet<Vector3Int>();
    readonly Dictionary<Vector3Int, float> importance = new Dictionary<Vector3Int, float>();
    readonly Dictionary<int, RegionCache> regions = new Dictionary<int, RegionCache>();
    readonly HashSet<int> dirtyRegions = new HashSet<int>();
    readonly List<int> sortedRegionIds = new List<int>();
    readonly List<ExplorationFrontierRegion> frontiers = new List<ExplorationFrontierRegion>();
    readonly Dictionary<string, ExploreObjective> objectives = new Dictionary<string, ExploreObjective>(StringComparer.Ordinal);
    readonly Dictionary<string, ExplorationMovementHistory> histories = new Dictionary<string, ExplorationMovementHistory>(StringComparer.Ordinal);
    readonly Dictionary<int, ExplorationActor> actors = new Dictionary<int, ExplorationActor>();
    readonly List<ExplorationActor> sortedActors = new List<ExplorationActor>();
    readonly HashSet<string> liveLifeIds = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<int, int> cooldowns = new Dictionary<int, int>();
    readonly HashSet<int> reservedRegions = new HashSet<int>();
    readonly HashSet<Vector3Int> newlyRevealedSet = new HashSet<Vector3Int>();
    readonly Dictionary<int, List<Vector3Int>> newlyRevealedByRegion = new Dictionary<int, List<Vector3Int>>();
    readonly Dictionary<int, int> revealCredit = new Dictionary<int, int>();
    readonly List<string> removedLifeIds = new List<string>();
    readonly Dictionary<string, List<ExplorationCandidateScore>> lastCandidates = new Dictionary<string, List<ExplorationCandidateScore>>(StringComparer.Ordinal);
    ExplorationObservation observation;
    int width, depth, turn = -1, nextObjectiveId = 1;
    int viewVersion = int.MinValue, cellVersion = int.MinValue, actorVersion = int.MinValue;
    int dangerVersion = int.MinValue;
    int lastFrontierRefresh = -1;
    bool emergency;
    Team actorTeam;
    public IReadOnlyDictionary<Vector3Int, ExplorationCellMemory> Memory => memory;
    public IReadOnlyList<ExplorationFrontierRegion> Frontiers => frontiers;
    public IReadOnlyCollection<ExploreObjective> Objectives => objectives.Values;
    public IReadOnlyList<ExplorationDecisionRecord> Diagnostics => telemetry.Records;
    public int ObservationCellsProcessed { get; private set; }
    public int FrontierRegionRebuildCount { get; private set; }
    public int LastCandidateCount { get; private set; }
    public ExplorationAISettings Settings => settings;
    public int KnownRouteNodesExpanded => knownRoute.NodesExpanded;
    public int KnownRouteBuildCount => knownRoute.TotalRouteBuilds;
    public int KnownRouteCacheCount => knownRoute.CachedRouteCount;
    public AIExplorationPlanner(ExplorationAISettings settings = null)
    {
        this.settings = settings != null ? settings : ExplorationAISettings.Active;
        telemetry = new ExplorationTelemetry(this.settings); knownRoute = new ExplorationKnownRoute(this.settings);
    }

    public void Update(ExplorationObservation next, bool isEmergency)
    {
        if (!settings.Enabled || next == null || next.Width <= 0 || next.Depth <= 0) return;
        int incomingActors = ActorStamp(next.Actors);
        if (observation != null && actorTeam == next.ActorTeam && turn == next.Turn && width == next.Width && depth == next.Depth
            && viewVersion == next.ViewVersion && cellVersion == next.CellVersion && dangerVersion == next.DangerVersion && actorVersion == incomingActors && emergency == isEmergency) return;
        bool newTurn = turn != next.Turn;
        bool dimensionsChanged = width != next.Width || depth != next.Depth;
        bool factionChanged = memory.Count > 0 && actorTeam != next.ActorTeam;
        if ((dimensionsChanged || factionChanged) && memory.Count > 0) ResetKnowledge();
        observation = next; width = next.Width; depth = next.Depth; turn = next.Turn; actorTeam = next.ActorTeam;
        viewVersion = next.ViewVersion; cellVersion = next.CellVersion; actorVersion = incomingActors;
        dangerVersion = next.DangerVersion;
        bool resume = emergency && !isEmergency;
        emergency = isEmergency;
        Observe(next);
        PrepareActors(next);
        if (dimensionsChanged || factionChanged || dirtyRegions.Count > 0 || newTurn && turn - lastFrontierRefresh >= Mathf.Max(1, settings.FrontierRefreshTurns)) RefreshFrontiers();
        ExpireCooldowns();
        reservedRegions.Clear();
        for (int i = 0; i < sortedActors.Count; i++)
        {
            var actor = sortedActors[i];
            if (objectives.TryGetValue(actor.ActorLifeId, out var objective))
            {
                objective.AssignedUnitId = actor.UnitId;
                if (objective.State == ObjectiveState.Active || objective.State == ObjectiveState.Suspended) reservedRegions.Add(objective.FrontierRegionId);
            }
        }
        for (int i = 0; i < sortedActors.Count; i++) MaintainObjective(sortedActors[i], newTurn, resume);
    }

    void Observe(ExplorationObservation next)
    {
        newlyRevealedSet.Clear(); newlyRevealedByRegion.Clear(); revealCredit.Clear();
        previousVisible.Clear(); foreach (var cell in visible) previousVisible.Add(cell);
        foreach (var oldVisible in visible)
            if (memory.TryGetValue(oldVisible, out var old)) { old.State = ExplorationKnowledgeState.SeenBefore; memory[oldVisible] = old; }
        visible.Clear();
        for (int i = 0; i < next.KnownCells.Count; i++)
        {
            var observed = next.KnownCells[i]; var cell = XZ(observed.Cell);
            if (!Inside(cell)) continue;
            bool added = !memory.TryGetValue(cell, out var value);
            if (added)
            {
                value = new ExplorationCellMemory { State = ExplorationKnowledgeState.SeenBefore, LastSeenTurn = observed.SeenTurn >= 0 ? observed.SeenTurn : turn, LastVisitedTurn = -1, RevealCount = 1 };
                memory[cell] = value; GetRegion(RegionId(cell)).Cells.Add(cell); AddRevealed(cell);
            }
            else if (observed.SeenTurn >= 0 && observed.SeenTurn > value.LastSeenTurn)
            { value.LastSeenTurn = Mathf.Min(turn, observed.SeenTurn); memory[cell] = value; }
            bool wasBlocked = blocked.Contains(cell);
            bool changed = added || wasBlocked == observed.Passable || !terrain.TryGetValue(cell, out var previous) || previous.y != observed.Cell.y;
            terrain[cell] = observed.Cell;
            if (observed.Passable) blocked.Remove(cell); else blocked.Add(cell);
            if (changed) { ObservationCellsProcessed++; MarkDirtyAround(cell); }
        }
        for (int i = 0; i < next.VisibleCells.Count; i++)
        {
            var cell = XZ(next.VisibleCells[i]); if (!Inside(cell)) continue;
            if (!memory.TryGetValue(cell, out var value))
            {
                value = new ExplorationCellMemory { LastVisitedTurn = -1, RevealCount = 1 };
                GetRegion(RegionId(cell)).Cells.Add(cell); AddRevealed(cell); ObservationCellsProcessed++; MarkDirtyAround(cell);
            }
            value.State = ExplorationKnowledgeState.VisibleNow; value.LastSeenTurn = turn; memory[cell] = value; visible.Add(cell);
            if (!previousVisible.Contains(cell) && !newlyRevealedSet.Contains(cell))
            { value.RevealCount = (ushort)Mathf.Min(ushort.MaxValue, value.RevealCount + 1); memory[cell] = value; }
        }
        foreach (var important in next.ImportantCells)
        {
            var cell = XZ(important.Key);
            if (memory.ContainsKey(cell))
            {
                float value = Mathf.Max(0, important.Value);
                if (!importance.TryGetValue(cell, out var previous) || previous != value) { importance[cell] = value; dirtyRegions.Add(RegionId(cell)); }
            }
        }
    }

    void PrepareActors(ExplorationObservation next)
    {
        actors.Clear(); sortedActors.Clear(); liveLifeIds.Clear();
        for (int i = 0; i < next.Actors.Count; i++)
        {
            var actor = next.Actors[i];
            if (actor.Kind != Kind.Scout || actor.HP <= 0 || !Inside(XZ(actor.Cell))) continue;
            if (string.IsNullOrEmpty(actor.ActorLifeId)) actor.ActorLifeId = actor.UnitId.ToString();
            actors[actor.UnitId] = actor; sortedActors.Add(actor); liveLifeIds.Add(actor.ActorLifeId);
            if (!histories.ContainsKey(actor.ActorLifeId)) histories[actor.ActorLifeId] = new ExplorationMovementHistory(settings.RecentHistoryTurns);
            int reveals = 0; int radius = Mathf.Clamp(settings.RevealCreditRadius, 1, 128);
            int minX = Mathf.Max(0, actor.Cell.x - radius) / RegionSize, maxX = Mathf.Min(width - 1, actor.Cell.x + radius) / RegionSize;
            int minZ = Mathf.Max(0, actor.Cell.z - radius) / RegionSize, maxZ = Mathf.Min(depth - 1, actor.Cell.z + radius) / RegionSize;
            for (int rz = minZ; rz <= maxZ; rz++) for (int rx = minX; rx <= maxX; rx++)
                if (newlyRevealedByRegion.TryGetValue(rz * RegionColumns + rx, out var localReveals))
                    for (int c = 0; c < localReveals.Count; c++) if (Distance(actor.Cell, localReveals[c]) <= radius) reveals++;
            revealCredit[actor.UnitId] = reveals;
            var cell = XZ(actor.Cell);
            if (memory.TryGetValue(cell, out var value) && value.LastVisitedTurn != turn)
            {
                value.LastVisitedTurn = turn; value.VisitCount = (ushort)Mathf.Min(ushort.MaxValue, value.VisitCount + 1); memory[cell] = value;
                var region = GetRegion(RegionId(cell)); region.LastVisitedTurn = turn; region.VisitCount = Mathf.Min(ushort.MaxValue, region.VisitCount + 1);
            }
        }
        sortedActors.Sort((a, b) => { int result = StringComparer.Ordinal.Compare(a.ActorLifeId, b.ActorLifeId); return result != 0 ? result : a.UnitId.CompareTo(b.UnitId); });
        removedLifeIds.Clear();
        foreach (var pair in objectives)
            if (!liveLifeIds.Contains(pair.Key))
            {
                if (pair.Value.State == ObjectiveState.Active || pair.Value.State == ObjectiveState.Suspended)
                    Finish(pair.Value, ObjectiveState.Failed, "unit_lost", default(ExplorationActor));
                removedLifeIds.Add(pair.Key);
            }
        foreach (var id in removedLifeIds)
        {
            if (objectives.TryGetValue(id, out var lost)) knownRoute.Remove(lost.AssignedUnitId);
            objectives.Remove(id); histories.Remove(id); lastCandidates.Remove(id);
        }
        removedLifeIds.Clear();
        foreach (var pair in histories) if (!liveLifeIds.Contains(pair.Key)) removedLifeIds.Add(pair.Key);
        foreach (var id in removedLifeIds) { histories.Remove(id); lastCandidates.Remove(id); }
    }

    void MaintainObjective(ExplorationActor actor, bool newTurn, bool resume)
    {
        objectives.TryGetValue(actor.ActorLifeId, out var objective);
        var history = histories[actor.ActorLifeId];
        if (objective != null && objective.RebaseProgressOnNextObservation)
        {
            objective.BestDistance = objective.LastDistance = ProgressDistance(actor.Cell, objective.TargetCell);
            history.RebaseDistances(objective.LastDistance);
            objective.RebaseProgressOnNextObservation = false;
        }
        if (emergency)
        {
            if (objective != null && objective.State == ObjectiveState.Active)
            {
                objective.State = ObjectiveState.Suspended; objective.SuspendedTurn = turn; objective.LastReason = "emergency_suspend";
                RecordDecision(actor, objective, history, "emergency_suspend", false, null);
            }
            return;
        }
        if (!actor.CanAct)
        {
            if (objective != null && (objective.State == ObjectiveState.Active || objective.State == ObjectiveState.Suspended)) Finish(objective, ObjectiveState.Failed, "unit_lost", actor);
            return;
        }
        if (objective != null && objective.State == ObjectiveState.Suspended)
        {
            objective.LastProgressTurn += Mathf.Max(0, turn - objective.SuspendedTurn);
            objective.State = ObjectiveState.Active; objective.LastSampleTurn = turn - 1; history.Clear();
        }
        if (objective != null && objective.State == ObjectiveState.Active)
        {
            float distance = MeasureProgress(actor, objective.TargetCell, out bool hasRoute, out int routeRevision, out var waypoint);
            SynchronizeProgressMetric(objective, history, distance, hasRoute, routeRevision, waypoint);
            int reveals = revealCredit.TryGetValue(actor.UnitId, out var revealed) ? revealed : 0;
            objective.NewlyRevealedSinceStart += reveals;
            var region = FindFrontier(objective.FrontierRegionId);
            int unknown = region != null ? region.UnknownCellsBehind : 0;
            if (reveals > 0 || distance < objective.BestDistance - settings.ProgressDistanceThreshold || unknown < objective.LastUnknownCount)
            { objective.LastProgressTurn = turn; objective.BestDistance = Mathf.Min(distance, objective.BestDistance); }
            objective.LastUnknownCount = unknown;
            history.Add(actor.Cell, turn, reveals, distance);
            bool loop = history.Count >= Mathf.Max(4, settings.LoopMinimumSamples) && history.UniqueCellsInWindow <= Mathf.Max(1, settings.LoopUniqueCellLimit)
                && history.RepeatedCellInWindow && history.NewlyRevealedInWindow == 0 && history.DistanceProgressInWindow <= settings.ProgressDistanceThreshold;
            if (region == null || objective.IsIntelRevisit && memory.TryGetValue(XZ(objective.TargetCell), out var intelMemory) && IsFresh(intelMemory.LastSeenTurn)) Finish(objective, ObjectiveState.Completed, "frontier_completed", actor);
            else if (Distance(actor.Cell, objective.TargetCell) <= Mathf.Max(0, settings.CompletionDistance)
                && (objective.NewlyRevealedSinceStart >= Mathf.Max(1, settings.CompletionRevealCells) || objective.IsIntelRevisit && visible.Contains(XZ(objective.TargetCell))))
                Finish(objective, ObjectiveState.Completed, "frontier_completed", actor);
            else if (observation.CanReach != null && !observation.CanReach(actor.UnitId, objective.TargetCell)) Finish(objective, ObjectiveState.Failed, "unreachable", actor);
            else if (loop)
            {
                cooldowns[objective.FrontierRegionId] = turn + Mathf.Max(1, settings.LoopCooldownTurns);
                cooldowns[RegionId(actor.Cell)] = turn + Mathf.Max(1, settings.LoopCooldownTurns);
                RecordDecision(actor, objective, history, "loop_detected", true, null);
                Finish(objective, ObjectiveState.Failed, "loop_detected", actor, false);
            }
            else if (turn - objective.LastProgressTurn >= Mathf.Max(1, settings.StallTurns))
            {
                cooldowns[objective.FrontierRegionId] = turn + Mathf.Max(1, settings.StallTurns);
                Finish(objective, ObjectiveState.Failed, "objective_stalled", actor);
            }
            else if (newTurn) RecordDecision(actor, objective, history, resume ? "emergency_resume" : "objective_progress", false, null);
            objective.LastDistance = distance; objective.LastSampleTurn = turn;
        }
        if (objective == null || objective.State == ObjectiveState.Failed || objective.State == ObjectiveState.Completed)
            Assign(actor, history);
    }

    void Assign(ExplorationActor actor, ExplorationMovementHistory history)
    {
        ExplorationFrontierRegion best = null; float bestScore = float.NegativeInfinity;
        var candidates = new List<ExplorationCandidateScore>();
        // Only regions with observed entrances are candidates. No map-wide unknown-cell traversal.
        var shortlist = new List<ExplorationFrontierRegion>(frontiers);
        shortlist.Sort((a, b) =>
        {
            float av = PreliminaryScore(a, actor), bv = PreliminaryScore(b, actor);
            int order = bv.CompareTo(av); return order != 0 ? order : a.RegionId.CompareTo(b.RegionId);
        });
        int limit = Mathf.Clamp(settings.MaxCandidateRegions, 1, 512);
        LastCandidateCount = Mathf.Min(limit, shortlist.Count);
        for (int i = 0; i < LastCandidateCount; i++)
        {
            var candidate = shortlist[i]; var score = ScoreFrontier(candidate, actor, history); candidates.Add(score);
            if (!string.IsNullOrEmpty(score.RejectReason)) continue;
            // Travel is a relative cost, never a distance horizon that removes a safe unknown frontier.
            if (score.Score < settings.MinimumFrontierScore && candidate.UnknownCellsBehind <= 0) continue;
            if (score.Score > bestScore || score.Score == bestScore && (best == null || candidate.RegionId < best.RegionId))
            { best = candidate; bestScore = score.Score; }
        }
        if (best == null)
        { RecordDecision(actor, null, history, "no_safe_frontier", false, candidates); return; }
        float initialDistance = MeasureProgress(actor, best.RepresentativeCell, out bool hasRoute, out int routeRevision, out var waypoint);
        var objective = new ExploreObjective
        {
            ObjectiveId = nextObjectiveId++, FrontierRegionId = best.RegionId, TargetCell = best.RepresentativeCell,
            AssignedUnitId = actor.UnitId, ActorLifeId = actor.ActorLifeId, CreatedTurn = turn, LastProgressTurn = turn,
            State = ObjectiveState.Active, BestDistance = initialDistance, LastDistance = initialDistance,
            LastUnknownCount = best.UnknownCellsBehind, LastSampleTurn = turn, IsIntelRevisit = best.IsIntelRevisit, LastReason = "objective_assigned",
            UsesKnownRouteDistance = hasRoute, RouteMetricRevision = routeRevision, RouteWaypoint = waypoint
        };
        objectives[actor.ActorLifeId] = objective; reservedRegions.Add(best.RegionId);
        best.LastAssignedTurn = turn; regions[best.RegionId].LastAssignedTurn = turn;
        history.Clear(); history.Add(actor.Cell, turn, 0, objective.BestDistance);
        RecordDecision(actor, objective, history, "objective_assigned", false, candidates);
    }

    float PreliminaryScore(ExplorationFrontierRegion frontier, ExplorationActor actor)
        => settings.UnknownGainWeight * Mathf.Sqrt(frontier.UnknownCellsBehind) + IntelValue(frontier) - Distance(actor.Cell, frontier.RepresentativeCell) * settings.TravelCostWeight
            - (reservedRegions.Contains(frontier.RegionId) ? settings.OtherScoutPenalty : 0) - (cooldowns.ContainsKey(frontier.RegionId) ? settings.LoopAreaPenalty : 0);

    ExplorationCandidateScore ScoreFrontier(ExplorationFrontierRegion frontier, ExplorationActor actor, ExplorationMovementHistory history)
    {
        var score = new ExplorationCandidateScore { RegionId = frontier.RegionId };
        float damage = observation.EstimateDanger != null ? Mathf.Max(0, observation.EstimateDanger(actor.UnitId, frontier.RepresentativeCell)) : 0;
        frontier.EstimatedDanger = damage / Mathf.Max(1, actor.HP);
        frontier.Reachable = !blocked.Contains(XZ(frontier.RepresentativeCell)) && (observation.CanReach == null || observation.CanReach(actor.UnitId, frontier.RepresentativeCell));
        memory.TryGetValue(XZ(frontier.RepresentativeCell), out var cellMemory);
        var regionVisits = regions[frontier.RegionId];
        int age = regionVisits.LastVisitedTurn < 0 ? int.MaxValue : Mathf.Max(0, turn - regionVisits.LastVisitedTurn);
        score.UnknownGain = settings.UnknownGainWeight * Mathf.Sqrt(Mathf.Max(0, frontier.UnknownCellsBehind));
        score.StaleIntel = IntelValue(frontier);
        score.DirectionDiversity = settings.DirectionDiversityWeight * history.DirectionNovelty(actor.Cell, frontier.RepresentativeCell);
        score.StrategicRoute = settings.StrategicRouteWeight * frontier.Importance;
        score.DistanceFromRecentArea = settings.DistanceFromRecentAreaWeight * history.DistanceFromRecentArea(frontier.RepresentativeCell);
        score.TravelCost = settings.TravelCostWeight * Distance(actor.Cell, frontier.RepresentativeCell);
        score.DangerCost = settings.DangerWeight * frontier.EstimatedDanger;
        score.RecentVisit = settings.RecentVisitPenalty * Mathf.Clamp01(1f - age / (float)Mathf.Max(1, settings.RecentVisitTurns));
        score.RepeatVisit = settings.RepeatVisitPenalty * Mathf.Min(regionVisits.VisitCount, Mathf.Max(1, settings.VisitCountCap));
        score.OtherScout = reservedRegions.Contains(frontier.RegionId) ? settings.OtherScoutPenalty : 0;
        score.LoopArea = cooldowns.ContainsKey(frontier.RegionId) ? settings.LoopAreaPenalty : 0;
        score.Score = score.UnknownGain + score.StaleIntel + score.DirectionDiversity + score.StrategicRoute + score.DistanceFromRecentArea
            - score.TravelCost - score.DangerCost - score.RecentVisit - score.RepeatVisit - score.OtherScout - score.LoopArea;
        if (!frontier.Reachable) score.RejectReason = "frontier_unreachable";
        else if (frontier.EstimatedDanger >= Mathf.Max(0, settings.MaximumFrontierDangerFraction)) score.RejectReason = "danger_too_high";
        else if (score.OtherScout > 0) score.RejectReason = "frontier_already_assigned";
        else if (score.LoopArea > 0) score.RejectReason = "loop_area_penalty";
        else if (frontier.IsIntelRevisit && IsFresh(frontier.OldestSeenTurn)) score.RejectReason = "recently_explored";
        else if (score.UnknownGain <= 0 && score.StaleIntel <= 0) score.RejectReason = "no_unknown_gain";
        return score;
    }

    float IntelValue(ExplorationFrontierRegion frontier)
    {
        if (frontier.Importance <= 0 || frontier.OldestSeenTurn < 0) return 0;
        if (memory.TryGetValue(XZ(frontier.ImportantCell), out var latest)) frontier.OldestSeenTurn = latest.LastSeenTurn;
        int age = Mathf.Max(0, turn - frontier.OldestSeenTurn);
        if (age <= Mathf.Max(0, settings.FreshIntelTurns)) return 0;
        float value = age <= Mathf.Max(settings.FreshIntelTurns, settings.LowIntelTurns) ? settings.LowIntelValue
            : age <= Mathf.Max(settings.LowIntelTurns, Mathf.Max(settings.MediumIntelTurns, settings.StaleIntelTurns)) ? settings.MediumIntelValue : settings.HighIntelValue;
        return settings.StaleIntelWeight * frontier.Importance * value;
    }
    bool IsFresh(int lastSeen) => lastSeen >= 0 && turn - lastSeen <= Mathf.Max(0, settings.FreshIntelTurns);

    void RefreshFrontiers()
    {
        if (turn - lastFrontierRefresh >= Mathf.Max(1, settings.FrontierRefreshTurns)) foreach (var id in regions.Keys) dirtyRegions.Add(id);
        sortedRegionIds.Clear(); foreach (int id in dirtyRegions) sortedRegionIds.Add(id); sortedRegionIds.Sort();
        for (int i = 0; i < sortedRegionIds.Count; i++)
        {
            int id = sortedRegionIds[i]; if (!regions.TryGetValue(id, out var cache)) continue;
            cache.FrontierCells.Clear(); Vector3Int representative = default(Vector3Int), intelRepresentative = default(Vector3Int);
            bool found = false, foundIntel = false; float farthest = float.NegativeInfinity, bestImportance = 0;
            int oldest = int.MaxValue; int unknownNear = 0;
            foreach (var cell in cache.Cells)
            {
                if (blocked.Contains(cell) || !terrain.ContainsKey(cell)) continue;
                bool frontier = false;
                for (int n = 0; n < neighbours.Length; n++) if (Inside(cell + neighbours[n]) && !memory.ContainsKey(cell + neighbours[n])) { frontier = true; unknownNear++; }
                if (frontier)
                {
                    cache.FrontierCells.Add(cell); float distance = Distance(cell, observation.OwnBase);
                    if (!found || distance > farthest || distance == farthest && CompareCell(cell, representative) < 0)
                    { representative = cell; farthest = distance; found = true; }
                }
                if (importance.TryGetValue(cell, out float value) && value > 0)
                {
                    int lastSeen = memory[cell].LastSeenTurn;
                    if (!foundIntel || value > bestImportance || value == bestImportance && (lastSeen < oldest || lastSeen == oldest && CompareCell(cell, intelRepresentative) < 0))
                    { foundIntel = true; bestImportance = value; oldest = lastSeen; intelRepresentative = cell; }
                }
            }
            if (!found && !foundIntel) { cache.Frontier = null; FrontierRegionRebuildCount++; continue; }
            int size = RegionSize, rx = id % RegionColumns, rz = id / RegionColumns;
            int area = Mathf.Min(size, width - rx * size) * Mathf.Min(size, depth - rz * size);
            cache.Frontier = new ExplorationFrontierRegion
            {
                RegionId = id, RepresentativeCell = terrain[found ? representative : intelRepresentative],
                UnknownCellsBehind = found ? Mathf.Max(unknownNear, area - cache.Cells.Count) : 0,
                FrontierCellCount = cache.FrontierCells.Count, LastAssignedTurn = cache.LastAssignedTurn,
                Importance = bestImportance, OldestSeenTurn = foundIntel ? oldest : -1, ImportantCell = foundIntel ? intelRepresentative : default(Vector3Int), IsIntelRevisit = !found,
                Reachable = true
            };
            FrontierRegionRebuildCount++;
        }
        dirtyRegions.Clear(); frontiers.Clear();
        foreach (var region in regions.Values) if (region.Frontier != null) frontiers.Add(region.Frontier);
        frontiers.Sort((a, b) => a.RegionId.CompareTo(b.RegionId)); lastFrontierRefresh = turn;
    }

    public ExploreObjective GetObjective(int unitId)
    { return actors.TryGetValue(unitId, out var actor) && objectives.TryGetValue(actor.ActorLifeId, out var result) ? result : null; }
    public ExplorationMovementHistory GetHistory(int unitId)
    { return actors.TryGetValue(unitId, out var actor) && histories.TryGetValue(actor.ActorLifeId, out var result) ? result : null; }
    public bool TryGetAssignedTarget(int unitId, out Vector3Int target)
    {
        var objective = GetObjective(unitId); target = objective != null ? objective.TargetCell : default(Vector3Int);
        return settings.Enabled && objective != null && objective.State == ObjectiveState.Active && !emergency;
    }
    public float GetMoveBonus(int unitId, Vector3Int destination, float estimatedDamage = 0)
    {
        if (!settings.Enabled || !actors.TryGetValue(unitId, out var actor) || !actor.CanAct || actor.Kind != Kind.Scout || emergency) return 0;
        float risk = Mathf.Max(0, estimatedDamage) / Mathf.Max(1, actor.HP);
        float score = -risk * settings.MoveDangerWeight;
        if (risk >= 1) score -= settings.SuicideMovePenalty;
        if (TryGetAssignedTarget(unitId, out var target))
        {
            float progress = GoalProgress(actor, target, destination, out bool hasRoute, out bool followsRoute);
            score += Mathf.Clamp(progress, -settings.MaximumDistanceProgressBonus, settings.MaximumDistanceProgressBonus) * settings.TargetDistanceWeight;
            if (hasRoute && !followsRoute) score -= Mathf.Max(0, settings.RouteDeviationPenalty);
            // A safe one-edge merge may preserve route cost in either direction. Prefer the goal
            // only while that route metric is tied; this is not actual exploration progress.
            if (hasRoute && followsRoute && Mathf.Abs(progress) < settings.ProgressDistanceThreshold)
            {
                float direction = ProgressDistance(actor.Cell, target) - ProgressDistance(destination, target);
                score += Mathf.Clamp(direction * Mathf.Max(0, settings.RouteTieDirectionWeight), -4f, 4f);
            }
        }
        return score;
    }
    public float GetGoalProgress(int unitId, Vector3Int destination)
    {
        if (!actors.TryGetValue(unitId, out var actor) || !actor.CanAct || !TryGetAssignedTarget(unitId, out var target)) return 0;
        return GoalProgress(actor, target, destination, out var _, out var _);
    }
    float GoalProgress(ExplorationActor actor, Vector3Int target, Vector3Int destination, out bool hasRoute, out bool followsRoute)
    {
        float current = MeasureProgress(actor, target, out hasRoute, out var _, out var _);
        if (hasRoute)
        {
            followsRoute = knownRoute.TryMeasureDestination(actor.UnitId, destination, out float remaining);
            return followsRoute ? current - remaining : 0;
        }
        followsRoute = false;
        return current - ProgressDistance(destination, target);
    }
    float MeasureProgress(ExplorationActor actor, Vector3Int target, out bool hasRoute, out int revision, out Vector3Int waypoint)
    {
        hasRoute = knownRoute.TryMeasure(actor, target, terrain, blocked,
            actor.MovementOffsets ?? MovePatterns.Offsets(actor.Kind), !actor.MovementDirectionIndependent,
            MovePatterns.DirZ(actor.Direction), ObservationCellsProcessed, turn, observation != null ? observation.DangerVersion : 0,
            observation != null ? observation.EstimateDanger : null, out float remaining, out waypoint, width, depth);
        revision = hasRoute ? knownRoute.GetMetricRevision(actor.UnitId) : 0;
        if (!hasRoute) waypoint = target;
        return hasRoute ? remaining : ProgressDistance(actor.Cell, target);
    }
    void SynchronizeProgressMetric(ExploreObjective objective, ExplorationMovementHistory history, float distance, bool hasRoute, int revision, Vector3Int waypoint)
    {
        if (objective.UsesKnownRouteDistance != hasRoute || hasRoute && objective.RouteMetricRevision != revision)
        {
            objective.BestDistance = objective.LastDistance = distance;
            history.RebaseDistances(distance);
        }
        objective.UsesKnownRouteDistance = hasRoute; objective.RouteMetricRevision = revision; objective.RouteWaypoint = waypoint;
    }
    public void RecordAction(int unitId, Vector3Int destination, bool success)
    {
        var objective = GetObjective(unitId); if (objective == null || objective.State != ObjectiveState.Active) return;
        if (!success)
        {
            objective.FailedMoves++;
            if (objective.FailedMoves >= Mathf.Max(1, settings.FailedMoveLimit))
            { cooldowns[objective.FrontierRegionId] = turn + Mathf.Max(1, settings.StallTurns); Finish(objective, ObjectiveState.Failed, "unreachable", actors[unitId]); }
            return;
        }
        objective.FailedMoves = 0;
        var actor = actors[unitId]; actor.Cell = destination; actors[unitId] = actor;
        float distance = MeasureProgress(actor, objective.TargetCell, out bool hasRoute, out int routeRevision, out var waypoint);
        SynchronizeProgressMetric(objective, histories[actor.ActorLifeId], distance, hasRoute, routeRevision, waypoint);
        if (distance < objective.BestDistance - settings.ProgressDistanceThreshold) { objective.BestDistance = distance; objective.LastProgressTurn = turn; }
        if (memory.TryGetValue(XZ(destination), out var cell) && cell.LastVisitedTurn != turn)
        {
            cell.LastVisitedTurn = turn; cell.VisitCount = (ushort)Mathf.Min(ushort.MaxValue, cell.VisitCount + 1); memory[XZ(destination)] = cell;
            var region = GetRegion(RegionId(destination)); region.LastVisitedTurn = turn; region.VisitCount = Mathf.Min(ushort.MaxValue, region.VisitCount + 1);
        }
        histories[actor.ActorLifeId].Add(destination, turn, 0, distance);
    }
    void Finish(ExploreObjective objective, ObjectiveState state, string reason, ExplorationActor actor, bool log = true)
    {
        objective.State = state; objective.LastReason = reason; reservedRegions.Remove(objective.FrontierRegionId);
        if (log) RecordDecision(actor, objective, histories.TryGetValue(objective.ActorLifeId ?? "", out var history) ? history : null, reason, false, null);
    }
    void RecordDecision(ExplorationActor actor, ExploreObjective objective, ExplorationMovementHistory history, string reason, bool loop, List<ExplorationCandidateScore> candidates)
    {
        string identity = objective != null ? objective.ActorLifeId : actor.ActorLifeId;
        if (!string.IsNullOrEmpty(identity))
        {
            if (candidates != null) lastCandidates[identity] = candidates;
            else lastCandidates.TryGetValue(identity, out candidates);
        }
        int reveals = 0;
        if (history != null) foreach (var sample in history.Capture()) if (turn - sample.Turn < 3) reveals += sample.NewlyRevealed;
        telemetry.Record(new ExplorationDecisionRecord
        {
            Turn = turn, ActorTeam = actorTeam, Explorer = objective != null ? objective.AssignedUnitId : actor.UnitId,
            ActorLifeId = objective != null ? objective.ActorLifeId : actor.ActorLifeId,
            Objective = objective != null ? objective.ObjectiveId : 0, ObjectiveAge = objective != null ? turn - objective.CreatedTurn : 0,
            FrontierRegion = objective != null ? objective.FrontierRegionId : -1, TargetFrontier = objective != null ? objective.TargetCell : default(Vector3Int),
            PreviousDistance = objective != null ? objective.LastDistance : 0,
            DistanceToTarget = objective != null ? string.IsNullOrEmpty(actor.ActorLifeId) || objective.State == ObjectiveState.Suspended
                ? objective.LastDistance : MeasureProgress(actor, objective.TargetCell, out var _, out var _, out var _) : 0,
            NewlyRevealedLast3T = reveals, RecentUniqueCells = history != null ? history.UniqueCellsInWindow : 0,
            LoopSuspected = loop, Reason = reason, State = objective != null ? objective.State : ObjectiveState.Failed,
            Candidates = candidates ?? new List<ExplorationCandidateScore>()
        });
    }
    public void FlushTelemetry() => telemetry.Flush();
    RegionCache GetRegion(int id) { if (!regions.TryGetValue(id, out var cache)) regions[id] = cache = new RegionCache(); return cache; }
    ExplorationFrontierRegion FindFrontier(int id) => regions.TryGetValue(id, out var cache) ? cache.Frontier : null;
    void MarkDirtyAround(Vector3Int cell)
    { dirtyRegions.Add(RegionId(cell)); for (int n = 0; n < neighbours.Length; n++) if (Inside(cell + neighbours[n])) dirtyRegions.Add(RegionId(cell + neighbours[n])); }
    void AddRevealed(Vector3Int cell)
    {
        if (!newlyRevealedSet.Add(cell)) return;
        int id = RegionId(cell);
        if (!newlyRevealedByRegion.TryGetValue(id, out var cells)) newlyRevealedByRegion[id] = cells = new List<Vector3Int>();
        cells.Add(cell);
    }
    void ExpireCooldowns()
    {
        sortedRegionIds.Clear(); foreach (var cooldown in cooldowns) if (cooldown.Value <= turn) sortedRegionIds.Add(cooldown.Key);
        foreach (int id in sortedRegionIds) cooldowns.Remove(id);
    }
    int RegionSize => Mathf.Clamp(settings.RegionSize, 2, 128);
    int RegionColumns => Mathf.Max(1, (width + RegionSize - 1) / RegionSize);
    int RegionId(Vector3Int cell) => Mathf.Max(0, cell.z) / RegionSize * RegionColumns + Mathf.Max(0, cell.x) / RegionSize;
    bool Inside(Vector3Int cell) => cell.x >= 0 && cell.z >= 0 && cell.x < width && cell.z < depth;
    static Vector3Int XZ(Vector3Int cell) => new Vector3Int(cell.x, 0, cell.z);
    static float Distance(Vector3Int a, Vector3Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.z - b.z));
    // Both axes matter for progress: the shorter axis may shrink through a legal known corridor
    // while the Chebyshev travel estimate remains flat. Arrival and candidate costs use Distance.
    static float ProgressDistance(Vector3Int a, Vector3Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    static int CompareCell(Vector3Int a, Vector3Int b) { int x = a.x.CompareTo(b.x); return x != 0 ? x : a.z.CompareTo(b.z); }
    static int ActorStamp(List<ExplorationActor> list)
    {
        unchecked
        {
            int stamp = 0;
            foreach (var actor in list)
            {
                int offsets = 17;
                if (actor.MovementOffsets != null) for (int i = 0; i < actor.MovementOffsets.Count; i++) offsets = offsets * 31 + actor.MovementOffsets[i].GetHashCode();
                stamp ^= actor.UnitId * 397 ^ actor.Cell.GetHashCode() * 31 ^ actor.HP * 7 ^ (actor.CanAct ? 1 : 0)
                    ^ StableHash(actor.ActorLifeId) ^ offsets ^ (int)actor.Direction * 53 ^ (actor.MovementDirectionIndependent ? 1 : 0);
            }
            return stamp;
        }
    }
    static int StableHash(string value) { unchecked { int hash = 17; if (value != null) for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i]; return hash; } }
    void ResetKnowledge()
    { memory.Clear(); terrain.Clear(); blocked.Clear(); visible.Clear(); previousVisible.Clear(); importance.Clear(); regions.Clear(); dirtyRegions.Clear(); frontiers.Clear(); objectives.Clear(); histories.Clear(); cooldowns.Clear(); lastCandidates.Clear(); knownRoute.Clear(); }
}
