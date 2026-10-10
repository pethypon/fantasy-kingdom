using System;
using System.Collections.Generic;

/// <summary>Generalized experience only. Runtime units, objectives and coordinates remain in the battle state.</summary>
[Serializable]
public sealed class AIOperationKnowledgeEntry
{
    public string ContextKey;
    public AIOperationGoal Goal;
    public string OperationPattern;
    public int Samples, Successes, Failures;
    public float AverageScore, LearnedValue, Confidence;
    public int LastUsedTurn;
    public long LastUseSequence;
    public List<AIOperationSuccessReason> SuccessReasons = new List<AIOperationSuccessReason>();
    public List<AIOperationFailureReason> FailureReasons = new List<AIOperationFailureReason>();
}
