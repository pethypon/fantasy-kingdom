#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Deterministic R2 policies tested with legal value-only knowledge, without reading a hidden map.</summary>
public static class ExplorationR2CoreTests
{
    static int passed;
    static void Check(string name, bool result)
    {
        if (!result) throw new InvalidOperationException("[ExplorationR2] FAIL " + name);
        passed++; Debug.Log("[ExplorationR2] PASS " + name);
    }
    static Vector3Int Cell(int x, int z) => new Vector3Int(x, 0, z);
    static ExplorationObservation Corridor(bool unknown = true, int turn = 1)
    {
        var observation = new ExplorationObservation { Width = 128, Depth = 64, Turn = turn,
            CellVersion = 1, ViewVersion = 1, OwnBase = Cell(20, 32) };
        for (int x = 0; x < (unknown ? 90 : 128); x++)
            for (int z = 0; z < 64; z++) observation.KnownCells.Add(new ExplorationObservedCell(Cell(x, z), true, 0));
        observation.Actors.Add(new ExplorationActor(7, Cell(20, 32), actorLifeId: "scout-a"));
        return observation;
    }
    static AIExplorationPlanner Seed(ExplorationAISettings settings, ExplorationObservation observation)
    {
        var planner = new AIExplorationPlanner(settings);
        var actors = observation.Actors.ToArray(); int turn = observation.Turn;
        observation.Actors.Clear(); observation.Turn = 0; planner.Update(observation, false);
        observation.Actors.AddRange(actors); observation.Turn = turn; planner.Update(observation, false);
        return planner;
    }
    static void Move(ExplorationObservation observation, int turn, Vector3Int cell)
    {
        observation.Turn = turn;
        var actor = observation.Actors[0]; actor.Cell = cell; observation.Actors[0] = actor;
    }
    static bool Reason(AIExplorationPlanner planner, string reason) => planner.Diagnostics.Any(item => item.Reason == reason);

    public static void RunAll()
    {
        passed = 0;
        var settings = ScriptableObject.CreateInstance<ExplorationAISettings>();
        settings.EnableJsonlTelemetry = false;
        try
        {
            UnknownAndFreshIntel(settings);
            PersistenceAndLongRange(settings);
            StallAndLoops(settings);
            DistributedExplorers(settings);
            EmergencyAndKing(settings);
            DeterminismAndHiddenInformation(settings);
            ContinuationAndKnowledgeUpdates(settings);
            LargeMapWorkBound(settings);
            KnownRoutesAndChangingMetrics(settings);
            AlternateKnownRouteProgress(settings);
            BoundedRouteDirectionTie(settings);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
        Debug.Log("[ExplorationR2] " + passed + " core checks passed; mandatory 13 policies covered");
    }

    static void UnknownAndFreshIntel(ExplorationAISettings settings)
    {
        var observation = Corridor(); observation.ImportantCells[Cell(21, 32)] = 10;
        var planner = Seed(settings, observation); var objective = planner.GetObjective(7);
        Check("Exploration_PrefersUnknownOverRecentlySeen", objective != null && !objective.IsIntelRevisit && objective.TargetCell.x >= 89);
        observation = Corridor(false, 5); observation.ImportantCells[Cell(40, 32)] = 10;
        planner = Seed(settings, observation); objective = planner.GetObjective(7);
        Check("Exploration_DoesNotRevisitFreshIntelWithoutReason", objective == null || objective.State != ObjectiveState.Active);
        observation = Corridor(false, 31); observation.ImportantCells[Cell(40, 32)] = 10;
        planner = Seed(settings, observation); objective = planner.GetObjective(7);
        Check("Exploration_StaleImportantAreaCanBeRevisited", objective != null && objective.IsIntelRevisit && objective.State == ObjectiveState.Active);
        observation = Corridor(true, 31); observation.ImportantCells[Cell(89, 32)] = 1;
        planner = Seed(settings, observation); objective = planner.GetObjective(7);
        Check("unknown frontier remains preferred over stale-information opportunism", objective != null && !objective.IsIntelRevisit);
    }

    static void PersistenceAndLongRange(ExplorationAISettings settings)
    {
        var observation = Corridor(); var planner = Seed(settings, observation);
        var original = planner.GetObjective(7);
        Check("far frontier exists beyond a fifty-cell move horizon", original != null && Mathf.Abs(original.TargetCell.x - 20) >= 50);
        int objectiveId = original.ObjectiveId, region = original.FrontierRegionId; float distance = original.LastDistance;
        for (int turn = 2; turn <= 12; turn++)
        {
            // A proved route may turn diagonally; use its legal next waypoint instead of prescribing an unrelated straight line.
            Move(observation, turn, planner.GetObjective(7).RouteWaypoint); planner.Update(observation, false);
            var current = planner.GetObjective(7);
            Check("Exploration_KeepsSameFrontierAcrossTurns T" + turn, current != null && current.ObjectiveId == objectiveId
                && current.FrontierRegionId == region && current.State == ObjectiveState.Active);
            Check("Exploration_LongRangeObjectiveProgressesAcrossTurns T" + turn, current.LastDistance < distance);
            distance = current.LastDistance;
        }
        var beforeMove = observation.Actors[0].Cell;
        var nextWaypoint = planner.GetObjective(7).RouteWaypoint;
        float forward = planner.GetMoveBonus(7, nextWaypoint);
        float backward = planner.GetMoveBonus(7, beforeMove - (nextWaypoint - beforeMove));
        Check("known corridor transit receives progress bonus instead of a revisit block", forward > backward);
        Check("known threat prevents suicidal shortcut", planner.GetMoveBonus(7, nextWaypoint, 200) < forward - 100);
        var save = JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(planner.CaptureState()));
        var restored = new AIExplorationPlanner(settings); restored.RestoreState(save); restored.Update(observation, false);
        Check("saved long-range objective and recent progress survive continuation", restored.GetObjective(7)?.ObjectiveId == objectiveId
            && restored.TryGetAssignedTarget(7, out var restoredTarget) && restoredTarget == original.TargetCell);
    }

