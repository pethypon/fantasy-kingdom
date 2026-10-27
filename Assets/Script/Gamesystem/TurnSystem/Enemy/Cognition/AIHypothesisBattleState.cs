using System;
using System.Collections.Generic;

/// <summary>Optional R4 save values. No live unit, future action or execution reservation is persisted.</summary>
[Serializable]
public sealed class AIHypothesisBattleState
{
    public const int CurrentVersion = 1;
    public int Version = CurrentVersion;
    public string BattleId;
    public Team ActorTeam;
    public int LastOwnTurn = -1;
    public List<AIHypothesisSaveEntry> Entries = new List<AIHypothesisSaveEntry>();
    public List<string> ProcessedEventIds = new List<string>();
}

[Serializable]
public sealed class AIHypothesisSaveEntry
{
    public string HypothesisId, ProblemId, TestActionType, ExpectedObservation;
    public AIHypothesisCause CauseCode;
    public AIHypothesisOutcome Outcome;
    public float Confidence, ExpectedInformationGain;
    public int TestedTurn = -1;
    public List<string> SupportingEvidenceIds = new List<string>();
    public List<string> Contradictions = new List<string>();
}
