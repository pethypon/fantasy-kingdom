using UnityEngine;

/// <summary>Information is useful only if it can change a concrete decision. Missing measurements cannot prove a positive value.</summary>
public static class AIInformationValueEvaluator
{
    public static float Evaluate(float decisionImprovement, float uncertaintyReduction, float apCost,
        float expectedLoss, float delayCost, float opportunityCost, bool decisionRelevant)
    {
        if (!decisionRelevant || !AIWorldFact.Finite(decisionImprovement) || !AIWorldFact.Finite(uncertaintyReduction)
            || !AIWorldFact.Finite(apCost) || !AIWorldFact.Finite(expectedLoss) || !AIWorldFact.Finite(delayCost)
            || !AIWorldFact.Finite(opportunityCost)) return 0;
        return Mathf.Max(0, decisionImprovement) * Mathf.Clamp01(uncertaintyReduction)
            - Mathf.Max(0, apCost) - Mathf.Max(0, expectedLoss) - Mathf.Max(0, delayCost) - Mathf.Max(0, opportunityCost);
    }
}
