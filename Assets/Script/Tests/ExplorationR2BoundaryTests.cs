#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small independent boundary fixtures for saved clocks and legal observed-route evidence.</summary>
public static class ExplorationR2BoundaryTests
{
    static int passed;
    static readonly Vector2Int[] orthogonal = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
    static Vector3Int Cell(int x, int z) => new Vector3Int(x, 0, z);
    static void Check(string name, bool result)
    {
        if (!result) throw new InvalidOperationException("[ExplorationR2Boundary] FAIL " + name);
        passed++; Debug.Log("[ExplorationR2Boundary] PASS " + name);
    }
    public static void RunAll()
    {
        passed = 0;
        var settings = ScriptableObject.CreateInstance<ExplorationAISettings>();
        settings.EnableJsonlTelemetry = false;
        try
        {
            OldSchemaKeepsProgressAndSuspensionClocks(settings);
            UnprovedRouteKeepsMinorAxisProgress(settings);
            DiagonalRequiresKnownOpenCorners(settings);
            AsymmetricOffsetsRespectDirection(settings);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
        Debug.Log("[ExplorationR2Boundary] " + passed + " boundary checks passed");
    }

    static AIExplorationPlanner Seed(ExplorationAISettings settings, ExplorationObservation observation)
    {
        var planner = new AIExplorationPlanner(settings);
        var actors = observation.Actors.ToArray(); int originalTurn = observation.Turn;
        observation.Actors.Clear(); observation.Turn = 0; planner.Update(observation, false);
        observation.Actors.AddRange(actors); observation.Turn = originalTurn; planner.Update(observation, false);
        return planner;
    }
    static bool HasReason(AIExplorationPlanner planner, string reason)
    {
        foreach (var record in planner.Diagnostics) if (record.Reason == reason) return true;
        return false;
    }
    static ExplorationObservation ClockObservation()
    {
        var observation = new ExplorationObservation { Width = 64, Depth = 32, Turn = 1, OwnBase = Cell(5, 16), CellVersion = 1, ViewVersion = 1 };
        for (int x = 0; x <= 60; x++) for (int z = 0; z < 32; z++)
            observation.KnownCells.Add(new ExplorationObservedCell(Cell(x, z), true, 0));
        observation.Actors.Add(new ExplorationActor(7, observation.OwnBase, actorLifeId: "boundary-clock")
        { MovementOffsets = orthogonal, MovementDirectionIndependent = true });
        return observation;
    }
    static AIExplorationState AsOldSchema(AIExplorationState state, Vector3Int actorCell)
    {
        state.Version = 2;
        foreach (var objective in state.Objectives)
        {
            objective.BestDistance = objective.LastDistance = GridHelper.ChebyshevDistance(actorCell, objective.TargetCell);
            objective.UsesKnownRouteDistance = false; objective.RouteMetricRevision = 0;
            objective.RebaseProgressOnNextObservation = false;
            foreach (var history in state.Histories) if (history.ActorLifeId == objective.ActorLifeId)
                for (int i = 0; i < history.Samples.Count; i++)
                {
                    var sample = history.Samples[i]; sample.Distance = GridHelper.ChebyshevDistance(sample.Cell, objective.TargetCell);
                    history.Samples[i] = sample;
                }
        }
        return state;
    }
    static void OldSchemaKeepsProgressAndSuspensionClocks(ExplorationAISettings settings)
    {
        int oldLoopMinimum = settings.LoopMinimumSamples;
        settings.LoopMinimumSamples = 9; // Isolate the three-turn clock from the separately covered position loop.
        try
        {
            var observation = ClockObservation(); var source = Seed(settings, observation);
            var initial = source.GetObjective(7);
            Check("clock fixture has an assigned goal", initial != null && initial.State == ObjectiveState.Active);
            var state = AsOldSchema(source.CaptureState(), observation.Actors[0].Cell);
            var restored = new AIExplorationPlanner(settings); restored.RestoreState(state);
            var pending = JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(restored.CaptureState()));
            Check("saving before the first observation preserves pending old-distance migration", pending.Version == 3
                && pending.Objectives.Count == 1 && pending.Objectives[0].RebaseProgressOnNextObservation
                && pending.Objectives[0].LastProgressTurn == 1);
            restored = new AIExplorationPlanner(settings); restored.RestoreState(pending);
            var actor = observation.Actors[0]; actor.UnitId = 77; observation.Actors[0] = actor;
            observation.Turn = 2; restored.Update(observation, false);
            var migrated = restored.GetObjective(77);
            float expectedDistance = Mathf.Abs(actor.Cell.x - initial.TargetCell.x) + Mathf.Abs(actor.Cell.z - initial.TargetCell.z);
            Check("old-schema migration rebinds identity without rewarding a stationary explorer", migrated != null
                && migrated.ObjectiveId == initial.ObjectiveId && migrated.AssignedUnitId == 77
                && !migrated.RebaseProgressOnNextObservation && migrated.LastProgressTurn == 1
                && Mathf.Abs(migrated.LastDistance - expectedDistance) < .001f);
            observation.Turn = 3; restored.Update(observation, false);
            observation.Turn = 4; restored.Update(observation, false);
            Check("old-schema baseline changes do not postpone the three-turn stall", migrated.LastProgressTurn == 1
                && HasReason(restored, "objective_stalled"));

            observation = ClockObservation(); source = Seed(settings, observation);
            observation.Turn = 2; source.Update(observation, true);
            state = AsOldSchema(source.CaptureState(), observation.Actors[0].Cell);
            restored = new AIExplorationPlanner(settings); restored.RestoreState(state);
            observation.Turn = 3; restored.Update(observation, true); migrated = restored.GetObjective(7);
            Check("old-schema migration retains the original suspension and progress clocks", migrated != null
                && migrated.State == ObjectiveState.Suspended && migrated.SuspendedTurn == 2 && migrated.LastProgressTurn == 1);
            observation.Turn = 4; restored.Update(observation, false);
            Check("resuming an old suspended objective compensates the paused duration once", restored.GetObjective(7) == migrated
                && migrated.State == ObjectiveState.Active && migrated.LastProgressTurn == 3);
        }
        finally { settings.LoopMinimumSamples = oldLoopMinimum; }
    }

