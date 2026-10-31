#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

/// <summary>R3 persistence/identity fixtures. Source only / NOT RUN; no match acceptance is implied.</summary>
public static class AIR3ScoutContinuityTests
{
    static readonly Vector2Int[] Orthogonal = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
    static Vector3Int Cell(int x, int z) => new Vector3Int(x, 0, z);
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[R3ScoutContinuity] FAIL " + id); Debug.Log("[R3ScoutContinuity] PASS " + id); }
    static AIExplorationState RoundTrip(AIExplorationState state)
        => JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(state));

    public static void Run()
    {
        var settings = ScriptableObject.CreateInstance<ExplorationAISettings>();
        settings.EnableJsonlTelemetry = false;
        try
        {
            SparseContextCannotLeak(settings);
            StableLifeRebindAndLegMigration(settings);
            DuplicateObservationCannotDoubleCredit(settings);
            CompletedViewpointRetainsEvidence(settings);
            ObjectiveCounterCannotOverflow(settings);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }

    static AIExplorationState Fixture(bool terrain = true)
    {
        var state = new AIExplorationState { Width = 32, Depth = 16, Turn = 10, ActorTeam = Team.Enemy, NextObjectiveId = 18 };
        if (terrain)
            for (int x = 0; x < 16; x++) for (int z = 0; z < 16; z++)
                state.Cells.Add(new ExplorationMemoryRecord { Cell = Cell(x, z), Passable = true,
                    Memory = new ExplorationCellMemory { State = ExplorationKnowledgeState.SeenBefore, LastSeenTurn = 10, LastVisitedTurn = -1 } });
        state.Regions.Add(new ExplorationRegionState { RegionId = 0, LastAssignedTurn = 10, InformationRevision = 3 });
        state.Objectives.Add(new ExploreObjective { ObjectiveId = 17, ActorLifeId = "scout-life", AssignedUnitId = 7,
            TargetCell = Cell(15, 8), FrontierRegionId = 0, CreatedTurn = 8, LastProgressTurn = 10, LastSampleTurn = 10,
            State = ObjectiveState.Active, BestDistance = 7, LastDistance = 7, LastUnknownCount = 16,
            NewlyRevealedSinceStart = 20, HasLegBaseline = true, LegRevealBaseline = 18, LegInformationRevision = 3, LegStartedTurn = 9 });
        state.Histories.Add(new ExplorationHistoryRecord { ActorLifeId = "scout-life" });
        state.Histories[0].Samples.Add(new ExplorationHistorySample { Cell = Cell(8, 8), Turn = 10, NewlyRevealed = 0, Distance = 7 });
        state.Expeditions.Add(new ExplorationExpeditionRecord { ActorLifeId = "scout-life", RegionId = 0, Viewpoint = Cell(15, 8),
            LastArrivalTurn = 9, Visits = 2, TotalReveals = 5, KnownCells = 256, InformationRevision = 3, Unproductive = true });
        return state;
    }
    static ExplorationObservation Observation(AIExplorationState state, int runtime = 77)
    {
        var observation = new ExplorationObservation { Width = state.Width, Depth = state.Depth, Turn = state.Turn + 1,
            ActorTeam = state.ActorTeam, CellVersion = 1, ViewVersion = 1, OwnBase = Cell(1, 8) };
        foreach (var entry in state.Cells) observation.KnownCells.Add(new ExplorationObservedCell(entry.Cell, entry.Passable, 10));
        observation.Actors.Add(new ExplorationActor(runtime, Cell(8, 8), actorLifeId: "scout-life")
            { MovementOffsets = Orthogonal, MovementDirectionIndependent = true });
        return observation;
    }

    static void SparseContextCannotLeak(ExplorationAISettings settings)
    {
        for (int change = 0; change < 3; change++)
        {
            var state = Fixture(false); var planner = new AIExplorationPlanner(settings); planner.RestoreState(state);
            var observation = Observation(state);
            if (change == 0) observation.ActorTeam = Team.Player;
            else if (change == 1) observation.Width++;
            else observation.Turn = state.Turn - 1;
            planner.Update(observation, true);
            Check("empty-terrain-owner-map-clock-boundary-" + change,
                planner.Expeditions.Count == 0 && planner.Objectives.Count == 0 && planner.GetObjective(77) == null);
        }
    }
    static void StableLifeRebindAndLegMigration(ExplorationAISettings settings)
    {
        var state = Fixture(); state.Objectives[0].HasLegBaseline = false;
        var planner = new AIExplorationPlanner(settings); planner.RestoreState(RoundTrip(state));
        var pending = RoundTrip(planner.CaptureState());
        Check("loaded-runtime-id-is-not-reused", pending.Objectives[0].AssignedUnitId == 0);
        Check("legacy-leg-does-not-reuse-previous-reveals", pending.Objectives[0].HasLegBaseline
            && pending.Objectives[0].LegRevealBaseline == 20 && pending.Objectives[0].LegStartedTurn == 10);
        planner.RestoreState(pending); var observation = Observation(state); planner.Update(observation, true);
        var rebound = planner.GetObjective(77);
        Check("stable-life-rebind-keeps-mission-and-paused-clock", rebound != null && rebound.ObjectiveId == 17
            && rebound.AssignedUnitId == 77 && rebound.LegRevealBaseline == 20 && rebound.LegStartedTurn == 10
            && rebound.LastProgressTurn == 10 && planner.Expeditions.Count == 1);
        var actor = observation.Actors[0]; actor.ActorLifeId = "different-life"; observation.Actors[0] = actor;
        observation.Turn++; planner.Update(observation, true);
        Check("recycled-runtime-id-cannot-inherit-mission", planner.GetObjective(77) == null
            && planner.Expeditions.Count == 1 && planner.Expeditions[0].ActorLifeId == "scout-life");
    }
    static void DuplicateObservationCannotDoubleCredit(ExplorationAISettings settings)
    {
        var state = Fixture(); var planner = new AIExplorationPlanner(settings); planner.RestoreState(state);
        var observation = Observation(state);
        observation.KnownCells.Add(new ExplorationObservedCell(Cell(16, 8), true, observation.Turn));
        observation.Actors.Add(observation.Actors[0]);
        var duplicateLife = observation.Actors[0]; duplicateLife.UnitId = 88; observation.Actors.Add(duplicateLife);
        planner.Update(observation, false);
        var objective = planner.GetObjective(77);
        Check("duplicate-life-reveal-is-credited-once", objective != null && objective.ObjectiveId == 17
            && objective.NewlyRevealedSinceStart == 21 && planner.GetObjective(88) == null);
        var retained = planner.CaptureState();
        Check("objective-update-does-not-erase-expedition-evidence", retained.Expeditions.Count == 1
            && retained.Expeditions[0].Visits == 2 && retained.Expeditions[0].TotalReveals == 5);
    }
    static bool Unproductive(AIExplorationPlanner planner)
        => (bool)typeof(AIExplorationPlanner).GetMethod("IsUnproductiveViewpoint", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(planner, new object[] { "scout-life", 0, Cell(15, 8), false });
    static void CompletedViewpointRetainsEvidence(ExplorationAISettings settings)
    {
        var state = Fixture(); state.Objectives[0].State = ObjectiveState.Completed;
        var planner = new AIExplorationPlanner(settings); planner.RestoreState(RoundTrip(state));
        planner.RestoreState(RoundTrip(planner.CaptureState()));
        Check("completed-viewpoint-roundtrip-keeps-result", planner.Expeditions.Count == 1 && Unproductive(planner));
        var observation = Observation(state); observation.Turn = 25; planner.Update(observation, true);
        Check("elapsed-time-alone-does-not-invent-new-information", Unproductive(planner));
        observation.ObservedContacts.Add(new ExplorationObservedContact(Cell(14, 8), 123)); planner.Update(observation, true);
        Check("new-observed-contact-allows-legitimate-revisit", !Unproductive(planner));
        var latest = RoundTrip(state);
        latest.Expeditions.Add(new ExplorationExpeditionRecord { ActorLifeId = "scout-life", RegionId = 0, Viewpoint = Cell(15, 8),
            LastArrivalTurn = 10, Visits = 3, TotalReveals = 6, KnownCells = 256, InformationRevision = 3, Unproductive = false });
        planner.RestoreState(latest);
        Check("duplicate-save-record-retains-latest-observed-result", planner.Expeditions.Count == 1
            && !planner.Expeditions[0].Unproductive && planner.Expeditions[0].Visits == 3 && planner.Expeditions[0].TotalReveals == 6);
        latest.Expeditions.Reverse(); planner.RestoreState(latest);
        Check("older-duplicate-cannot-revive-stale-viewpoint-result", planner.Expeditions.Count == 1 && !planner.Expeditions[0].Unproductive);
    }
    static void ObjectiveCounterCannotOverflow(ExplorationAISettings settings)
    {
        var state = Fixture(); state.NextObjectiveId = int.MaxValue;
        state.Objectives[0].ObjectiveId = int.MaxValue - 1;
        state.Objectives.Add(new ExploreObjective { ObjectiveId = 1, ActorLifeId = "other-scout", TargetCell = Cell(14, 8),
            State = ObjectiveState.Suspended, CreatedTurn = 8, LastProgressTurn = 10, LastSampleTurn = 10 });
        var planner = new AIExplorationPlanner(settings); planner.RestoreState(state);
        var allocator = typeof(AIExplorationPlanner).GetMethod("AllocateObjectiveId", BindingFlags.Instance | BindingFlags.NonPublic);
        int assigned = (int)allocator.Invoke(planner, null);
        Check("counter-wrap-skips-retained-objective-identities", assigned == 2 && planner.CaptureState().NextObjectiveId == 3);
    }
}
#endif
