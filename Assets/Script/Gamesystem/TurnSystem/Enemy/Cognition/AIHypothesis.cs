using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIHypothesisCause { UnknownRoute, FormationDispersed, ObservedDefense, FiringLine, InsufficientAP, ForecastDeficit, PriorityConflict, ObservationChanged, RepeatedUnproductiveMethod }
public enum AIHypothesisOutcome { Untested, Supported, Refuted, Inconclusive }

/// <summary>A falsifiable cause proposal, never a current enemy fact.</summary>
public sealed class AIHypothesis
{
    public string HypothesisId, ProblemId;
    public AIHypothesisCause CauseCode;
    public IReadOnlyList<string> SupportingEvidenceIds = Array.Empty<string>();
    public IReadOnlyList<string> Contradictions = Array.Empty<string>();
    public float Confidence;
    public string TestActionType, ExpectedObservation;
    public float ExpectedInformationGain;
    public int TestedTurn = -1;
    public AIHypothesisOutcome Outcome;

    public AIHypothesis Copy()
    {
        var support = new string[SupportingEvidenceIds.Count]; var opposing = new string[Contradictions.Count];
        for (int i = 0; i < support.Length; i++) support[i] = SupportingEvidenceIds[i];
        for (int i = 0; i < opposing.Length; i++) opposing[i] = Contradictions[i];
        return new AIHypothesis { HypothesisId = HypothesisId, ProblemId = ProblemId, CauseCode = CauseCode,
            SupportingEvidenceIds = Array.AsReadOnly(support), Contradictions = Array.AsReadOnly(opposing),
            Confidence = AIWorldFact.Finite(Confidence) ? Mathf.Clamp01(Confidence) : 0,
            TestActionType = TestActionType, ExpectedObservation = ExpectedObservation,
            ExpectedInformationGain = ExpectedInformationGain, TestedTurn = TestedTurn, Outcome = Outcome };
    }
}
