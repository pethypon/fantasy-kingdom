using System;
using UnityEngine;

/// <summary>Bounded approximate comparison around the existing action evaluation. It never proves Sim parity.</summary>
public static class AICognitivePlanEvaluator
{
    public static bool Evaluate(AICognitivePlan plan, AIObservedWorldModel world, AICognitivePlannerLimits limits)
    {
        if (plan == null || world == null || !plan.HasValidDependencies() || plan.Steps.Count > limits.MaxOperatorsPerPlan ||
            plan.BoardGenerationAtValidation != world.BoardGeneration || plan.OwnerTeam != world.ActorTeam ||
            plan.BattleId != world.BattleId || plan.APCost < 0 || plan.APCost > world.OwnAP ||
            !Finite(plan)) return Reject(plan, "illegal_or_stale_plan");
        if (plan.StrategicPriorityBand < 2) return Reject(plan, "protected_safety_band");
        if (string.IsNullOrEmpty(plan.NextActionKey)) return Reject(plan, "unbound_first_step");

        plan.EnemyResponses.Clear();
        float knownAttack = 0;
        foreach (var enemy in world.ObservedOpponents)
            if (enemy.Source == AIWorldFactSource.CurrentObservation && enemy.ObservedTurn == world.GlobalTurn && enemy.ATK >= 0)
                knownAttack = Mathf.Max(knownAttack, enemy.ATK);
        // These are response classes/ranges, not invented legal enemy moves or calibrated probabilities.
        if (world.ObservedOpponents.Count > 0)
        {
            AddResponse(plan, limits, "Defend", AICognitiveResponseLegality.PossiblyLegal, 0, plan.PredictedIncomingDamage, "observed_opponents");
            AddResponse(plan, limits, "Retreat", AICognitiveResponseLegality.PossiblyLegal, 0, plan.PredictedIncomingDamage, "observed_opponents");
            AddResponse(plan, limits, "Counterattack", AICognitiveResponseLegality.PossiblyLegal,
                plan.PredictedIncomingDamage, Mathf.Max(plan.PredictedIncomingDamage, knownAttack), "observed_opponents");
        }
        else
            AddResponse(plan, limits, "UnknownThreat", AICognitiveResponseLegality.UnknownThreat, 0,
                plan.PredictedIncomingDamage, "unobserved_enemy_response");
        plan.WorstCaseLoss = plan.PredictedIncomingDamage;
        foreach (var response in plan.EnemyResponses) plan.WorstCaseLoss = Mathf.Max(plan.WorstCaseLoss, response.LossUpperBound);

        float actorHp = 1;
        foreach (var own in world.OwnUnits) if (own.LifeId == plan.Steps[0].ActorLifeId) { actorHp = Mathf.Max(1, own.HP); break; }
        float oldValue = plan.ExistingActionScore / (1 + Mathf.Abs(plan.ExistingActionScore));
        plan.Uncertainty = Mathf.Clamp01(1 - plan.Confidence);
        plan.Utility = plan.GoalProgress + .1f * oldValue - plan.APCost / Mathf.Max(1, world.OwnAP) * .1f -
            plan.WorstCaseLoss / actorHp * .25f - plan.Uncertainty * .2f - (plan.HasUnknownContinuationCost ? .05f : 0);
        if (!AIWorldFact.Finite(plan.Utility)) return Reject(plan, "non_finite_utility");
        plan.Status = AICognitivePlanStatus.Validated; return true;
    }

    static void AddResponse(AICognitivePlan plan, AICognitivePlannerLimits limits, string kind,
        AICognitiveResponseLegality legality, float low, float high, string evidence)
    {
        if (plan.EnemyResponses.Count >= limits.MaxEnemyResponses) return;
        plan.EnemyResponses.Add(new AICognitiveEnemyResponse { ResponseClass = kind, EvidenceId = evidence,
            Legality = legality, LossLowerBound = Mathf.Max(0, low), LossUpperBound = Mathf.Max(low, high) });
    }
    static bool Finite(AICognitivePlan p) => AIWorldFact.Finite(p.APCost) && AIWorldFact.Finite(p.GoalProgress) &&
        AIWorldFact.Finite(p.EffectiveDamage) && AIWorldFact.Finite(p.EffectiveSupport) && AIWorldFact.Finite(p.PredictedIncomingDamage) &&
        AIWorldFact.Finite(p.ExistingActionScore) && AIWorldFact.Finite(p.Confidence) && p.Confidence >= 0 && p.Confidence <= 1 &&
        p.EffectiveDamage >= 0 && p.EffectiveSupport >= 0 && p.PredictedIncomingDamage >= 0;
    static bool Reject(AICognitivePlan p, string reason) { if (p != null) { p.RejectionReason = reason; p.Status = AICognitivePlanStatus.Failed; } return false; }

    public static int Compare(AICognitivePlan left, AICognitivePlan right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return 1; if (right == null) return -1;
        int c = right.ProvenVictory.CompareTo(left.ProvenVictory); if (c != 0) return c;
        c = left.StrategicPriorityBand.CompareTo(right.StrategicPriorityBand); if (c != 0) return c;
        c = right.Utility.CompareTo(left.Utility); if (c != 0) return c;
        c = right.Confidence.CompareTo(left.Confidence); if (c != 0) return c;
        return string.CompareOrdinal(left.Signature, right.Signature);
    }
}