    static void StallAndLoops(ExplorationAISettings settings)
    {
        var observation = Corridor(); var planner = Seed(settings, observation); int original = planner.GetObjective(7).ObjectiveId;
        for (int turn = 2; turn <= 4; turn++) { Move(observation, turn, Cell(20, 31 + turn)); planner.Update(observation, false); }
        Check("Exploration_ReplansAfterStall", Reason(planner, "objective_stalled") && planner.GetObjective(7)?.ObjectiveId != original);
        observation = Corridor(); planner = Seed(settings, observation);
        for (int turn = 2; turn <= 8; turn++)
        {
            Move(observation, turn, Cell(20, turn % 2 == 0 ? 33 : 32)); planner.Update(observation, false);
            if (Reason(planner, "loop_detected")) break;
        }
        Check("Exploration_DetectsABABLoop", Reason(planner, "loop_detected") && planner.Diagnostics.Any(item => item.LoopSuspected));
        // Isolate the ring-history detector from the separately tested three-turn stall detector.
        settings.StallTurns = 20;
        try
        {
            observation = Corridor(); planner = Seed(settings, observation);
            for (int turn = 2; turn <= 8; turn++)
            {
                Move(observation, turn, Cell(20, 30 + (turn - 1) % 4)); planner.Update(observation, false);
            }
            Check("Exploration_DetectsSmallAreaCircling", Reason(planner, "loop_detected"));
            Check("loop telemetry reports limited unique cells without new reveal", planner.Diagnostics.Any(item => item.LoopSuspected
                && item.RecentUniqueCells <= 4 && item.NewlyRevealedLast3T == 0));
        }
        finally { settings.StallTurns = 3; }
        var history = new ExplorationMovementHistory(8);
        for (int turn = 1; turn <= 50; turn++) { history.Add(Cell(turn, 0), turn, 0, 50 - turn); history.Add(Cell(turn, 1), turn, 1, 50 - turn); }
        Check("movement history stays fixed-length with one sample per turn", history.Count <= 8 && history.Capture().Select(item => item.Turn).Distinct().Count() == history.Count);
    }