    static void UnprovedRouteKeepsMinorAxisProgress(ExplorationAISettings settings)
    {
        var observation = new ExplorationObservation { Width = 128, Depth = 128, Turn = 1,
            OwnBase = Cell(60, 32), CellVersion = 1, ViewVersion = 1 };
        observation.KnownCells.Add(new ExplorationObservedCell(Cell(60, 32), true, 0));
        observation.KnownCells.Add(new ExplorationObservedCell(Cell(89, 63), true, 0));
        observation.Actors.Add(new ExplorationActor(7, observation.OwnBase, actorLifeId: "boundary-minor-axis"));
        var planner = Seed(settings, observation); var initial = planner.GetObjective(7);
        Check("a sparse known frontier retains an unproved-route objective", initial != null
            && initial.TargetCell == Cell(89, 63) && !initial.UsesKnownRouteDistance);
        int objectiveId = initial.ObjectiveId; float previousDistance = initial.LastDistance;
        for (int turn = 2; turn <= 5; turn++)
        {
            var actor = observation.Actors[0]; var destination = actor.Cell + Cell(1, 0);
            Check("a legal east step advances the shorter axis while Chebyshev distance stays flat T" + turn,
                MovePatterns.CanMove(Kind.Scout, Direction.N, 1, 0)
                && GridHelper.ChebyshevDistance(actor.Cell, initial.TargetCell) == GridHelper.ChebyshevDistance(destination, initial.TargetCell)
                && planner.GetGoalProgress(7, destination) > 0 && planner.GetMoveBonus(7, destination) > 0);
            actor.Cell = destination; observation.Actors[0] = actor; observation.Turn = turn;
            planner.Update(observation, false); var current = planner.GetObjective(7);
            Check("unknown-route fallback recognizes actual progress without a new reveal T" + turn,
                current != null && current.ObjectiveId == objectiveId && current.State == ObjectiveState.Active
                && !current.UsesKnownRouteDistance && current.LastDistance < previousDistance
                && current.LastProgressTurn == turn && current.NewlyRevealedSinceStart == 0);
            previousDistance = current.LastDistance;
        }
        Check("minor-axis progress alone keeps the goal beyond the stall window", planner.Memory.Count == 2
            && !HasReason(planner, "objective_stalled") && !HasReason(planner, "unreachable"));
    }

    static void DiagonalRequiresKnownOpenCorners(ExplorationAISettings settings)
    {
        var terrain = new Dictionary<Vector3Int, Vector3Int>();
        for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++) terrain[Cell(x, z)] = Cell(x, z);
        var blocked = new HashSet<Vector3Int> { Cell(1, 0) };
        var offsets = new[] { new Vector2Int(1, 1) };
        var actor = new ExplorationActor(9, Cell(0, 0)); var route = new ExplorationKnownRoute(settings);
        Check("a diagonal cannot cut the corner of a known blocked cell", !route.TryMeasure(actor, Cell(1, 1), terrain, blocked,
            offsets, false, 1, 1, 1, 1, null, out _, out _, 2, 2));
        blocked.Clear();
        Check("opening both known corner cells makes the same diagonal provable", route.TryMeasure(actor, Cell(1, 1), terrain, blocked,
            offsets, false, 1, 2, 1, 1, null, out float remaining, out var waypoint, 2, 2)
            && remaining == 2 && waypoint == Cell(1, 1));
        terrain.Remove(Cell(0, 1));
        Check("an unknown touched corner never becomes an invented safe passage", !route.TryMeasure(actor, Cell(1, 1), terrain, blocked,
            offsets, false, 1, 3, 1, 1, null, out _, out _, 2, 2));
    }

    static void AsymmetricOffsetsRespectDirection(ExplorationAISettings settings)
    {
        var terrain = new Dictionary<Vector3Int, Vector3Int>();
        for (int z = 0; z < 3; z++) terrain[Cell(0, z)] = Cell(0, z);
        var blocked = new HashSet<Vector3Int>(); var offsets = new[] { Vector2Int.up };
        var actor = new ExplorationActor(10, Cell(0, 1)); var route = new ExplorationKnownRoute(settings);
        Check("an asymmetric north-facing mask can follow its forward edge", route.TryMeasure(actor, Cell(0, 2), terrain, blocked,
            offsets, true, 1, 1, 1, 1, null, out float remaining, out var waypoint, 1, 3) && remaining == 1 && waypoint == Cell(0, 2));
        Check("the same asymmetric mask cannot invent a backward edge", !route.TryMeasure(actor, Cell(0, 0), terrain, blocked,
            offsets, true, 1, 1, 1, 1, null, out _, out _, 1, 3));
        Check("south-facing direction reverses the asymmetric mask", route.TryMeasure(actor, Cell(0, 0), terrain, blocked,
            offsets, true, -1, 1, 1, 1, null, out remaining, out waypoint, 1, 3) && remaining == 1 && waypoint == Cell(0, 0));
        Check("an independent authored mask ignores the facing sign", route.TryMeasure(actor, Cell(0, 2), terrain, blocked,
            offsets, false, -1, 1, 1, 1, null, out remaining, out waypoint, 1, 3) && remaining == 1 && waypoint == Cell(0, 2));
    }
}
#endif
