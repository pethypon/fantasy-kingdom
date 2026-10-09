using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIExplorationPlanner
{
    public AIExplorationState CaptureState()
    {
        FlushTelemetry();
        var state = new AIExplorationState { Width = width, Depth = depth, Turn = turn, ActorTeam = actorTeam, NextObjectiveId = nextObjectiveId };
        var cells = new List<Vector3Int>(memory.Keys); cells.Sort(CompareCell);
        foreach (var cell in cells)
            state.Cells.Add(new ExplorationMemoryRecord { Cell = terrain.TryGetValue(cell, out var position) ? position : cell,
                Memory = memory[cell], Passable = !blocked.Contains(cell), Importance = importance.TryGetValue(cell, out var value) ? value : 0 });
        var identities = new List<string>(objectives.Keys); identities.Sort(System.StringComparer.Ordinal);
        foreach (var identity in identities) state.Objectives.Add(CloneObjective(objectives[identity]));
        identities = new List<string>(histories.Keys); identities.Sort(System.StringComparer.Ordinal);
        foreach (var identity in identities) state.Histories.Add(new ExplorationHistoryRecord { ActorLifeId = identity, Samples = histories[identity].Capture() });
        var regionIds = new List<int>(cooldowns.Keys); regionIds.Sort();
        foreach (var id in regionIds) state.LoopCooldowns.Add(new ExplorationRegionCooldown { RegionId = id, UntilTurn = cooldowns[id] });
        regionIds = new List<int>(regions.Keys); regionIds.Sort();
        foreach (var id in regionIds) state.Regions.Add(new ExplorationRegionState { RegionId = id, LastAssignedTurn = regions[id].LastAssignedTurn });
        return state;
    }
    public void RestoreState(AIExplorationState state)
    {
        ResetKnowledge(); actors.Clear(); sortedActors.Clear(); observation = null;
        ClearBoardCaches();
        viewVersion = cellVersion = actorVersion = dangerVersion = int.MinValue; lastFrontierRefresh = -1;
        if (state == null || (state.Version != 2 && state.Version != 3) || state.Width <= 0 || state.Depth <= 0
            || state.Width > Mathf.Clamp(settings.MaxMapDimension, 16, 32768) || state.Depth > Mathf.Clamp(settings.MaxMapDimension, 16, 32768)
            || state.ActorTeam != Team.Player && state.ActorTeam != Team.Enemy)
        { width = depth = 0; turn = -1; nextObjectiveId = 1; return; }
        width = state.Width; depth = state.Depth; turn = Mathf.Max(0, state.Turn); actorTeam = state.ActorTeam;
        nextObjectiveId = Mathf.Clamp(state.NextObjectiveId, 1, int.MaxValue - 1);
        int cellLimit = (int)System.Math.Min((long)width * depth, Mathf.Clamp(settings.MaxSavedCells, 1024, 4194304));
        if (state.Cells != null)
        for (int i = 0; i < state.Cells.Count && i < cellLimit; i++)
        {
            var entry = state.Cells[i]; if (entry == null || !Inside(XZ(entry.Cell))) continue;
            var cell = XZ(entry.Cell); var value = entry.Memory;
            // Visibility is refreshed from current faction observations after loading.
            value.State = ExplorationKnowledgeState.SeenBefore; value.LastSeenTurn = Mathf.Clamp(value.LastSeenTurn, -1, turn);
            value.LastVisitedTurn = Mathf.Clamp(value.LastVisitedTurn, -1, turn); memory[cell] = value; terrain[cell] = entry.Cell;
            if (!entry.Passable) blocked.Add(cell); if (Finite(entry.Importance) && entry.Importance > 0) importance[cell] = Mathf.Min(entry.Importance, 1000);
            var region = GetRegion(RegionId(cell)); region.Cells.Add(cell); region.LastVisitedTurn = Mathf.Max(region.LastVisitedTurn, value.LastVisitedTurn);
            region.VisitCount = Mathf.Min(ushort.MaxValue, region.VisitCount + value.VisitCount);
            dirtyRegions.Add(RegionId(cell)); importedTerrain.Add(cell);
        }
        int actorLimit = Mathf.Clamp(settings.MaxSavedActors, 1, 4096);
        if (state.Objectives != null)
        for (int i = 0; i < state.Objectives.Count && i < actorLimit; i++)
        {
            var objective = state.Objectives[i];
            if (objective == null || !ValidIdentity(objective.ActorLifeId) || !Inside(XZ(objective.TargetCell))
                || (int)objective.State < 0 || (int)objective.State > (int)ObjectiveState.Failed || !Finite(objective.BestDistance) || !Finite(objective.LastDistance)) continue;
            var copy = CloneObjective(objective); copy.CreatedTurn = Mathf.Clamp(copy.CreatedTurn, 0, turn);
            copy.LastProgressTurn = Mathf.Clamp(copy.LastProgressTurn, copy.CreatedTurn, turn); copy.LastSampleTurn = Mathf.Clamp(copy.LastSampleTurn, copy.CreatedTurn, turn);
            copy.SuspendedTurn = Mathf.Clamp(copy.SuspendedTurn, 0, turn); copy.BestDistance = Mathf.Max(0, copy.BestDistance); copy.LastDistance = Mathf.Max(0, copy.LastDistance);
            copy.FrontierRegionId = RegionId(copy.TargetCell); copy.FailedMoves = Mathf.Clamp(copy.FailedMoves, 0, Mathf.Max(1, settings.FailedMoveLimit));
            copy.NewlyRevealedSinceStart = Mathf.Max(0, copy.NewlyRevealedSinceStart);
            if (state.Version == 2) copy.RebaseProgressOnNextObservation = true;
            // Route revisions belong to the discarded runtime cache. A fresh cache may reuse
            // the saved number with different costs; rebind the distance basis without awarding progress.
            if (copy.UsesKnownRouteDistance) copy.RouteMetricRevision = -1;
            objectives[copy.ActorLifeId] = copy;
        }
        if (state.Histories != null)
        for (int i = 0; i < state.Histories.Count && i < actorLimit; i++)
        {
            var record = state.Histories[i]; if (record == null || !ValidIdentity(record.ActorLifeId)) continue;
            var history = new ExplorationMovementHistory(settings.RecentHistoryTurns);
            if (record.Samples != null)
            for (int s = Mathf.Max(0, record.Samples.Count - Mathf.Clamp(settings.RecentHistoryTurns, 2, 64)); s < record.Samples.Count; s++)
            {
                var sample = record.Samples[s];
                if (Inside(XZ(sample.Cell)) && sample.Turn >= 0 && sample.Turn <= turn && Finite(sample.Distance))
                    history.Add(sample.Cell, sample.Turn, Mathf.Clamp(sample.NewlyRevealed, 0, cellLimit),
                        state.Version == 2 && objectives.TryGetValue(record.ActorLifeId, out var objective)
                            ? ProgressDistance(sample.Cell, objective.TargetCell) : Mathf.Max(0, sample.Distance));
            }
            histories[record.ActorLifeId] = history;
        }
        int regionLimit = Mathf.Max(1, RegionColumns * ((depth + RegionSize - 1) / RegionSize));
        if (state.LoopCooldowns != null)
        for (int i = 0; i < state.LoopCooldowns.Count && i < regionLimit; i++)
        {
            var cooldown = state.LoopCooldowns[i];
            if (cooldown.RegionId >= 0 && cooldown.RegionId < regionLimit && cooldown.UntilTurn > turn)
                cooldowns[cooldown.RegionId] = (int)System.Math.Min(cooldown.UntilTurn, (long)turn + Mathf.Max(settings.LoopCooldownTurns, settings.StallTurns));
        }
        if (state.Regions != null)
        for (int i = 0; i < state.Regions.Count && i < regionLimit; i++)
        {
            var region = state.Regions[i];
            if (regions.TryGetValue(region.RegionId, out var cache)) cache.LastAssignedTurn = Mathf.Clamp(region.LastAssignedTurn, -1, turn);
        }
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool ValidIdentity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64;
    static ExploreObjective CloneObjective(ExploreObjective source) => new ExploreObjective
    {
        ObjectiveId = source.ObjectiveId, FrontierRegionId = source.FrontierRegionId, TargetCell = source.TargetCell,
        AssignedUnitId = source.AssignedUnitId, ActorLifeId = source.ActorLifeId, CreatedTurn = source.CreatedTurn,
        LastProgressTurn = source.LastProgressTurn, NewlyRevealedSinceStart = source.NewlyRevealedSinceStart, State = source.State,
        BestDistance = source.BestDistance, LastDistance = source.LastDistance, LastSampleTurn = source.LastSampleTurn,
        LastUnknownCount = source.LastUnknownCount, FailedMoves = source.FailedMoves, SuspendedTurn = source.SuspendedTurn,
        LastReason = source.LastReason, IsIntelRevisit = source.IsIntelRevisit,
        RebaseProgressOnNextObservation = source.RebaseProgressOnNextObservation,
        UsesKnownRouteDistance = source.UsesKnownRouteDistance, RouteMetricRevision = source.RouteMetricRevision, RouteWaypoint = source.RouteWaypoint
    };
}
