using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    bool IsSafeVictoryAttack(AIAction action, AIBoardState board)
    {
        if (Mode == StrategicMode.EmergencyDefense || Mode == StrategicMode.EconomicRecovery
            || action?.ActionType != AIActionType.Attack || action.Unit == null || action.TargetUnit == null) return false;
        var actor = action.Unit; var target = action.TargetUnit;
        if (!actor.IsAlive || actor.team != board.ActorTeam || !MatchObjectiveRules.IsDecisive(target)
            || target.team != board.OpponentTeam || !target.IsAlive || !board.IsSpecialActorObserved(target)
            || actor.type != Type.Unit || StatusEffectSystem.IsStunned(actor) || SpecialSkillRuntime.AttackBlocked(actor)
            || action.APCost < 0 || action.APCost > board.EnemyAP || board.CalcAttackCost(actor) > board.EnemyAP
            || !board.GetAttackTargets(actor).Contains(target)) return false;
        int impact = AIActionEvaluator.EstimateEffectiveSpecialImpact(actor, target, board, out var victim);
        return impact > 0 && victim != null && board.IsSpecialActorObserved(victim) && Risk(action, board) < actor.HP;
    }
}
