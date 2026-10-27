using System;
using System.Collections.Generic;

[Serializable]
public sealed class AICognitiveDecisionTrace
{
    public string SourceSHA, BattleId, SelectedPlanId, ChosenAIActionKey, ReplanReason, FallbackReason;
    public int AIConfigVersion = 1, GlobalTurn, OwnTurn, BoardGeneration, KnownCount, BeliefCount, UnknownCount;
    public Team ActorTeam;
    public long MatchedOperationId;
    public List<string> ObservedFactIds = new List<string>(), ProblemIds = new List<string>(), HypothesisIds = new List<string>(),
        KnowledgeIds = new List<string>(), CandidatePlanIds = new List<string>(), RejectedPlanReasons = new List<string>();
    public bool Shadow, ActionLegal, ExecutionAttempted, ExecutionSucceeded;
    public string LegalityStatus = "NO_EXECUTION";
    public float ObservedDamage, ObservedHealing, ObservedLoss, ObservedNewCells;
    public double ElapsedMilliseconds, BudgetRemainingMilliseconds;
    // Absence of measurements must never be serialized as a measured zero.
    public string FrameAndGCMeasurement = "NOT_MEASURED", SimulationParity = "NOT_VERIFIED";
}

/// <summary>Battle-local bounded diagnostics; no per-frame disk IO and no live actor references.</summary>
public sealed class AICognitiveTraceBuffer
{
    readonly Queue<AICognitiveDecisionTrace> entries = new Queue<AICognitiveDecisionTrace>();
    readonly int maximum;
    public int Count => entries.Count;
    public AICognitiveDecisionTrace Latest { get; private set; }
    public AICognitiveTraceBuffer(int maximum = 128) { this.maximum = Math.Max(1, Math.Min(128, maximum)); }
    public void Add(AICognitiveDecisionTrace trace)
    {
        if (trace == null) return;
        while (entries.Count >= maximum) entries.Dequeue();
        entries.Enqueue(trace); Latest = trace;
    }
    public AICognitiveDecisionTrace[] Snapshot() => entries.ToArray();
    public void Clear() { entries.Clear(); Latest = null; }
}
