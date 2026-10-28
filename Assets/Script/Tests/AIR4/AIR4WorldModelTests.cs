#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>R4 value/observation fixtures. Added as source only; no test result is implied by compiling this file.</summary>
public static class AIR4WorldModelTests
{
    static void Check(string id, bool value)
    { if (!value) throw new InvalidOperationException("[R4World] FAIL " + id); Debug.Log("[R4World] PASS " + id); }

    public static void Run()
    {
        Check("W-truth-and", AIWorldFact.And(AITruthValue.True, AITruthValue.Unknown) == AITruthValue.Unknown
            && AIWorldFact.And(AITruthValue.False, AITruthValue.Unknown) == AITruthValue.False);
        Check("W-truth-or", AIWorldFact.Or(AITruthValue.False, AITruthValue.Unknown) == AITruthValue.Unknown
            && AIWorldFact.Or(AITruthValue.True, AITruthValue.Unknown) == AITruthValue.True);
        var invalid = new AIWorldFact("invalid", AITruthValue.True, AIWorldFactSource.CurrentObservation, 1, float.NaN);
        Check("W-nonfinite-confidence", invalid.Truth == AITruthValue.Unknown && invalid.Confidence == 0 && invalid.Source == AIWorldFactSource.Unknown);
        var units = new[] { Unit("own", Team.Enemy) };
        var factArray = new[] { new AIWorldFact("past-contact", AITruthValue.Unknown, AIWorldFactSource.PastObservation, 2, .5f),
            new AIWorldFact("future", AITruthValue.True, AIWorldFactSource.CurrentObservation, 100, 1) };
        var model = Model(5, units: units, facts: factArray);
        units[0] = Unit("changed", Team.Player); factArray[0] = default;
        Check("W-caller-array-isolation", model.OwnUnits[0].LifeId == "own" && model.Fact("past-contact") == AITruthValue.Unknown);
        Check("W-no-future-observation", !model.TryFact("future", out _));
        Check("W-missing-is-unknown", model.Fact("unsupported-route") == AITruthValue.Unknown);
        Check("W-own-and-global-clock-distinct", Model(3, globalTurn: 8,
            facts: new[] { new AIWorldFact("seen", AITruthValue.True, AIWorldFactSource.CurrentObservation, 8) }).Fact("seen") == AITruthValue.True);
        foreach (var type in new[] { typeof(AIObservedUnitValue), typeof(AIWorldFact), typeof(AIWorldTerrainValue), typeof(AIWorldObjectiveValue), typeof(AIWorldBeliefCell) })
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Check("W-value-field-" + type.Name + "." + field.Name, !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType));

