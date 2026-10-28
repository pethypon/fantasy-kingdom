using System;
using System.Collections.Generic;
using UnityEngine;

public enum AICognitivePlanStatus { Proposed, Validated, Active, Suspended, Failed, Completed }
public enum AICognitiveResponseLegality { UnknownThreat, PossiblyLegal, LegalKnown, Unsupported }

[Serializable]
public sealed class AICognitiveEnemyResponse
{
    public string ResponseClass, EvidenceId;
    public AICognitiveResponseLegality Legality;
    public float LossLowerBound, LossUpperBound;
    public bool IsPrediction = true;
}

/// <summary>Battle-local values only. No Status, Transform, AIAction or hidden live state is retained.</summary>
[Serializable]
public sealed class AICognitivePlan
{
    public string PlanId, Signature, ObjectiveRef, TargetObservedId, PrimaryMethod, BattleId;
    public Team OwnerTeam;
    public long ParentOperationId;
    public AIOperationGoal Goal;
    public int CreatedTurn, LastValidatedTurn, BoardGenerationAtValidation, StrategicPriorityBand;
    public AICognitivePlanStatus Status;
    public List<AIPlanOperator> Steps = new List<AIPlanOperator>();
    public List<string> KnowledgeIds = new List<string>();
    public List<int> KnowledgeVersions = new List<int>();
    public List<string> OriginProblemIds = new List<string>();
    public List<string> HypothesisIds = new List<string>();
    public List<string> ExplanationEvidenceIds = new List<string>();
    public List<AICognitiveEnemyResponse> EnemyResponses = new List<AICognitiveEnemyResponse>();
    public float GoalProgress, EffectiveDamage, EffectiveSupport, PredictedIncomingDamage, WorstCaseLoss;
    public float APCost, Delay, Confidence, Uncertainty, Utility, ExistingActionScore;
    public bool ProvenVictory, PreparationProven, HasUnknownContinuationCost;
    public Vector3 StartActorPosition, ExpectedActorPosition;
    public int StartTargetHP = -1, StartTargetBarrier = -1, StartOwnAP, StartOwnUnitCount;
    public string ActualObservationReason;
    // A completed existing-search hint about the bound first action, not proof of every future Operator.
    public int LookaheadCompletedDepth;
    public float LookaheadDelta, LookaheadUtilityAdjustment;
    public string LookaheadSourceKind;
    public string SquadSequenceSignature;
    public string AbandonCondition, ReplanTrigger, RejectionReason;
    public string NextActionKey => Steps.Count == 0 ? string.Empty : Steps[0].BoundActionKey ?? string.Empty;

    public bool HasValidDependencies()
    {
        if (Steps == null || Steps.Count == 0 || Steps.Count > 6) return false;
        var ids = new HashSet<int>();
        foreach (var step in Steps)
        {
            if (step == null || step.Dependencies == null || step.StepId < 0 || !ids.Add(step.StepId)) return false;
            foreach (int dependency in step.Dependencies)
                if (dependency >= step.StepId || !ids.Contains(dependency)) return false;
        }
        return true;
    }

    public static string StableId(string signature)
    {
        // FNV-1a is an identifier, not an equality test: candidates are deduplicated by full Signature.
        unchecked { ulong hash = 14695981039346656037UL; foreach (char c in signature ?? string.Empty) { hash ^= c; hash *= 1099511628211UL; }
            return "r4-" + hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
