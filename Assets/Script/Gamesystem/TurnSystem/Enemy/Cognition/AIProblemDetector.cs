using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIProblemType
{
    NoProgress, BlockedRoute, InsufficientForce, UnusableRangeOrLineOfSight, FormationBroken,
    ResourceBottleneck, ActionPriorityMismatch, InvalidAssumption, RepeatedFailure
}
public enum AIProblemStatus { Active, Resolved }

public sealed class AIProblem
{
    public string ProblemId { get; }
    public long OperationId { get; }
    public string ObjectiveId { get; }
    public AIProblemType Type { get; }
    public int FirstTurn { get; }
    public int LastConfirmedTurn { get; }
    public float Severity { get; }
    public float Confidence { get; }
    public AIProblemStatus Status { get; }
    public IReadOnlyList<string> EvidenceIds { get; }
    public IReadOnlyList<string> PreviousRemedies { get; }
    public string ObservedSymptoms { get; }
    public AIProblem(string id, AIProblemType type, long operationId, string objectiveId,
        int firstTurn, int lastTurn, float severity, float confidence, string[] evidence,
        string symptoms = null, string[] previousRemedies = null, AIProblemStatus status = AIProblemStatus.Active)
    {
        ProblemId = id ?? string.Empty; Type = type; OperationId = operationId; ObjectiveId = objectiveId ?? string.Empty;
        FirstTurn = firstTurn; LastConfirmedTurn = lastTurn;
        Severity = AIWorldFact.Finite(severity) ? Mathf.Clamp01(severity) : 0;
        Confidence = AIWorldFact.Finite(confidence) ? Mathf.Clamp01(confidence) : 0; Status = status;
        EvidenceIds = Array.AsReadOnly((string[])(evidence ?? Array.Empty<string>()).Clone());
        PreviousRemedies = Array.AsReadOnly((string[])(previousRemedies ?? Array.Empty<string>()).Clone());
        ObservedSymptoms = symptoms ?? string.Empty;
    }
}

/// <summary>Observed shortfalls, not emotional states. Lack of candidates is never inferred from geometric distance.</summary>
public sealed class AIProblemDetector
{
    readonly Dictionary<string, int> firstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
    readonly HashSet<string> active = new HashSet<string>(StringComparer.Ordinal);
    readonly List<string> removals = new List<string>();
    string battle;
    Team team;
    public void Clear() { firstSeen.Clear(); active.Clear(); removals.Clear(); battle = null; }

    public IReadOnlyList<AIProblem> Detect(AIObservedWorldModel world, int maxProblems = 16)
    {
        if (world == null) return Array.Empty<AIProblem>();
        if (battle != world.BattleId || team != world.ActorTeam) { Clear(); battle = world.BattleId; team = world.ActorTeam; }
        maxProblems = Mathf.Clamp(maxProblems, 1, 32);
        var result = new List<AIProblem>(); active.Clear();
        if (world.OwnEconomy.Known && world.OwnEconomy.HasCriticalDeficit)
            Add(result, world, AIProblemType.ResourceBottleneck, 0, "economy", .8f, 1,
                new[] { "resource_bottleneck" }, "Own official production forecast reports a critical deficit.", maxProblems);
        foreach (var operation in world.OngoingOperations)
        {
            if (result.Count >= maxProblems) break;
            // A suspended emergency/economic operation and useful siege progress are not stalled marches.
            bool deliberateHold = operation.Status == AIOperationStatus.Suspended
                || operation.Goal == AIOperationGoal.DefendOwnCrystal || operation.Goal == AIOperationGoal.DefendSubCrystal
                || operation.Goal == AIOperationGoal.HoldTerritory || operation.Goal == AIOperationGoal.DelayEnemy
                || world.Fact("emergency_defense") == AITruthValue.True;
            int reference = Mathf.Max(operation.StartTurn, operation.LastProgressTurn);
            if (!deliberateHold && (operation.Status == AIOperationStatus.Active || operation.Status == AIOperationStatus.Replanning)
                && world.OwnTurn - reference >= 2 && world.OwnAP > 0)
                Add(result, world, AIProblemType.NoProgress, operation.OperationId, operation.TargetLifeId,
                    Mathf.Min(.9f, .3f + (world.OwnTurn - reference) * .1f), .8f,
                    new[] { "operation:" + operation.OperationId }, "No new progress was recorded by the existing operation owner.", maxProblems);
        }
        // Precise tactical causes are accepted only if another formal assessment supplied that fact.
        AddIfProven(result, world, "route_blocked", AIProblemType.BlockedRoute, .7f, maxProblems);
        AddIfProven(result, world, "insufficient_force", AIProblemType.InsufficientForce, .7f, maxProblems);
        AddIfProven(result, world, "unusable_range_or_los", AIProblemType.UnusableRangeOrLineOfSight, .5f, maxProblems);
        AddIfProven(result, world, "formation_broken", AIProblemType.FormationBroken, .5f, maxProblems);
        AddIfProven(result, world, "priority_mismatch", AIProblemType.ActionPriorityMismatch, .5f, maxProblems);
        AddIfProven(result, world, "invalid_assumption", AIProblemType.InvalidAssumption, .7f, maxProblems);
        AddIfProven(result, world, "repeated_failure", AIProblemType.RepeatedFailure, .7f, maxProblems);
        removals.Clear(); foreach (var id in firstSeen.Keys) if (!active.Contains(id)) removals.Add(id);
        foreach (var id in removals) firstSeen.Remove(id);
        result.Sort((a, b) => { int severity = b.Severity.CompareTo(a.Severity); return severity != 0 ? severity : string.CompareOrdinal(a.ProblemId, b.ProblemId); });
        return result.AsReadOnly();
    }

    void AddIfProven(List<AIProblem> result, AIObservedWorldModel world, string factId, AIProblemType type, float severity, int limit)
    {
        if (world.TryFact(factId, out var fact) && fact.Truth == AITruthValue.True && fact.Source == AIWorldFactSource.CurrentObservation)
            Add(result, world, type, 0, string.Empty, severity, fact.Confidence, new[] { factId }, factId, limit);
    }

    void Add(List<AIProblem> result, AIObservedWorldModel world, AIProblemType type, long operation, string objective,
        float severity, float confidence, string[] evidence, string symptom, int limit)
    {
        if (result.Count >= limit) return;
        string id = type + ":" + operation;
        if (!active.Add(id)) return;
        if (!firstSeen.TryGetValue(id, out int first)) firstSeen[id] = first = world.OwnTurn;
        result.Add(new AIProblem(id, type, operation, objective, first, world.OwnTurn, severity, confidence, evidence, symptom));
    }
}
