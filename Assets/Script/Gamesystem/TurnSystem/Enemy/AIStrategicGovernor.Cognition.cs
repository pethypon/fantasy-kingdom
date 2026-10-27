using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    /// <summary>Called for an already generated formal candidate; never grants additional game actions.</summary>
    public AICognitiveActionAssessment AssessCognitiveAction(AIAction action, AIBoardState board)
    {
        var result = new AICognitiveActionAssessment();
        if (action == null || board == null || action.APCost < 0 || action.APCost > board.EnemyAP
            || !string.IsNullOrEmpty(action.StrategicRejectReason)) return result;
        Evaluate(board);
        // The shadow observer must not write StrategicRejectReason or mutate candidate scores.
        result.Legal = RejectReason(action, board) == null;
        if (!result.Legal) return result;
        result.ProvenVictory = IsDecisiveVictoryAttack(action, board);
        float risk = action.Unit != null ? Risk(action, board) : 0;
        result.PredictedIncomingDamage = Mathf.Max(0, risk);
        result.HardSafe = result.ProvenVictory || action.Unit == null || risk < action.Unit.HP;
        result.Confidence = .6f; // A conservative model confidence, not a calibrated victory probability.
        if (action.ActionType == AIActionType.Attack && action.Unit != null && action.TargetUnit != null
            && board.IsSpecialActorObserved(action.TargetUnit))
        {
            result.EffectiveDamage = AIActionEvaluator.EstimateEffectiveSpecialImpact(action.Unit, action.TargetUnit, board, out var recipient);
            if (recipient != null && board.IsSpecialActorObserved(recipient))
                result.ExpectedGoalProgress = Mathf.Clamp01(result.EffectiveDamage / Mathf.Max(1f, recipient.HP + (float)recipient.Barrier));
        }
        if (action.Unit != null && IsMovement(action))
        {
            var primary = board.Operations?.PrimaryOperation;
            if (primary != null && primary.HasTargetPosition)
            {
                var objective = new Vector3(primary.TargetX, 0, primary.TargetZ);
                float gain = Distance(action.Unit.transform.position, objective) - Distance(action.TargetPos, objective);
                result.ExpectedGoalProgress = Mathf.Clamp01(gain / 8f);
            }
        }
        // These official orientation predicates demonstrate a current attack/visibility preparation.
        // They do not prove arbitrary moving-blocker/open-line simulations.
        result.PreparationProven = action.Unit != null && result.HardSafe
            && (AIOrientation.IsObservedAttackPreparation(action, board) || AIOrientation.IsVisibilityPreparation(action, board));
        return result;
    }
}
