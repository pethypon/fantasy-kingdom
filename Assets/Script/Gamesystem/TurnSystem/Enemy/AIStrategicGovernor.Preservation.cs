using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    bool IsDecisiveVictoryAttack(AIAction action, AIBoardState board)
    {
        var target = action.TargetUnit;
        return action.ActionType == AIActionType.Attack && action.Unit != null && target != null
            && target.team == board.OpponentTeam && board.IsSpecialActorObserved(target)
            && (MatchObjectiveRules.IsCommander(target) || target.kind == Kind.Crystal)
            && action.APCost <= board.EnemyAP && board.GetAttackTargets(action.Unit).Contains(target)
            && AIActionEvaluator.CanFinishSpecialTarget(action.Unit, target, board);
    }
    bool IsImmediateRescue(AIAction action, AIBoardState board)
    {
        var actor = action.Unit;
        if (actor == null || !actor.IsAlive || actor.type != Type.Unit || action.APCost > board.EnemyAP
            || StatusEffectSystem.IsStunned(actor)) return false;
        if (action.ActionType == AIActionType.SkillUse && action.TargetUnit != null
            && action.TargetUnit.team == board.ActorTeam && action.TargetUnit.IsAlive)
        {
            var patient = action.TargetUnit;
            int healing = action.Skill?.FixedHeal ?? 0;
            if (action.SpecialCommand != null && SpecialSkillRuntime.Definition(actor)?.ability == UnitSpecialSkill.WhiteMageHeal
                && AIActionEvaluator.CalcSpecialSkillScore(action, board) > 0)
                healing = SpecialSkillRuntime.FractionOfMaximum(patient, SpecialSkillDefinition.WhiteMageHealFraction);
            float incoming = IncomingDamage(board, patient, patient.transform.position, null);
            if (healing > 0 && incoming >= patient.HP
                && incoming < System.Math.Min(patient.MaxHP, (long)patient.HP + healing)) return true;
        }
        // Saving the loss-condition target is an immediate rescue too. Otherwise
        // a defender's escape (P0) could eliminate a legal base-saving kill (P1)
        // before their scores can be compared.
        if (action.ActionType == AIActionType.Attack && action.TargetUnit != null
            && board.IsSpecialActorObserved(action.TargetUnit)
            && board.GetAttackTargets(actor).Contains(action.TargetUnit)
            && AIActionEvaluator.CanFinishSpecialTarget(actor, action.TargetUnit, board))
            foreach (var target in critical)
                if (target != null && target.IsAlive
                    && IncomingDamage(board, target, target.transform.position, null) >= target.HP
                    && IncomingDamage(board, target, target.transform.position, action.TargetUnit) < target.HP)
                    return true;
        float danger = IncomingDamage(board, actor, actor.transform.position, null);
        if (danger < actor.HP) return false;
        if (IsMovement(action)) return !StatusEffectSystem.IsMovementBlocked(actor)
            && !SpecialSkillRuntime.MovementBlocked(actor) && Risk(action, board) < actor.HP;
        // Eliminating an observed attacker may save the unit without sacrificing an attack.
        if (action.ActionType == AIActionType.Attack) return Risk(action, board) < actor.HP;
        return false;
    }
}