    static void DistributedExplorers(ExplorationAISettings settings)
    {
        var observation = Corridor(); observation.Actors.Add(new ExplorationActor(8, Cell(20, 32), actorLifeId: "scout-b"));
        var planner = Seed(settings, observation); var first = planner.GetObjective(7); var second = planner.GetObjective(8);
        Check("Exploration_TwoScoutsChooseDifferentFrontiers", first != null && second != null && first.FrontierRegionId != second.FrontierRegionId);
        Check("frontier reservations are consistent with assigned explorer IDs", first.AssignedUnitId == 7 && second.AssignedUnitId == 8);
        observation.EstimateDanger = (_, cell) => 999;
        var threatened = Seed(settings, observation);
        Check("high-risk-only frontiers allow hold instead of a suicide mission", threatened.GetObjective(7) == null
            || threatened.GetObjective(7).State != ObjectiveState.Active);
    }

    static void EmergencyAndKing(ExplorationAISettings settings)
    {
        var observation = Corridor(); var planner = Seed(settings, observation); var initial = planner.GetObjective(7);
        Move(observation, 2, Cell(23, 32)); planner.Update(observation, false);
        observation.Turn = 3; planner.Update(observation, true);
        Check("Exploration_EmergencySuspendsAndLaterResumes suspend", planner.GetObjective(7)?.State == ObjectiveState.Suspended
            && planner.GetObjective(7).ObjectiveId == initial.ObjectiveId && Reason(planner, "emergency_suspend"));
        Check("suspended objective contributes no exploration move preference", Mathf.Abs(planner.GetMoveBonus(7, Cell(24, 32))) < .001f);
        observation.Turn = 4; planner.Update(observation, true); observation.Turn = 5; planner.Update(observation, false);
        Check("Exploration_EmergencySuspendsAndLaterResumes resume", planner.GetObjective(7)?.State == ObjectiveState.Active
            && planner.GetObjective(7).ObjectiveId == initial.ObjectiveId);
        observation = Corridor(); observation.Actors[0] = new ExplorationActor(7, Cell(20, 32), Kind.King, actorLifeId: "king");
        planner = Seed(settings, observation);
        Check("Exploration_KingNeverAssignedExplorer", planner.GetObjective(7) == null && !planner.TryGetAssignedTarget(7, out _));
        observation = Corridor(); planner = Seed(settings, observation); observation.Actors.Clear(); observation.Turn = 2; planner.Update(observation, false);
        Check("lost explorer releases its frontier and records a reason", !planner.TryGetAssignedTarget(7, out _) && Reason(planner, "unit_lost"));
    }

    static void DeterminismAndHiddenInformation(ExplorationAISettings settings)
    {
        var first = Corridor(); var second = Corridor(); second.KnownCells.Reverse();
        var a = Seed(settings, first); var b = Seed(settings, second);
        Check("Exploration_SameStateSameFrontier", a.GetObjective(7)?.TargetCell == b.GetObjective(7)?.TargetCell
            && a.GetObjective(7)?.FrontierRegionId == b.GetObjective(7)?.FrontierRegionId);
        var legal = Corridor(); var known = new HashSet<Vector3Int>(legal.KnownCells.Select(item => item.Cell));
        int queries = 0;
        legal.EstimateDanger = (_, cell) =>
        {
            if (!known.Contains(cell)) throw new InvalidOperationException("Unknown terrain was queried as known evidence");
            queries++; return 0;
        };
        var c = Seed(settings, legal);
        Check("frontier scoring queries observed entrances instead of hidden terrain", queries > 0
            && c.GetObjective(7)?.TargetCell == a.GetObjective(7)?.TargetCell);
    }

