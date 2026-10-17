using UnityEngine;

public static partial class AIActionEvaluator
{
    public static float CalcEnvironmentAdjustment(AIAction action, AIBoardState board)
    {
        if (action?.Unit == null || board?.ReconMap == null || !board.ReconMap.Environment.HasEffects) return 0;
        bool moving = action.ActionType == AIActionType.Move || action.ActionType == AIActionType.Retreat
            || action.ActionType == AIActionType.Support || action.ActionType == AIActionType.Surround || action.ActionType == AIActionType.DefenseRepos;
        if (board.HasKnownEnvironment && (moving || action.ActionType == AIActionType.Attack || action.ActionType == AIActionType.Wait))
            action.PreserveSpecialEvaluation = true;
        if (!moving && action.ActionType != AIActionType.Wait) return 0;
        var actor = action.Unit;
        var from = actor.transform.position; var to = moving ? action.TargetPos : from;
        if (!board.IsTerrainKnown(to)) return 0; // No hazard/connector oracle beyond exploration.
        var environment = board.ReconMap.Environment;
        var destination = board.KnownEnvironmentDefinition(to);
        var origin = board.KnownEnvironmentDefinition(from);
        if (destination == null && origin == null) return 0;
        // The ordinary simulator has no overlay effects/teleportation. Preserve this truthful static model.
        action.PreserveSpecialEvaluation = true;
        int entry = moving ? EstimateObservedHitLoss(actor, board, board.KnownEnvironmentDamage(actor, to, true), HpChangeKind.IgnoreDefenceDamage) : 0;
        int standing = board.KnownEnvironmentDamage(actor, to, false);
        int remaining = Mathf.Max(0, actor.HP - entry);
        float value = AIBoardEvaluator.GetPieceValuePublic(actor);
        int loss = (int)System.Math.Min(actor.HP, (long)entry + Mathf.Min(remaining, standing));
        float score = -value * loss / Mathf.Max(1f, actor.HP);
        if (loss >= actor.HP) score -= value + (MatchObjectiveRules.IsDecisive(actor) ? 2000 : 35);
        int recovery = Mathf.Min(Mathf.Max(0, actor.MaxHP - remaining), board.KnownEnvironmentHeal(actor, to));
        score += value * recovery / Mathf.Max(1f, actor.MaxHP);
        if (moving && board.IsTerrainKnown(from))
        {
            int avoided = board.KnownEnvironmentDamage(actor, from, false);
            score += value * Mathf.Min(avoided, actor.HP) / Mathf.Max(1f, actor.HP);
            score -= board.KnownEnvironmentMoveCost(from, to);
        }
        int visionChange = board.KnownEnvironmentVisionModifier(to) - board.KnownEnvironmentVisionModifier(from);
        score += visionChange;
        if (destination?.kind == TerrainEnvironmentKind.HealingSpring && recovery > 0
            && board.EstimateCounterDamageAt(to, actor) < actor.HP)
            score += value * recovery / Mathf.Max(1f, actor.MaxHP) * AISpecialSkillKnowledge.FutureWeight(board, 2);
        if (destination?.kind == TerrainEnvironmentKind.Rock)
            foreach (var opponent in board.AlivePlayerUnits)
                if (board.IsSpecialActorObserved(opponent) && AIBoardQuery.EstimateAttackRange(opponent) > 1.5f)
                    score += value * .05f;
        if (destination?.kind == TerrainEnvironmentKind.Cave && moving)
        {
            // The three AP transaction arrives next own turn; it is not an instantaneous teleport.
            score *= AISpecialSkillKnowledge.FutureWeight(board, 1);
            score -= 1;
        }
        CombatRuleTelemetry.Record("ai_environment_value", actor, cause:"known_environment", candidateValue:score);
        return score;
    }
}