        var detector = new AIProblemDetector();
        var stalled = new AIWorldOperationValue(1, AIOperationGoal.DestroyEnemyCrystal, AIOperationStatus.Active,
            1, 1, "crystal", new Vector3(10, 0, 10), true, new[] { "own" });
        var problems = detector.Detect(Model(5, operations: new[] { stalled }));
        Check("H-stall-uses-recorded-progress", problems.Count == 1 && problems[0].Type == AIProblemType.NoProgress);
        var same = detector.Detect(Model(6, operations: new[] { stalled }));
        Check("H-repeat-deduplicates-problem", same.Count == 1 && same[0].FirstTurn == problems[0].FirstTurn);
        var progress = new AIWorldOperationValue(1, stalled.Goal, stalled.Status, 1, 5, "crystal", stalled.Position, true, new[] { "own" });
        Check("H-siege-progress-is-not-stall", detector.Detect(Model(6, operations: new[] { progress })).Count == 0);
        var suspended = new AIWorldOperationValue(1, stalled.Goal, AIOperationStatus.Suspended, 1, 1, "crystal", stalled.Position, true, new[] { "own" });
        Check("H-deliberate-suspension-is-not-stall", detector.Detect(Model(9, operations: new[] { suspended })).Count == 0);
        Check("H-emergency-hold-is-not-stall", detector.Detect(Model(9, operations: new[] { stalled }, facts: new[] {
            new AIWorldFact("emergency_defense", AITruthValue.True, AIWorldFactSource.CurrentObservation, 9) })).Count == 0);
        Check("H-unknown-route-is-not-confirmed-block", detector.Detect(Model(9, facts: new[] {
            new AIWorldFact("route_blocked", AITruthValue.Unknown, AIWorldFactSource.Belief, 9, .6f) })).Count == 0);
        Check("H-confirmed-block-is-problem", detector.Detect(Model(9, facts: new[] {
            new AIWorldFact("route_blocked", AITruthValue.True, AIWorldFactSource.CurrentObservation, 9) }))[0].Type == AIProblemType.BlockedRoute);
        var manager = new AIHypothesisManager(); var world = Model(5, operations: new[] { stalled });
        var hypotheses = manager.Update(world, detector.Detect(world));
        Check("H-retains-alternative-causes", hypotheses.Count >= 2 && hypotheses[0].Confidence < 1);
        string id = hypotheses[0].HypothesisId;
        Check("H-records-real-refutation", manager.ObserveOutcome("action-1", id, AIHypothesisOutcome.Refuted, 5, "actual-route-closed"));
        Check("H-rejects-duplicate-result", !manager.ObserveOutcome("action-1", id, AIHypothesisOutcome.Refuted, 5));
        var refreshed = manager.Update(world, detector.Detect(world)); bool retained = false;
        foreach (var hypothesis in refreshed) if (hypothesis.HypothesisId == id) retained = hypothesis.Outcome == AIHypothesisOutcome.Refuted;
        Check("H-refutation-survives-unchanged-context", retained);
        var saved = manager.Snapshot(world.BattleId, world.ActorTeam); var loaded = new AIHypothesisManager();
        Check("H-save-value-roundtrip", loaded.Restore(saved, world.BattleId, world.ActorTeam, world.OwnTurn)
            && !loaded.ObserveOutcome("action-1", id, AIHypothesisOutcome.Refuted, 5));
        Check("H-save-does-not-share-entry-list", loaded.Snapshot(world.BattleId, world.ActorTeam).Entries.Count == saved.Entries.Count);
        saved.Version++;
        Check("H-future-save-rejected", !loaded.Restore(saved, world.BattleId, world.ActorTeam, world.OwnTurn));
        saved.Version--; saved.ActorTeam = Team.Player;
        Check("H-foreign-owner-rejected", !loaded.Restore(saved, world.BattleId, world.ActorTeam, world.OwnTurn));
        saved.ActorTeam = world.ActorTeam; saved.LastOwnTurn = 100;
        Check("H-future-own-turn-rejected", !loaded.Restore(saved, world.BattleId, world.ActorTeam, world.OwnTurn));
        saved.LastOwnTurn = 5; saved.Entries[0].Confidence = float.NaN;
        Check("H-malformed-confidence-rejected", !loaded.Restore(saved, world.BattleId, world.ActorTeam, world.OwnTurn));
        Check("H-low-information-value", AIInformationValueEvaluator.Evaluate(1, .1f, 3, 2, 1, 1, true) < 0);
        Check("H-irrelevant-scout-value", AIInformationValueEvaluator.Evaluate(9999, 1, 0, 0, 0, 0, false) == 0);
        Check("H-nonfinite-information-value", AIInformationValueEvaluator.Evaluate(float.NaN, 1, 0, 0, 0, 0, true) == 0);
        var distinctClocks = Model(3, operations: new[] { stalled }, globalTurn: 8);
        var clockManager = new AIHypothesisManager();
        var clockHypotheses = clockManager.Update(distinctClocks, new AIProblemDetector().Detect(distinctClocks));
        Check("H-global-clock-event", clockManager.ObserveOutcome("event8", clockHypotheses[0].HypothesisId, AIHypothesisOutcome.Supported, 8));
        var clockState = clockManager.Snapshot(distinctClocks.BattleId, distinctClocks.ActorTeam);
        Check("SV-clock-values-roundtrip", clockState.LastOwnTurn == 3 && clockState.LastGlobalTurn == 8
            && new AIHypothesisManager().Restore(clockState, distinctClocks.BattleId, distinctClocks.ActorTeam, 3, 8));
        Check("SV-future-global-turn-rejected", !new AIHypothesisManager().Restore(clockState, distinctClocks.BattleId, distinctClocks.ActorTeam, 3, 7));
        Check("H-future-outcome-rejected", !clockManager.ObserveOutcome("event9", clockHypotheses[0].HypothesisId, AIHypothesisOutcome.Supported, 9));
    }

    static AIObservedUnitValue Unit(string life, Team team)
        => new AIObservedUnitValue(life, Kind.Knight, team, Vector3.zero, 20, 20, 3, 3, 5, 30,
            false, false, false, 1);
    static AIObservedWorldModel Model(int turn, AIObservedUnitValue[] units = null, AIWorldOperationValue[] operations = null,
        AIWorldFact[] facts = null, int globalTurn = -1)
        => new AIObservedWorldModel("fixture", Team.Enemy, turn, turn, 40, 40, 30, default,
            own: units, operations: operations, facts: facts, globalTurn: globalTurn);
}
#endif
