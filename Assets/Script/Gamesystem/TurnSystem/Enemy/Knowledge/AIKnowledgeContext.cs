using System;
using System.Collections.Generic;

public readonly struct AIKnowledgeEvidence
{
    public readonly AITruthValue State;
    public readonly string EvidenceId;
    public readonly float Confidence;
    public AIKnowledgeEvidence(AITruthValue state, string evidenceId, float confidence)
    { State = state; EvidenceId = evidenceId ?? string.Empty; Confidence = confidence; }
}

/// <summary>
/// One generation's proven predicate answers. This is a value-only boundary: it
/// cannot retrieve a live enemy, run an action, or promote a belief to a fact.
/// </summary>
public sealed class AIKnowledgeContext
{
    readonly AIKnowledgeEvidence[] facts = new AIKnowledgeEvidence[Enum.GetValues(typeof(AIStrategyPredicate)).Length];
    public string BattleId { get; }
    public Team ActorTeam { get; }
    public int BoardGeneration { get; }
    public int OwnTurn { get; }
    public int ThreatLevel { get; }
    public AIOperationGoal Goal { get; }
    public int Revision { get; private set; }
    public bool Enabled { get; set; } = true;
    public AIKnowledgeContext(string battleId, Team actorTeam, int ownTurn, int generation,
        AIOperationGoal goal = AIOperationGoal.None, int threatLevel = 100)
    {
        BattleId = battleId ?? string.Empty; ActorTeam = actorTeam; OwnTurn = ownTurn;
        BoardGeneration = generation; Goal = goal; ThreatLevel = Math.Max(0, Math.Min(100, threatLevel));
    }
    public void Set(AIStrategyPredicate predicate, AITruthValue state, string evidenceId, float confidence = 1f)
    {
        int i = (int)predicate;
        if (i < 0 || i >= facts.Length) throw new ArgumentOutOfRangeException(nameof(predicate));
        if (!Enum.IsDefined(typeof(AITruthValue), state)) throw new ArgumentOutOfRangeException(nameof(state));
        // Missing/invalid evidence remains unknown; never silently prove a precondition.
        if (state == AITruthValue.Unknown || string.IsNullOrWhiteSpace(evidenceId)
            || float.IsNaN(confidence) || float.IsInfinity(confidence) || confidence <= 0f || confidence > 1f)
            facts[i] = default;
        else facts[i] = new AIKnowledgeEvidence(state, evidenceId, confidence);
        unchecked { Revision++; }
    }
    public AIKnowledgeEvidence Get(AIStrategyPredicate predicate)
    {
        int i = (int)predicate;
        return i < 0 || i >= facts.Length ? default : facts[i];
    }
    public bool IsCurrent(string battleId, Team side, int generation)
        => string.Equals(BattleId, battleId, StringComparison.Ordinal) && ActorTeam == side && BoardGeneration == generation;
}
