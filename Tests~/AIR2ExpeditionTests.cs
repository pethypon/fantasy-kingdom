#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>R3 P0-A: deterministic observed-input replay. This is not a normal-match result.</summary>
public static class AIR2ExpeditionTests
{
    static int checks, failed;
    static void Check(string label, bool value)
    { if (!value) throw new InvalidOperationException("[AIR3Expedition] FAIL " + label); checks++; Debug.Log("[AIR3Expedition] PASS " + label); }
    static void Case(string label, Action action)
    {
        try { action(); }
        catch (Exception error) { failed++; Debug.Log("[AIR3ExpeditionCase] FAIL " + label + " " + (error.InnerException ?? error).Message); }
    }
    public static void Run(GameSystems live)
    {
        checks = failed = 0;
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            Case(side + " ContinueRegion", () => ContinueRegion(side));
            Case(side + " ScoutingRepeat30Turn", () => Repeat30(side));
            Case(side + " ScoutingLegitimateRevisit", () => LegitimateRevisit(side));
            Case(side + " ScoutingRetreatForSafety", () => Safety(side));
            Case(side + " legacy-save-and-bounds", () => PersistenceContracts(side));
        }
        if (failed > 0) throw new InvalidOperationException("[AIR3Expedition] failed cases=" + failed + " passed checks=" + checks);
        Debug.Log("[AIR3Expedition] ALL PASSED " + checks);
    }
    sealed class Fixture : IDisposable
    {
        public readonly ExplorationAISettings Settings = ScriptableObject.CreateInstance<ExplorationAISettings>();
        public readonly AIExplorationPlanner Planner;
        public readonly ExplorationObservation Observation;
        public ExplorationActor Actor;
        public Fixture(Team side)
        {
            Settings.RegionSize = 16; Settings.CompletionDistance = 2; Settings.StallTurns = 3;
            Planner = new AIExplorationPlanner(Settings);
            Observation = new ExplorationObservation { Width = 32, Depth = 16, Turn = 1, ActorTeam = side,
                ViewVersion = 1, CellVersion = 1, OwnBase = new Vector3Int(0, 0, 2), CanReach = (_, p) => p.x < 16 };
            for (int x = 0; x < 16; x++)
            {
                var point = new Vector3Int(x, 1, 2);
                Observation.KnownCells.Add(new ExplorationObservedCell(point)); Observation.VisibleCells.Add(GridHelper.ToGridXZ(point));
            }
            Actor = new ExplorationActor(191, new Vector3Int(0, 1, 2), actorLifeId: "r3-expedition-" + side);
            Observation.Actors.Add(Actor); Planner.Update(Observation, false);
        }
        public ExploreObjective Goal => Planner.GetObjective(Actor.UnitId);
        public void MoveTo(Vector3Int position, bool reveal = false, bool emergency = false)
        {
            Actor.Cell = position; Observation.Actors[0] = Actor; Observation.Turn++; Observation.ViewVersion++;
            if (reveal)
            {
                var cell = position + Vector3Int.forward;
                Observation.KnownCells.Add(new ExplorationObservedCell(cell)); Observation.VisibleCells.Add(GridHelper.ToGridXZ(cell)); Observation.CellVersion++;
            }
            Planner.Update(Observation, emergency);
        }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(Settings);
    }
    static void ContinueRegion(Team side)
    {
        using var f = new Fixture(side);
        Check(side + " replay has a safe genuine frontier", f.Goal != null && f.Goal.State == ObjectiveState.Active);
        int id = f.Goal.ObjectiveId; var first = f.Goal.TargetCell;
        // Replay the observed legacy state: cumulative gains from earlier legs are already 198.
        f.Goal.NewlyRevealedSinceStart = 198;
        f.MoveTo(first, reveal: true);
        var next = f.Goal.TargetCell;
        Check(side + " fresh observation continues an unfinished region without ordering home", f.Goal.ObjectiveId == id
            && f.Goal.LastReason == "region_exploration_continues" && next != first && f.Goal.NewlyRevealedSinceStart == 199);
        int lastProgress = f.Goal.LastProgressTurn;
        f.MoveTo(next);
        Check(side + " old cumulative199 cannot count as new information on the next leg", f.Goal.ObjectiveId != id
            || f.Goal.TargetCell == next || f.Goal.LastReason != "region_exploration_continues");
        Check(side + " switching an information-free waypoint does not renew the original stall clock", f.Goal.ObjectiveId != id
            || f.Goal.LastProgressTurn <= lastProgress || f.Goal.TargetCell == next);
    }
    static void Repeat30(Team side)
    {
        using var f = new Fixture(side);
        f.Goal.NewlyRevealedSinceStart = 198; f.MoveTo(f.Goal.TargetCell, reveal: true);
        var visits = new Dictionary<Vector3Int, int>(); int repeats = 0;
        for (int turn = 0; turn < 30; turn++)
        {
            var goal = f.Goal;
            if (goal == null || goal.State != ObjectiveState.Active) { f.MoveTo(f.Actor.Cell); continue; }
            var target = goal.TargetCell;
            if (target != f.Actor.Cell)
            {
                visits.TryGetValue(target, out int count); visits[target] = count + 1;
                if (count >= 2) repeats++;
            }
            f.MoveTo(target);
        }
        Debug.Log("[AIR3ExpeditionReplay] side=" + side + " turns=30 repeatArrivals=" + repeats + " cumulative=" + f.Goal?.NewlyRevealedSinceStart);
        Check(side + " ScoutingRepeat30Turn has no third information-free arrival at an unchanged viewpoint", repeats == 0);
        var state = f.Planner.CaptureState();
        var field = typeof(AIExplorationState).GetField("Expeditions");
        var entries = field?.GetValue(state) as System.Collections.ICollection;
        Check(side + " actor-region result evidence survives short-history clearing and capture", entries != null && entries.Count > 0 && entries.Count <= 256);
        var restored = new AIExplorationPlanner(f.Settings); restored.RestoreState(JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(state)));
        restored.Update(f.Observation, false);
        var savedAgain = field.GetValue(restored.CaptureState()) as System.Collections.ICollection;
        Check(side + " bounded actor-region evidence survives JSON save-load", savedAgain != null && savedAgain.Count == entries.Count);
    }
    static void LegitimateRevisit(Team side)
    {
        using var f = new Fixture(side);
        f.Goal.NewlyRevealedSinceStart = 198; f.MoveTo(f.Goal.TargetCell, reveal: true);
        var entry = f.Goal.TargetCell; f.MoveTo(entry);
        var method = typeof(AIExplorationPlanner).GetMethod("IsUnproductiveViewpoint", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(side + " unproductive arrival is recorded as an observation-basis fact", method != null);
        bool Blocked() => (bool)method.Invoke(f.Planner, new object[] { f.Actor.ActorLifeId, f.Goal.FrontierRegionId, entry, false });
        Check(side + " unchanged low-information viewpoint is suppressed", Blocked());
        f.Observation.ImportantCells[GridHelper.ToGridXZ(entry)] = 2;
        f.Observation.Turn++; f.Observation.ViewVersion++;
        f.Planner.Update(f.Observation, false);
        Check(side + " materially changed observed intelligence permits a legitimate revisit", !Blocked());
    }
    static void Safety(Team side)
    {
        using var f = new Fixture(side); int id = f.Goal.ObjectiveId;
        var target = f.Goal.TargetCell;
        f.MoveTo(f.Actor.Cell, emergency: true);
        Check(side + " danger suspends an expedition instead of forcing scouting", f.Goal.State == ObjectiveState.Suspended);
        f.MoveTo(f.Actor.Cell);
        Check(side + " resolving danger can resume the retained target", f.Goal.ObjectiveId == id && f.Goal.State == ObjectiveState.Active && f.Goal.TargetCell == target);
        Check(side + " lethal exposure is not overridden by information progress", f.Planner.GetMoveBonus(f.Actor.UnitId, target, estimatedDamage: f.Actor.HP * 2) < 0);
    }
    static void PersistenceContracts(Team side)
    {
        using var f = new Fixture(side);
        var legacy = f.Planner.CaptureState();
        legacy.Expeditions = null;
        foreach (var objective in legacy.Objectives)
        {
            objective.HasLegBaseline = false; objective.LegRevealBaseline = 0; objective.LegInformationRevision = 0;
            objective.NewlyRevealedSinceStart = 199;
        }
        for (int i = 0; i < legacy.Regions.Count; i++)
        {
            var region = legacy.Regions[i]; region.InformationRevision = region.ObservedContactSignature = 0; legacy.Regions[i] = region;
        }
        var restored = new AIExplorationPlanner(f.Settings); restored.RestoreState(JsonUtility.FromJson<AIExplorationState>(JsonUtility.ToJson(legacy)));
        var captured = restored.CaptureState();
        Check(side + " optional-v3 legacy payload rebases old cumulative199 without creating fresh leg credit",
            captured.Objectives.Count == 1 && captured.Objectives[0].HasLegBaseline
            && captured.Objectives[0].LegRevealBaseline == 199 && captured.Expeditions.Count == 0);
        var overlong = f.Planner.CaptureState();
        for (int i = 0; i < 300; i++) overlong.Expeditions.Add(new ExplorationExpeditionRecord
        {
            ActorLifeId = "bounded-scout-" + i, Viewpoint = new Vector3Int(i % 16, 0, 2), LastArrivalTurn = 1,
            Visits = 1, KnownCells = 16, InformationRevision = 16, Unproductive = true
        });
        restored.RestoreState(overlong);
        Check(side + " corrupt oversized expedition payload cannot exceed its256-record owner bound", restored.CaptureState().Expeditions.Count <= 256);
        overlong.Expeditions.Clear();
        overlong.Expeditions.Add(new ExplorationExpeditionRecord { ActorLifeId = null, Viewpoint = new Vector3Int(1, 0, 2), LastArrivalTurn = 1 });
        overlong.Expeditions.Add(new ExplorationExpeditionRecord { ActorLifeId = "future", Viewpoint = new Vector3Int(1, 0, 2), LastArrivalTurn = 999 });
        overlong.Expeditions.Add(new ExplorationExpeditionRecord { ActorLifeId = "outside", Viewpoint = new Vector3Int(999, 0, 2), LastArrivalTurn = 1 });
        restored.RestoreState(overlong);
        Check(side + " malformed identities, future evidence and out-of-bounds viewpoint facts are rejected", restored.CaptureState().Expeditions.Count == 0);
    }
}
#endif