    static void LargeMapWorkBound(ExplorationAISettings settings)
    {
        var observation = new ExplorationObservation { Width = 512, Depth = 512, Turn = 1, CellVersion = 1, ViewVersion = 1, OwnBase = Cell(10, 10) };
        foreach (var center in new[] { Cell(10, 10), Cell(70, 10) })
            for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++)
                observation.KnownCells.Add(new ExplorationObservedCell(center + Cell(dx, dz), true, 0));
        observation.CanReach = (_, cell) => cell.x >= 64;
        for (int i = 0; i < 12; i++) observation.Actors.Add(new ExplorationActor(100 + i, Cell(10, 10 + i), actorLifeId: "large-" + i));
        var planner = Seed(settings, observation); long cells = planner.ObservationCellsProcessed; long rebuilds = planner.FrontierRegionRebuildCount;
        Check("512-grid stores observed knowledge instead of allocating all unknown cells", planner.Memory.Count <= observation.KnownCells.Count && planner.Memory.Count < 100);
        Check("512-grid far frontier can be assigned without long-horizon combat search", planner.GetObjective(100) != null
            && planner.GetObjective(100).TargetCell.x >= 64);
        var watch = Stopwatch.StartNew(); long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) planner.Update(observation, false);
        for (int i = 0; i < 12000; i++) planner.GetMoveBonus(100 + i % 12, Cell(11, 11));
        watch.Stop(); bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check("unchanged same-frame observations never trigger repeated full knowledge scans", planner.ObservationCellsProcessed == cells
            && planner.FrontierRegionRebuildCount == rebuilds);
        Check("frontier candidate work is bounded independent of unknown map area", planner.LastCandidateCount <= settings.MaxCandidateRegions
            && planner.Diagnostics.Count <= settings.TelemetryMaxRecords);
        Check("512-grid cached updates and movement scoring avoid multi-second stalls", watch.Elapsed.TotalMilliseconds < 1500);
        Debug.Log($"[ExplorationR2Perf] grid=512x512 known={planner.Memory.Count} scouts=12 cachedUpdates=1000 moveScores=12000 ms={watch.Elapsed.TotalMilliseconds:F2} bytes={bytes} processedCells={cells} regionRebuilds={rebuilds}");
    }

    static void ContinuationAndKnowledgeUpdates(ExplorationAISettings settings)
    {
        var observation = Corridor(false, 5); observation.ImportantCells[Cell(40, 32)] = 1;
        var planner = Seed(settings, observation);
        Check("fresh intelligence is initially held without unknown gain", planner.GetObjective(7) == null);
        observation.Turn = 31; planner.Update(observation, false);
        Check("same known important region becomes eligible as intelligence ages", planner.GetObjective(7)?.IsIntelRevisit == true);
        observation = Corridor(); planner = Seed(settings, observation);
        observation.VisibleCells.Add(Cell(81, 31)); observation.Turn = 2; observation.ViewVersion++;
        planner.Update(observation, false);
        Check("sight and actual arrival have independent clocks", planner.Memory[Cell(81, 31)].LastSeenTurn == 2
            && planner.Memory[Cell(81, 31)].LastVisitedTurn == -1 && planner.Memory[Cell(20, 32)].LastVisitedTurn == 2);
        observation.VisibleCells[0] = Cell(82, 31); observation.ViewVersion++; planner.Update(observation, false);
        Check("same-count same-turn sight changes refresh the knowledge state", planner.Memory[Cell(81, 31)].State == ExplorationKnowledgeState.SeenBefore
            && planner.Memory[Cell(82, 31)].State == ExplorationKnowledgeState.VisibleNow);
        var previous = planner.GetObjective(7).ObjectiveId;
        var actor = observation.Actors[0]; actor.ActorLifeId = "new-life-same-runtime-id"; observation.Actors[0] = actor;
        planner.Update(observation, false);
        Check("reused runtime ID cannot inherit a previous actor life objective", planner.GetObjective(7) != null
            && planner.GetObjective(7).ObjectiveId != previous && planner.GetObjective(7).ActorLifeId == actor.ActorLifeId);
        var malformed = planner.CaptureState(); malformed.Version = 999;
        var guarded = new AIExplorationPlanner(settings); guarded.RestoreState(malformed);
        Check("unknown future exploration schema falls back without keeping corrupt objectives", guarded.Memory.Count == 0 && guarded.Objectives.Count == 0);
        var enemy = Corridor(); planner = Seed(settings, enemy); var save = planner.CaptureState();
        guarded.RestoreState(save);
        var player = new ExplorationObservation { ActorTeam = Team.Player, Width = 128, Depth = 64, Turn = 20, CellVersion = 1, ViewVersion = 1 };
        player.KnownCells.Add(new ExplorationObservedCell(Cell(1, 1), true, 20)); guarded.Update(player, false);
        Check("cross-faction observation never inherits another faction's explored memory", guarded.Memory.Count == 1
            && guarded.Memory.ContainsKey(Cell(1, 1)) && guarded.Objectives.Count == 0);
        var far = new ExplorationObservation { Width = 512, Depth = 512, Turn = 1, CellVersion = 1, ViewVersion = 1, OwnBase = Cell(10, 10) };
        far.KnownCells.Add(new ExplorationObservedCell(Cell(400, 10), true, 0));
        far.Actors.Add(new ExplorationActor(7, Cell(10, 10), actorLifeId: "far-only"));
        var farPlanner = Seed(settings, far);
        Check("a safe distant-only unknown frontier is not silently cut off by travel cost", farPlanner.TryGetAssignedTarget(7, out var farTarget)
            && farTarget.x == 400 && farPlanner.GetMoveBonus(7, Cell(11, 10)) > farPlanner.GetMoveBonus(7, Cell(9, 10)));
    }

    static ExplorationObservation DetourObservation()
    {
        var observation = new ExplorationObservation { Width = 12, Depth = 11, OwnBase = Cell(2, 4), Turn = 1, CellVersion = 1, ViewVersion = 1 };
        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++)
            observation.KnownCells.Add(new ExplorationObservedCell(Cell(x, z), !(x == 3 && z <= 8), 0));
        var actor = new ExplorationActor(7, Cell(2, 4), actorLifeId: "detour");
        actor.MovementOffsets = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        actor.MovementDirectionIndependent = true; observation.Actors.Add(actor); return observation;
    }

    static void KnownRoutesAndChangingMetrics(ExplorationAISettings settings)
    {
        var observation = DetourObservation(); var planner = Seed(settings, observation); var initial = planner.GetObjective(7);
        Check("known wall detour is proved from observed terrain", initial != null && initial.UsesKnownRouteDistance && initial.TargetCell.x == 10 && initial.TargetCell.z < 4);
        float previous = initial.LastDistance;
        for (int turn = 2; turn <= 5; turn++)
        {
            var goal = planner.GetObjective(7); var actor = observation.Actors[0];
            float oldManhattan = Mathf.Abs(actor.Cell.x - goal.TargetCell.x) + Mathf.Abs(actor.Cell.z - goal.TargetCell.z);
            var detourWaypoint = goal.RouteWaypoint;
            Check("safe detour may temporarily move away geometrically T" + turn,
                Mathf.Abs(detourWaypoint.x - goal.TargetCell.x) + Mathf.Abs(detourWaypoint.z - goal.TargetCell.z) > oldManhattan);
            Move(observation, turn, detourWaypoint); planner.Update(observation, false); goal = planner.GetObjective(7);
            Check("safe detour keeps goal and reduces proved route length T" + turn,
                goal.ObjectiveId == initial.ObjectiveId && goal.State == ObjectiveState.Active && goal.LastDistance < previous);
            previous = goal.LastDistance;
        }

        int oldMinimum = settings.LoopMinimumSamples;
        try
        {
            settings.LoopMinimumSamples = 9; observation = DetourObservation();
            float damage = 1; observation.EstimateDanger = (_, _) => damage;
            planner = Seed(settings, observation); var stalled = planner.GetObjective(7); int initialRevision = stalled.RouteMetricRevision;
            for (int turn = 2; turn <= 4; turn++) { damage = turn; observation.Turn = turn; observation.DangerVersion++; planner.Update(observation, false); }
            Check("route-cost rebasing cannot extend a stationary objective's progress clock", Reason(planner, "objective_stalled")
                && stalled.LastProgressTurn == 1 && stalled.RouteMetricRevision != initialRevision);
        }
        finally { settings.LoopMinimumSamples = oldMinimum; }
        observation = DetourObservation(); float currentDamage = 1; observation.EstimateDanger = (_, _) => currentDamage;
        planner = Seed(settings, observation);
        for (int turn = 2; turn <= 8; turn++)
        {
            currentDamage = turn; observation.DangerVersion++;
            Move(observation, turn, Cell(2, turn % 2 == 0 ? 5 : 4)); planner.Update(observation, false);
        }
        Check("changing danger costs do not erase the world-position ABAB loop", Reason(planner, "loop_detected"));
        var history = planner.GetHistory(7);
        Check("metric rebases retain bounded position samples", history != null && history.Count <= settings.RecentHistoryTurns);

        var terrain = new Dictionary<Vector3Int, Vector3Int>();
        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++) terrain[Cell(x, z)] = Cell(x, z);
        var blocked = new HashSet<Vector3Int>(); var offsets = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        var route = new ExplorationKnownRoute(settings); var unit = new ExplorationActor(10, Cell(0, 2));
        Func<int, Vector3Int, float> danger = (_, cell) =>
        {
            if (!terrain.ContainsKey(GridHelper.ToGridXZ(cell))) throw new InvalidOperationException("Route queried unknown evidence");
            return cell.x == 2 && cell.z == 2 ? 100 : 0;
        };
        Check("known-route danger evaluation avoids a lethal shortcut", route.TryMeasure(unit, Cell(4, 2), terrain, blocked, offsets, false, 1,
            1, 1, 1, danger, out float remaining, out var waypoint, 11, 11) && remaining > 4
            && !route.TryMeasureDestination(10, Cell(2, 2), out _));
        int builds = route.TotalRouteBuilds; var watch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            route.TryMeasure(unit, Cell(4, 2), terrain, blocked, offsets, false, 1, 1, 1, 1, danger, out _, out _, 11, 11);
            route.TryMeasureDestination(10, waypoint, out _);
        }
        watch.Stop();
        Check("candidate distance and cached routes do not launch another search", route.TotalRouteBuilds == builds && watch.Elapsed.TotalMilliseconds < 500);
        var gap = new Dictionary<Vector3Int, Vector3Int> { [Cell(0, 2)] = Cell(0, 2), [Cell(4, 2)] = Cell(4, 2) };
        var negative = new ExplorationKnownRoute(settings);
        Check("unknown gaps are unproved rather than invented blocked terrain", !negative.TryMeasure(unit, Cell(4, 2), gap, blocked, offsets, false, 1,
            1, 1, 1, null, out _, out _, 11, 11));
        int negativeMetric = negative.GetMetricRevision(10);
        negative.TryMeasure(unit, Cell(4, 2), gap, blocked, offsets, false, 1, 2, 2, 2, null, out _, out _, 11, 11);
        Check("unproved route revision changes preserve the fallback metric", negative.GetMetricRevision(10) == negativeMetric);
        route.TryMeasure(new ExplorationActor(11, Cell(0, 2)), Cell(4, 2), terrain, blocked, offsets, false, 1, 1, 1, 1, danger, out _, out _, 11, 11);
        int oldCache = settings.MaxCachedRoutes;
        try
        {
            settings.MaxCachedRoutes = 1;
            route.TryMeasure(unit, Cell(4, 2), terrain, blocked, offsets, false, 1, 1, 1, 1, danger, out _, out _, 11, 11);
            Check("shrinking route cache enforces the live limit", route.CachedRouteCount == 1);
        }
        finally { settings.MaxCachedRoutes = oldCache; }
        int oldNodes = settings.MaxRouteNodes;
        try
        {
            settings.MaxRouteNodes = 16; terrain.Clear();
            for (int x = 0; x <= 100; x++) terrain[Cell(x, 0)] = Cell(x, 0);
            var limited = new ExplorationKnownRoute(settings);
            Check("known-route node budget caps a long proof attempt", !limited.TryMeasure(new ExplorationActor(12, Cell(0, 0)), Cell(100, 0), terrain, blocked,
                new[] { Vector2Int.right }, false, 1, 1, 1, 1, null, out _, out _, 101, 1) && limited.NodesExpanded <= 16);
        }
        finally { settings.MaxRouteNodes = oldNodes; }
        Debug.Log($"[ExplorationKnownRoutePerf] cachedQueries=1000 ms={watch.Elapsed.TotalMilliseconds:F2} routeBuilds={builds}");
    }

    static void AlternateKnownRouteProgress(ExplorationAISettings settings)
    {
        var terrain = new Dictionary<Vector3Int, Vector3Int>();
        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++) terrain[Cell(x, z)] = Cell(x, z);
        var blocked = new HashSet<Vector3Int>();
        var offsets = new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        var route = new ExplorationKnownRoute(settings); var actor = new ExplorationActor(17, Cell(2, 2));
        Check("alternate-route fixture initially proves the straight shortest path", route.TryMeasure(actor, Cell(8, 2), terrain, blocked,
            offsets, false, 1, 1, 1, 1, null, out var initial, out var waypoint, 11, 11) && Mathf.Abs(initial - 6) < .001f && waypoint.z == 2);
        int revision = route.GetMetricRevision(17), builds = route.TotalRouteBuilds;
        actor.Cell = Cell(3, 3); // Two legal cardinal steps reach a different path without changing knowledge or danger.
        Check("a proved legal alternate path keeps the same distance-function identity", route.TryMeasure(actor, Cell(8, 2), terrain, blocked,
            offsets, false, 1, 1, 2, 1, null, out var alternate, out _, 11, 11) && route.TotalRouteBuilds > builds
            && route.GetMetricRevision(17) == revision && Mathf.Abs(alternate - initial) < .001f);
        actor.Cell = Cell(4, 3);
        Check("progress along an alternate path is measured without rebasing it away", route.TryMeasure(actor, Cell(8, 2), terrain, blocked,
            offsets, false, 1, 1, 3, 1, null, out var progressed, out _, 11, 11) && progressed < initial && route.GetMetricRevision(17) == revision);

        var observation = Corridor(); observation.CanReach = (_, cell) => cell.x == 89 && cell.z == 0;
        var planner = Seed(settings, observation); var objective = planner.GetObjective(7);
        Check("alternate exploration route begins with a proved northern objective", objective != null && objective.UsesKnownRouteDistance
            && objective.TargetCell == Cell(89, 0) && objective.RouteWaypoint != Cell(21, 32));
        int id = objective.ObjectiveId; float previous = objective.LastDistance;
        for (int turn = 2; turn <= 6; turn++)
        {
            var current = observation.Actors[0].Cell; var next = current + Cell(1, 0);
            Check("alternate exploration step is legal under the actor's movement mask T" + turn,
                MovePatterns.Map[Kind.Scout](next.x - current.x, next.z - current.z));
            Move(observation, turn, next); planner.Update(observation, false); objective = planner.GetObjective(7);
            Check("alternate legal route preserves real progress and avoids a false three-turn stall T" + turn,
                objective != null && objective.ObjectiveId == id && objective.State == ObjectiveState.Active
                && objective.LastDistance < previous && objective.LastProgressTurn == turn);
            previous = objective.LastDistance;
        }
    }

    static ExplorationObservation SmallRouteTieObservation()
    {
        var observation = new ExplorationObservation { Width = 12, Depth = 11, OwnBase = Cell(2, 4),
            Turn = 1, CellVersion = 1, ViewVersion = 1 };
        for (int x = 0; x <= 10; x++) for (int z = 0; z <= 10; z++)
            observation.KnownCells.Add(new ExplorationObservedCell(Cell(x, z), true, 0));
        observation.CanReach = (_, cell) => cell == Cell(10, 0);
        observation.Actors.Add(new ExplorationActor(7, Cell(2, 4), actorLifeId: "route-direction-tie"));
        return observation;
    }

    static void BoundedRouteDirectionTie(ExplorationAISettings settings)
    {
        float oldWeight = settings.RouteTieDirectionWeight;
        int oldLoopMinimum = settings.LoopMinimumSamples;
        try
        {
            settings.RouteTieDirectionWeight = 2; settings.LoopMinimumSamples = 9;
            var observation = SmallRouteTieObservation(); var planner = Seed(settings, observation);
            var objective = planner.GetObjective(7); var forward = Cell(3, 3); var backward = Cell(1, 3);
            Check("small route fixture has safe zero-progress merges beside a proved north2 waypoint", objective != null
                && objective.TargetCell == Cell(10, 0) && objective.UsesKnownRouteDistance && objective.RouteWaypoint == Cell(2, 2)
                && MovePatterns.CanMove(Kind.Scout, Direction.N, 1, -1) && MovePatterns.CanMove(Kind.Scout, Direction.N, -1, -1)
                && Mathf.Abs(planner.GetGoalProgress(7, forward)) < .001f && Mathf.Abs(planner.GetGoalProgress(7, backward)) < .001f);
            int lastProgressTurn = objective.LastProgressTurn;
            float initialDistance = objective.LastDistance, forwardScore = planner.GetMoveBonus(7, forward), backwardScore = planner.GetMoveBonus(7, backward);
            Check("direction resolves a safe route merge tie without changing the verified progress clock", forwardScore > backwardScore
                && forwardScore - backwardScore <= 4.001f && objective.LastProgressTurn == lastProgressTurn
                && Mathf.Abs(objective.LastDistance - initialDistance) < .001f);
            var primary = objective.RouteWaypoint;
            float primaryProgress = planner.GetGoalProgress(7, primary), primaryScore = planner.GetMoveBonus(7, primary);
            settings.RouteTieDirectionWeight = 0;
            float neutralForward = planner.GetMoveBonus(7, forward), neutralBackward = planner.GetMoveBonus(7, backward);
            Check("zero direction weight restores the tied score and preserves primary route scoring", Mathf.Abs(neutralForward - neutralBackward) < .001f
                && primaryProgress > 0 && Mathf.Abs(planner.GetMoveBonus(7, primary) - primaryScore) < .001f);
            settings.RouteTieDirectionWeight = 1000;
            Check("even a large authored direction weight remains bounded below a proved route advance", planner.GetMoveBonus(7, forward) - neutralForward <= 4.001f
                && planner.GetMoveBonus(7, forward) > neutralForward && planner.GetMoveBonus(7, primary) > planner.GetMoveBonus(7, forward)
                && Mathf.Abs(planner.GetMoveBonus(7, primary) - primaryScore) < .001f);
            Check("bounded direction cannot outweigh a lethal destination", planner.GetMoveBonus(7, forward, 100) < planner.GetMoveBonus(7, backward)
                && planner.GetMoveBonus(7, forward, 100) < -1000);

            var dangerObservation = SmallRouteTieObservation();
            dangerObservation.EstimateDanger = (_, cell) => GridHelper.ToGridXZ(cell) == forward ? 100 : 0;
            var dangerPlanner = Seed(settings, dangerObservation);
            settings.RouteTieDirectionWeight = 0; float dangerousNeutral = dangerPlanner.GetMoveBonus(7, forward, 100);
            settings.RouteTieDirectionWeight = 1000;
            Check("an unsafe merge never receives the route direction tie bonus", dangerPlanner.GetObjective(7)?.UsesKnownRouteDistance == true
                && Mathf.Abs(dangerPlanner.GetGoalProgress(7, forward)) < .001f
                && Mathf.Abs(dangerPlanner.GetMoveBonus(7, forward, 100) - dangerousNeutral) < .001f
                && dangerPlanner.GetMoveBonus(7, forward, 100) < dangerPlanner.GetMoveBonus(7, backward));

            settings.RouteTieDirectionWeight = 2;
            for (int turn = 2; turn <= 4; turn++)
            {
                for (int preview = 0; preview < 20; preview++) planner.GetMoveBonus(7, forward);
                observation.Turn = turn; planner.Update(observation, false);
            }
            Check("direction-only previews cannot postpone the stationary objective's three-turn stall", objective.LastProgressTurn == lastProgressTurn
                && Reason(planner, "objective_stalled") && observation.Actors[0].Cell == Cell(2, 4));
        }
        finally { settings.RouteTieDirectionWeight = oldWeight; settings.LoopMinimumSamples = oldLoopMinimum; }
    }
}
#endif
